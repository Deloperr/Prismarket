using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Prismarket.Application.Common;
using Prismarket.Domain.Entities;
using Prismarket.Domain.Enums;

namespace Prismarket.Application.Features.Cart;

public sealed record CartLineDto(
    int ProductId, int GameId, string GameSlug, string GameTitle, string? ImageUrl, string Platform,
    ProductKind Kind, string Edition, decimal Price, int DiscountPercent, decimal FinalPrice,
    int Quantity, int InStock, decimal LineTotal);

public sealed record CartDto(IReadOnlyList<CartLineDto> Items, int TotalQuantity, decimal Subtotal, decimal Total,
    decimal Savings);

public sealed record CartItemRequest([Range(1, int.MaxValue)] int ProductId, [Range(1, CartService.MaxQuantity)] int Quantity = 1);

public interface ICartService
{
    Task<CartDto> GetAsync(CancellationToken ct);
    Task<CartDto> AddAsync(CartItemRequest request, CancellationToken ct);
    Task<CartDto> SetQuantityAsync(int productId, int quantity, CancellationToken ct);
    Task<CartDto> RemoveAsync(int productId, CancellationToken ct);
    Task ClearAsync(CancellationToken ct);
    Task<CartDto> MergeAsync(IReadOnlyList<CartItemRequest> guestItems, CancellationToken ct);
}

public sealed class CartService(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock) : ICartService
{
    public const int MaxQuantity = 10;

    public async Task<CartDto> GetAsync(CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var lines = await db.CartItems.AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.AddedAt)
            .Select(c => new
            {
                c.ProductId, c.Product.GameId, c.Product.Game.Slug, c.Product.Game.Title,
                Image = c.Product.Game.HeaderImageUrl ?? c.Product.Game.CoverImageUrl,
                Platform = c.Product.Game.Platform.Name,
                c.Product.Kind, c.Product.Edition, c.Product.Price, c.Product.DiscountPercent, c.Quantity,
                InStock = c.Product.Keys.Count(k => k.Status == StockItemStatus.Available)
                          + c.Product.Accounts.Count(a => a.Status == StockItemStatus.Available)
            })
            .ToListAsync(ct);

        var items = lines.Select(l =>
        {
            var final = Product.ApplyDiscount(l.Price, l.DiscountPercent);
            return new CartLineDto(l.ProductId, l.GameId, l.Slug, l.Title, l.Image, l.Platform, l.Kind, l.Edition,
                l.Price, l.DiscountPercent, final, l.Quantity, l.InStock, final * l.Quantity);
        }).ToList();

        var subtotal = items.Sum(i => i.Price * i.Quantity);
        var total = items.Sum(i => i.LineTotal);
        return new CartDto(items, items.Sum(i => i.Quantity), subtotal, total, subtotal - total);
    }

    public async Task<CartDto> AddAsync(CartItemRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var existing = await db.CartItems.FirstOrDefaultAsync(
            c => c.UserId == userId && c.ProductId == request.ProductId, ct);
        var newQuantity = (existing?.Quantity ?? 0) + request.Quantity;
        await EnsureCanBuyAsync(request.ProductId, newQuantity, ct);

        var now = clock.GetUtcNow().UtcDateTime;
        if (existing is null)
        {
            db.CartItems.Add(new CartItem
            {
                UserId = userId, ProductId = request.ProductId, Quantity = newQuantity, AddedAt = now, UpdatedAt = now
            });
        }
        else
        {
            existing.Quantity = newQuantity;
            existing.UpdatedAt = now;
            existing.ReminderSentAt = null;
        }

        await db.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }

    public async Task<CartDto> SetQuantityAsync(int productId, int quantity, CancellationToken ct)
    {
        if (quantity <= 0) return await RemoveAsync(productId, ct);
        var userId = currentUser.RequireUserId();
        var item = await db.CartItems.FirstOrDefaultAsync(c => c.UserId == userId && c.ProductId == productId, ct)
                   ?? throw NotFoundException.For("Позиция корзины", productId);
        await EnsureCanBuyAsync(productId, quantity, ct);
        item.Quantity = quantity;
        item.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }

    public async Task<CartDto> RemoveAsync(int productId, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        await db.CartItems.Where(c => c.UserId == userId && c.ProductId == productId).ExecuteDeleteAsync(ct);
        return await GetAsync(ct);
    }

    public async Task ClearAsync(CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        await db.CartItems.Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);
    }

    public async Task<CartDto> MergeAsync(IReadOnlyList<CartItemRequest> guestItems, CancellationToken ct)
    {
        foreach (var item in guestItems.Where(i => i.Quantity > 0).GroupBy(i => i.ProductId))
        {
            try
            {
                await AddAsync(new CartItemRequest(item.Key, Math.Min(MaxQuantity, item.Sum(i => i.Quantity))), ct);
            }
            catch (AppException)
            {
                // Guest cart may contain products that are gone or out of stock — skip them silently.
            }
        }
        return await GetAsync(ct);
    }

    private async Task EnsureCanBuyAsync(int productId, int quantity, CancellationToken ct)
    {
        if (quantity > MaxQuantity)
            throw new BusinessRuleException($"Можно купить не более {MaxQuantity} копий одного товара.");

        var product = await db.Products.AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new
            {
                p.IsAvailable, GameAvailable = p.Game.IsAvailable,
                Stock = p.Keys.Count(k => k.Status == StockItemStatus.Available)
                        + p.Accounts.Count(a => a.Status == StockItemStatus.Available)
            })
            .FirstOrDefaultAsync(ct) ?? throw NotFoundException.For("Товар", productId);

        if (!product.IsAvailable || !product.GameAvailable)
            throw new BusinessRuleException("Товар сейчас недоступен для покупки.");
        if (product.Stock < quantity)
            throw new BusinessRuleException($"В наличии только {product.Stock} шт.");
    }
}

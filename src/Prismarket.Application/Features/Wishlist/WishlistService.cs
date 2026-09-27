using Microsoft.EntityFrameworkCore;
using Prismarket.Application.Common;
using Prismarket.Application.Features.Catalog;
using Prismarket.Domain.Entities;

namespace Prismarket.Application.Features.Wishlist;

public sealed record WishlistItemDto(int Id, DateTime AddedAt, decimal? PriceWhenAdded, GameCardDto Game);

public interface IWishlistService
{
    Task<IReadOnlyList<WishlistItemDto>> GetAsync(CancellationToken ct);
    Task<IReadOnlyList<int>> GetGameIdsAsync(CancellationToken ct);
    Task AddAsync(int gameId, CancellationToken ct);
    Task RemoveAsync(int gameId, CancellationToken ct);
}

public sealed class WishlistService(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock) : IWishlistService
{
    public async Task<IReadOnlyList<WishlistItemDto>> GetAsync(CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var items = await db.WishlistItems.AsNoTracking()
            .Where(w => w.UserId == userId)
            .OrderByDescending(w => w.AddedAt)
            .Select(w => new { w.Id, w.AddedAt, w.LastKnownPrice, w.GameId })
            .ToListAsync(ct);

        var gameIds = items.Select(i => i.GameId).ToList();
        var cards = (await db.Games.AsNoTracking().Where(g => gameIds.Contains(g.Id))
                .Select(CatalogService.CardProjection).ToListAsync(ct))
            .ToDictionary(g => g.Id, CatalogService.ToCard);

        return items.Where(i => cards.ContainsKey(i.GameId))
            .Select(i => new WishlistItemDto(i.Id, i.AddedAt, i.LastKnownPrice, cards[i.GameId]))
            .ToList();
    }

    public async Task<IReadOnlyList<int>> GetGameIdsAsync(CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        return await db.WishlistItems.Where(w => w.UserId == userId).Select(w => w.GameId).ToListAsync(ct);
    }

    public async Task AddAsync(int gameId, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        if (await db.WishlistItems.AnyAsync(w => w.UserId == userId && w.GameId == gameId, ct)) return;

        var game = await db.Games.Include(g => g.Products).FirstOrDefaultAsync(g => g.Id == gameId, ct)
                   ?? throw NotFoundException.For("Игра", gameId);

        db.WishlistItems.Add(new WishlistItem
        {
            UserId = userId, GameId = gameId, AddedAt = clock.GetUtcNow().UtcDateTime,
            // Baseline for the price-drop automation.
            LastKnownPrice = game.MinPrice()
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(int gameId, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        await db.WishlistItems.Where(w => w.UserId == userId && w.GameId == gameId).ExecuteDeleteAsync(ct);
    }
}

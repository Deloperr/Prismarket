using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Prismarket.Application.Common;
using Prismarket.Application.Features.Catalog;
using Prismarket.Domain.Entities;
using Prismarket.Domain.Enums;

namespace Prismarket.Application.Features.Admin;

public sealed record PromotionInput(
    [Required, StringLength(120)] string Name,
    [StringLength(500)] string? Description,
    [Range(1, 95)] int DiscountPercent,
    DateTime StartsAt,
    DateTime EndsAt,
    string? BannerImageUrl,
    IReadOnlyList<int> ProductIds);

public sealed record PromotionDto(int Id, string Name, string? Description, int DiscountPercent, DateTime StartsAt,
    DateTime EndsAt, PromotionStatus Status, string? BannerImageUrl, IReadOnlyList<PromotionProductDto> Products);

public sealed record PromotionProductDto(int ProductId, string GameTitle, string Edition, decimal Price);

public sealed record PromoCodeInput(
    [Required, StringLength(32, MinimumLength = 3), RegularExpression("^[A-Za-z0-9_-]+$")] string Code,
    [Range(1, 100)] int DiscountPercent,
    int? MaxUses,
    DateTime? ExpiresAt,
    bool IsActive = true);

public sealed record PromoCodeDto(int Id, string Code, int DiscountPercent, int? MaxUses, int UsedCount,
    DateTime? ExpiresAt, bool IsActive, string? Owner, string? Source, DateTime CreatedAt);

public interface IPromotionAdminService
{
    Task<IReadOnlyList<PromotionDto>> ListAsync(CancellationToken ct);
    Task<PromotionDto> CreateAsync(PromotionInput input, CancellationToken ct);
    Task<PromotionDto> UpdateAsync(int id, PromotionInput input, CancellationToken ct);
    Task CancelAsync(int id, CancellationToken ct);

    Task<PagedResult<PromoCodeDto>> ListPromoCodesAsync(PageRequest page, CancellationToken ct);
    Task<PromoCodeDto> CreatePromoCodeAsync(PromoCodeInput input, CancellationToken ct);
    Task<PromoCodeDto> UpdatePromoCodeAsync(int id, PromoCodeInput input, CancellationToken ct);
    Task DeletePromoCodeAsync(int id, CancellationToken ct);
}

/// <summary>
/// Admins only schedule promotions; activation and rollback of discounts is done by the
/// <c>promotions-scheduler</c> automation at the exact start/end time.
/// </summary>
public sealed class PromotionAdminService(IAppDbContext db, ICacheService cache) : IPromotionAdminService
{
    public async Task<IReadOnlyList<PromotionDto>> ListAsync(CancellationToken ct)
    {
        var promotions = await db.Promotions.AsNoTracking()
            .Include(p => p.Products).ThenInclude(pp => pp.Product).ThenInclude(p => p.Game)
            .OrderByDescending(p => p.StartsAt).AsSplitQuery().ToListAsync(ct);
        return promotions.Select(ToDto).ToList();
    }

    public async Task<PromotionDto> CreateAsync(PromotionInput input, CancellationToken ct)
    {
        Validate(input);
        var promotion = new Promotion();
        Apply(promotion, input);
        await SetProductsAsync(promotion, input.ProductIds, ct);
        db.Promotions.Add(promotion);
        await db.SaveChangesAsync(ct);
        return await GetAsync(promotion.Id, ct);
    }

    public async Task<PromotionDto> UpdateAsync(int id, PromotionInput input, CancellationToken ct)
    {
        Validate(input);
        var promotion = await db.Promotions.Include(p => p.Products).FirstOrDefaultAsync(p => p.Id == id, ct)
                        ?? throw NotFoundException.For("Акция", id);
        if (promotion.Status != PromotionStatus.Scheduled)
            throw new BusinessRuleException("Редактировать можно только запланированную акцию.");
        Apply(promotion, input);
        await SetProductsAsync(promotion, input.ProductIds, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task CancelAsync(int id, CancellationToken ct)
    {
        var promotion = await db.Promotions.Include(p => p.Products).ThenInclude(pp => pp.Product)
                            .FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw NotFoundException.For("Акция", id);
        if (promotion.Status == PromotionStatus.Active)
        {
            foreach (var pp in promotion.Products)
                pp.Product.DiscountPercent = pp.PreviousDiscountPercent ?? 0;
        }
        promotion.Status = PromotionStatus.Cancelled;
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(CatalogService.HomeCacheKey, ct);
    }

    public async Task<PagedResult<PromoCodeDto>> ListPromoCodesAsync(PageRequest page, CancellationToken ct) =>
        await db.PromoCodes.AsNoTracking().OrderByDescending(p => p.CreatedAt)
            .Select(p => new PromoCodeDto(p.Id, p.Code, p.DiscountPercent, p.MaxUses, p.UsedCount, p.ExpiresAt,
                p.IsActive, db.Users.Where(u => u.Id == p.UserId).Select(u => u.Username).FirstOrDefault(), p.Source,
                p.CreatedAt))
            .ToPagedAsync(page, ct);

    public async Task<PromoCodeDto> CreatePromoCodeAsync(PromoCodeInput input, CancellationToken ct)
    {
        var code = input.Code.Trim().ToUpperInvariant();
        if (await db.PromoCodes.AnyAsync(p => p.Code == code, ct)) throw new ConflictException("Такой промокод уже существует.");
        var promo = new PromoCode { Code = code, Source = "admin" };
        ApplyCode(promo, input);
        db.PromoCodes.Add(promo);
        await db.SaveChangesAsync(ct);
        return ToDto(promo);
    }

    public async Task<PromoCodeDto> UpdatePromoCodeAsync(int id, PromoCodeInput input, CancellationToken ct)
    {
        var promo = await db.PromoCodes.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw NotFoundException.For("Промокод", id);
        ApplyCode(promo, input);
        await db.SaveChangesAsync(ct);
        return ToDto(promo);
    }

    public async Task DeletePromoCodeAsync(int id, CancellationToken ct)
    {
        var promo = await db.PromoCodes.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw NotFoundException.For("Промокод", id);
        if (await db.Orders.AnyAsync(o => o.PromoCodeId == id, ct)) promo.IsActive = false;
        else db.PromoCodes.Remove(promo);
        await db.SaveChangesAsync(ct);
    }

    private async Task<PromotionDto> GetAsync(int id, CancellationToken ct)
    {
        var promotion = await db.Promotions.AsNoTracking()
            .Include(p => p.Products).ThenInclude(pp => pp.Product).ThenInclude(p => p.Game)
            .FirstAsync(p => p.Id == id, ct);
        return ToDto(promotion);
    }

    private static void Validate(PromotionInput input)
    {
        if (input.EndsAt <= input.StartsAt) throw new BusinessRuleException("Дата окончания должна быть позже даты начала.");
        if (input.ProductIds.Count == 0) throw new BusinessRuleException("Выберите хотя бы один товар.");
    }

    private static void Apply(Promotion promotion, PromotionInput input)
    {
        promotion.Name = input.Name.Trim();
        promotion.Description = input.Description;
        promotion.DiscountPercent = input.DiscountPercent;
        promotion.StartsAt = DateTime.SpecifyKind(input.StartsAt.ToUniversalTime(), DateTimeKind.Utc);
        promotion.EndsAt = DateTime.SpecifyKind(input.EndsAt.ToUniversalTime(), DateTimeKind.Utc);
        promotion.BannerImageUrl = input.BannerImageUrl;
    }

    private async Task SetProductsAsync(Promotion promotion, IReadOnlyList<int> productIds, CancellationToken ct)
    {
        var existing = await db.Products.Where(p => productIds.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct);
        promotion.Products.Clear();
        foreach (var id in existing.Distinct()) promotion.Products.Add(new PromotionProduct { ProductId = id });
    }

    private static void ApplyCode(PromoCode promo, PromoCodeInput input)
    {
        promo.DiscountPercent = input.DiscountPercent;
        promo.MaxUses = input.MaxUses;
        promo.ExpiresAt = input.ExpiresAt is null ? null : DateTime.SpecifyKind(input.ExpiresAt.Value.ToUniversalTime(), DateTimeKind.Utc);
        promo.IsActive = input.IsActive;
    }

    private static PromotionDto ToDto(Promotion p) => new(p.Id, p.Name, p.Description, p.DiscountPercent, p.StartsAt,
        p.EndsAt, p.Status, p.BannerImageUrl,
        p.Products.Select(pp => new PromotionProductDto(pp.ProductId, pp.Product.Game.Title, pp.Product.Edition,
            pp.Product.Price)).ToList());

    private static PromoCodeDto ToDto(PromoCode p) => new(p.Id, p.Code, p.DiscountPercent, p.MaxUses, p.UsedCount,
        p.ExpiresAt, p.IsActive, null, p.Source, p.CreatedAt);
}

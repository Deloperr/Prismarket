using Microsoft.EntityFrameworkCore;
using Prismarket.Application.Common;
using Prismarket.Application.Features.Catalog;
using Prismarket.Application.Features.Notifications;
using Prismarket.Application.Features.Orders;
using Prismarket.Domain.Enums;

namespace Prismarket.Application.Features.Automation.Jobs;

/// <summary>Cancels unpaid orders after the reservation window and returns keys to stock.</summary>
public sealed class OrderExpiryJob(IAppDbContext db, IOrderService orders, INotificationService notifications,
    TimeProvider clock) : IAutomationJob
{
    public string Key => AutomationKeys.OrderExpiry;

    public async Task<AutomationJobResult> ExecuteAsync(JobSettings settings, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var expired = await db.Orders.Include(o => o.Payments)
            .Where(o => o.Status == OrderStatus.AwaitingPayment && o.ExpiresAt <= now)
            .OrderBy(o => o.ExpiresAt).Take(200)
            .ToListAsync(ct);
        if (expired.Count == 0) return AutomationJobResult.Nothing();

        foreach (var order in expired)
        {
            await orders.ExpireAsync(order, OrderStatus.Expired, "Истекло время оплаты", ct);
            notifications.Add(order.UserId, NotificationType.System, "Заказ отменён",
                $"Заказ {order.Number} не был оплачен вовремя, ключи вернулись в продажу.", "/profile?tab=orders");
        }
        await db.SaveChangesAsync(ct);
        await notifications.FlushAsync(ct);
        return new AutomationJobResult(expired.Count, $"Отменено заказов: {expired.Count}");
    }
}

/// <summary>Applies discounts when promotions start and restores previous prices when they end.</summary>
public sealed class PromotionsSchedulerJob(IAppDbContext db, ICacheService cache, TimeProvider clock) : IAutomationJob
{
    public string Key => AutomationKeys.PromotionsScheduler;

    public async Task<AutomationJobResult> ExecuteAsync(JobSettings settings, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var started = 0;
        var finished = 0;

        var toStart = await db.Promotions.Include(p => p.Products).ThenInclude(pp => pp.Product)
            .Where(p => p.Status == PromotionStatus.Scheduled && p.StartsAt <= now && p.EndsAt > now)
            .ToListAsync(ct);
        foreach (var promotion in toStart)
        {
            foreach (var pp in promotion.Products)
            {
                pp.PreviousDiscountPercent = pp.Product.DiscountPercent;
                // Never make a price worse than an existing bigger discount.
                pp.Product.DiscountPercent = Math.Max(pp.Product.DiscountPercent, promotion.DiscountPercent);
            }
            promotion.Status = PromotionStatus.Active;
            started++;
        }

        var toFinish = await db.Promotions.Include(p => p.Products).ThenInclude(pp => pp.Product)
            .Where(p => (p.Status == PromotionStatus.Active && p.EndsAt <= now)
                        || (p.Status == PromotionStatus.Scheduled && p.EndsAt <= now))
            .ToListAsync(ct);
        foreach (var promotion in toFinish)
        {
            if (promotion.Status == PromotionStatus.Active)
                foreach (var pp in promotion.Products)
                    pp.Product.DiscountPercent = pp.PreviousDiscountPercent ?? 0;
            promotion.Status = PromotionStatus.Finished;
            finished++;
        }

        if (started + finished == 0) return AutomationJobResult.Nothing();
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(CatalogService.HomeCacheKey, ct);
        return new AutomationJobResult(started + finished, $"Запущено акций: {started}, завершено: {finished}");
    }
}

/// <summary>Picks top sellers for the home page and recalculates denormalized ratings.</summary>
public sealed class FeaturedRotationJob(IAppDbContext db, ICacheService cache, TimeProvider clock) : IAutomationJob
{
    public string Key => AutomationKeys.FeaturedRotation;

    public async Task<AutomationJobResult> ExecuteAsync(JobSettings settings, CancellationToken ct)
    {
        var since = clock.GetUtcNow().UtcDateTime.AddDays(-settings.GetInt("periodDays"));
        var top = settings.GetInt("topCount");

        var topGameIds = await db.OrderItems
            .Where(i => i.Order.Status == OrderStatus.Completed && i.Order.CompletedAt >= since)
            .GroupBy(i => i.Product.GameId)
            .OrderByDescending(g => g.Count())
            .Take(top)
            .Select(g => g.Key)
            .ToListAsync(ct);

        if (topGameIds.Count < top)
        {
            // Not enough recent sales — fill up with the best rated games.
            var fill = await db.Games.Where(g => g.IsAvailable && !topGameIds.Contains(g.Id))
                .OrderByDescending(g => g.AverageRating).ThenByDescending(g => g.SalesCount)
                .Take(top - topGameIds.Count).Select(g => g.Id).ToListAsync(ct);
            topGameIds.AddRange(fill);
        }

        await db.Games.Where(g => g.IsFeatured && !topGameIds.Contains(g.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.IsFeatured, false), ct);
        await db.Games.Where(g => topGameIds.Contains(g.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.IsFeatured, true), ct);

        // Recalculate ratings for every game in one set-based UPDATE (SQL executed by EF).
        var ratings = await db.Reviews.Where(r => r.Status == ReviewStatus.Published)
            .GroupBy(r => r.GameId)
            .Select(g => new { GameId = g.Key, Avg = g.Average(r => (decimal)r.Rating), Count = g.Count() })
            .ToListAsync(ct);
        foreach (var rating in ratings)
        {
            await db.Games.Where(g => g.Id == rating.GameId).ExecuteUpdateAsync(s => s
                .SetProperty(g => g.AverageRating, Math.Round(rating.Avg, 2))
                .SetProperty(g => g.RatingsCount, rating.Count), ct);
        }

        await cache.RemoveAsync(CatalogService.HomeCacheKey, ct);
        return new AutomationJobResult(topGameIds.Count,
            $"На витрине {topGameIds.Count} игр, пересчитано рейтингов: {ratings.Count}");
    }
}

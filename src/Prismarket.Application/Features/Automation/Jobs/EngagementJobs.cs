using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Prismarket.Application.Common;
using Prismarket.Application.Features.Catalog;
using Prismarket.Application.Features.Notifications;
using Prismarket.Domain.Entities;
using Prismarket.Domain.Enums;

namespace Prismarket.Application.Features.Automation.Jobs;

/// <summary>
/// Watches key stock: warns admins about low stock, hides sold-out products and brings them back
/// (notifying wishlist subscribers) after restock.
/// </summary>
public sealed class StockMonitorJob(
    IAppDbContext db,
    INotificationService notifications,
    IEmailQueue emails,
    IAppUrls urls,
    ICacheService cache,
    TimeProvider clock) : IAutomationJob
{
    public string Key => AutomationKeys.StockMonitor;

    public async Task<AutomationJobResult> ExecuteAsync(JobSettings settings, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var defaultThreshold = settings.GetInt("lowStockThreshold");
        var autoHide = settings.GetBool("autoHide");
        var notifyWishlist = settings.GetBool("notifyWishlist");

        var products = await db.Products
            .Where(p => p.Game.IsAvailable && (p.IsAvailable || p.HiddenByStockMonitor))
            .Select(p => new
            {
                Product = p,
                p.Game.Title,
                p.Game.Slug,
                Stock = p.Kind == ProductKind.Account
                    ? p.Accounts.Count(a => a.Status == StockItemStatus.Available)
                    : p.Keys.Count(k => k.Status == StockItemStatus.Available)
            })
            .ToListAsync(ct);

        int hidden = 0, restored = 0, warned = 0;
        var lowStockLines = new List<string>();

        foreach (var row in products)
        {
            var p = row.Product;
            var threshold = p.LowStockThreshold ?? defaultThreshold;

            if (row.Stock == 0 && p.IsAvailable && autoHide)
            {
                p.IsAvailable = false;
                p.HiddenByStockMonitor = true;
                hidden++;
                lowStockLines.Add($"{row.Title} ({p.Edition}) — закончились, товар скрыт");
            }
            else if (row.Stock > 0 && p.HiddenByStockMonitor)
            {
                p.IsAvailable = true;
                p.HiddenByStockMonitor = false;
                p.LowStockNotifiedAt = null;
                restored++;
                if (notifyWishlist) await NotifyBackInStockAsync(p.GameId, row.Title, row.Slug, ct);
            }
            else if (row.Stock > 0 && row.Stock <= threshold
                     && (p.LowStockNotifiedAt is null || p.LowStockNotifiedAt < now.AddHours(-24)))
            {
                p.LowStockNotifiedAt = now;
                warned++;
                lowStockLines.Add($"{row.Title} ({p.Edition}) — осталось {row.Stock} шт.");
            }
        }

        if (lowStockLines.Count > 0)
        {
            await notifications.AddForAdminsAsync(NotificationType.LowStock, "Заканчиваются ключи",
                string.Join("; ", lowStockLines.Take(5)) + (lowStockLines.Count > 5 ? "…" : ""), "/admin/games", ct);

            var adminEmails = await db.Users.Where(u => u.Role == UserRole.Admin && u.IsActive)
                .Select(u => u.Email).ToListAsync(ct);
            var body = "<ul>" + string.Join("", lowStockLines.Select(l => $"<li>{EmailTemplates.Encode(l)}</li>")) + "</ul>";
            foreach (var email in adminEmails)
                emails.Enqueue(new EmailEnvelope(email, "Prismarket: низкий остаток ключей",
                    EmailTemplates.Layout("Пора пополнить склад", body, "Открыть админку", urls.Build("/admin/games"))));
        }

        if (hidden + restored + warned == 0) return AutomationJobResult.Nothing("Остатки в норме");

        await db.SaveChangesAsync(ct);
        await notifications.FlushAsync(ct);
        await cache.RemoveAsync(CatalogService.HomeCacheKey, ct);
        return new AutomationJobResult(hidden + restored + warned,
            $"Скрыто: {hidden}, возвращено в продажу: {restored}, предупреждений: {warned}");
    }

    private async Task NotifyBackInStockAsync(int gameId, string title, string slug, CancellationToken ct)
    {
        var subscribers = await db.WishlistItems.Where(w => w.GameId == gameId)
            .Select(w => new { w.UserId, w.User.Email }).ToListAsync(ct);
        foreach (var s in subscribers)
        {
            notifications.Add(s.UserId, NotificationType.BackInStock, "Снова в наличии",
                $"«{title}» снова можно купить!", $"/game/{slug}");
            emails.Enqueue(new EmailEnvelope(s.Email, $"«{title}» снова в наличии",
                EmailTemplates.Layout("Игра из вашего списка желаемого снова в продаже",
                    $"<p>Ключи для <b>{EmailTemplates.Encode(title)}</b> снова в наличии. Успейте купить!</p>",
                    "Перейти к игре", urls.Build($"/game/{slug}"))));
        }
    }
}

/// <summary>Notifies wishlist owners when the lowest price of a game drops by N% or more.</summary>
public sealed class WishlistPriceAlertJob(
    IAppDbContext db,
    INotificationService notifications,
    IEmailQueue emails,
    IAppUrls urls,
    TimeProvider clock) : IAutomationJob
{
    public string Key => AutomationKeys.WishlistPriceAlerts;

    public async Task<AutomationJobResult> ExecuteAsync(JobSettings settings, CancellationToken ct)
    {
        var minDrop = settings.GetInt("minDropPercent");
        var now = clock.GetUtcNow().UtcDateTime;

        var items = await db.WishlistItems
            .Include(w => w.User)
            .Include(w => w.Game).ThenInclude(g => g.Products)
            .Where(w => w.Game.IsAvailable)
            .AsSplitQuery()
            .ToListAsync(ct);

        var alerts = 0;
        foreach (var item in items)
        {
            var current = item.Game.MinPrice();
            if (current is null) continue;

            if (item.LastKnownPrice is null || current > item.LastKnownPrice)
            {
                // Price went up (e.g. sale ended) — move the baseline silently.
                item.LastKnownPrice = current;
                continue;
            }

            var dropPercent = PriceDrop.Percent(item.LastKnownPrice.Value, current.Value);
            if (dropPercent < minDrop) continue;

            notifications.Add(item.UserId, NotificationType.PriceDrop, "Цена снизилась!",
                $"«{item.Game.Title}» подешевела на {dropPercent}%: {current:0.00} BYN", $"/game/{item.Game.Slug}");
            emails.Enqueue(new EmailEnvelope(item.User.Email, $"Скидка на «{item.Game.Title}»",
                EmailTemplates.Layout("Игра из вашего списка желаемого подешевела",
                    $"<p><b>{EmailTemplates.Encode(item.Game.Title)}</b>: " +
                    $"<s style=\"color:#8a8fb8\">{item.LastKnownPrice:0.00}</s> → <b style=\"color:#7dffb3\">{current:0.00} BYN</b> (−{dropPercent}%)</p>",
                    "Купить со скидкой", urls.Build($"/game/{item.Game.Slug}"))));

            item.LastKnownPrice = current;
            item.LastNotifiedAt = now;
            alerts++;
        }

        await db.SaveChangesAsync(ct);
        await notifications.FlushAsync(ct);
        return alerts == 0
            ? AutomationJobResult.Nothing($"Проверено позиций: {items.Count}, снижений нет")
            : new AutomationJobResult(alerts, $"Отправлено уведомлений о снижении цены: {alerts}");
    }
}

public static class PriceDrop
{
    /// <summary>Percentage decrease from <paramref name="before"/> to <paramref name="after"/> (0 if not cheaper).</summary>
    public static int Percent(decimal before, decimal after) =>
        before <= 0 || after >= before ? 0 : (int)Math.Floor((before - after) / before * 100m);
}

/// <summary>Reminds users about items that have been sitting in the cart for a long time.</summary>
public sealed class AbandonedCartJob(IAppDbContext db, IEmailQueue emails, INotificationService notifications,
    IAppUrls urls, TimeProvider clock) : IAutomationJob
{
    public string Key => AutomationKeys.AbandonedCart;

    public async Task<AutomationJobResult> ExecuteAsync(JobSettings settings, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var threshold = now.AddHours(-settings.GetInt("idleHours"));

        var carts = await db.CartItems
            .Include(c => c.User)
            .Include(c => c.Product).ThenInclude(p => p.Game)
            .Where(c => c.ReminderSentAt == null && c.UpdatedAt <= threshold && c.User.IsActive)
            .ToListAsync(ct);

        var byUser = carts.GroupBy(c => c.User).ToList();
        foreach (var group in byUser)
        {
            var user = group.Key;
            var rows = group.Select(c =>
                (EmailTemplates.Encode($"{c.Product.Game.Title} ({c.Product.Edition}) × {c.Quantity}"),
                    EmailTemplates.Money(c.Product.FinalPrice * c.Quantity)));
            emails.Enqueue(new EmailEnvelope(user.Email, "Вы кое-что забыли в корзине",
                EmailTemplates.Layout("Игры ждут вас в корзине",
                    $"<p>{EmailTemplates.Encode(user.Username)}, в вашей корзине остались товары:</p>{EmailTemplates.Table(rows)}" +
                    "<p>Ключи заканчиваются — не упустите!</p>",
                    "Перейти в корзину", urls.Build("/cart"))));
            notifications.Add(user.Id, NotificationType.Promo, "Корзина ждёт",
                "В вашей корзине остались товары.", "/cart");
            foreach (var item in group) item.ReminderSentAt = now;
        }

        if (byUser.Count == 0) return AutomationJobResult.Nothing();
        await db.SaveChangesAsync(ct);
        await notifications.FlushAsync(ct);
        return new AutomationJobResult(byUser.Count, $"Напоминаний отправлено: {byUser.Count}");
    }
}

/// <summary>Generates personal promo codes for customers who have not visited for a long time.</summary>
public sealed class WinBackJob(IAppDbContext db, IEmailQueue emails, INotificationService notifications,
    IAppUrls urls, TimeProvider clock) : IAutomationJob
{
    public string Key => AutomationKeys.WinBack;

    public async Task<AutomationJobResult> ExecuteAsync(JobSettings settings, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var inactiveSince = now.AddDays(-settings.GetInt("inactiveDays"));
        var discount = settings.GetInt("discountPercent");
        var validDays = settings.GetInt("validDays");

        var users = await db.Users
            .Where(u => u.Role == UserRole.Customer && u.IsActive && u.EmailConfirmed
                        && (u.LastLoginAt ?? u.CreatedAt) < inactiveSince
                        && (u.WinBackSentAt == null || u.WinBackSentAt < inactiveSince))
            .Take(500)
            .ToListAsync(ct);

        foreach (var user in users)
        {
            var code = $"BACK{discount}-{RandomNumberGenerator.GetHexString(6)}";
            db.PromoCodes.Add(new PromoCode
            {
                Code = code, DiscountPercent = discount, MaxUses = 1, UserId = user.Id,
                ExpiresAt = now.AddDays(validDays), Source = AutomationKeys.WinBack
            });
            user.WinBackSentAt = now;
            notifications.Add(user.Id, NotificationType.Promo, "Персональная скидка",
                $"Промокод {code} даёт −{discount}% на заказ до {now.AddDays(validDays):dd.MM.yyyy}.", "/catalog");
            emails.Enqueue(new EmailEnvelope(user.Email, $"Мы скучали! −{discount}% на следующую покупку",
                EmailTemplates.Layout("Персональный промокод для вас",
                    $"<p>{EmailTemplates.Encode(user.Username)}, давно не виделись! Дарим скидку <b>{discount}%</b> на следующий заказ.</p>" +
                    $"<p style=\"font-size:22px;letter-spacing:2px;text-align:center\"><code>{code}</code></p>" +
                    $"<p>Промокод действует {validDays} дней.</p>", "За покупками", urls.Build("/catalog"))));
        }

        if (users.Count == 0) return AutomationJobResult.Nothing();
        await db.SaveChangesAsync(ct);
        await notifications.FlushAsync(ct);
        return new AutomationJobResult(users.Count, $"Выдано персональных промокодов: {users.Count}");
    }
}

using Microsoft.EntityFrameworkCore;
using Prismarket.Application.Common;
using Prismarket.Application.Features.Automation;
using Prismarket.Application.Features.Notifications;
using Prismarket.Domain.Entities;
using Prismarket.Domain.Enums;

namespace Prismarket.Application.Features.Orders;

public interface IOrderFulfillmentService
{
    /// <summary>Delivers purchased keys/accounts for a paid order. Caller saves changes.</summary>
    Task FulfillAsync(Order order, CancellationToken ct);
}

/// <summary>
/// Event-driven automation "key-delivery" + "cashback": runs right after an order is paid.
/// Marks stock as sold, fills the library, e-mails the keys and credits cashback.
/// </summary>
public sealed class OrderFulfillmentService(
    IAppDbContext db,
    IAutomationSettingsProvider automation,
    IAutomationEventLog automationLog,
    INotificationService notifications,
    IEmailQueue emails,
    IAppUrls urls,
    TimeProvider clock) : IOrderFulfillmentService
{
    public async Task FulfillAsync(Order order, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var user = await db.Users.FirstAsync(u => u.Id == order.UserId, ct);

        var keyIds = order.Items.Where(i => i.KeyId != null).Select(i => i.KeyId!.Value).ToList();
        var accountIds = order.Items.Where(i => i.AccountId != null).Select(i => i.AccountId!.Value).ToList();
        var keys = await db.ProductKeys.Where(k => keyIds.Contains(k.Id)).ToDictionaryAsync(k => k.Id, ct);
        var accounts = await db.ProductAccounts.Where(a => accountIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        var productIds = order.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await db.Products.Include(p => p.Game)
            .Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        foreach (var item in order.Items)
        {
            if (item.KeyId is { } keyId) keys[keyId].MarkSold(now);
            if (item.AccountId is { } accountId)
            {
                accounts[accountId].Status = StockItemStatus.Sold;
                accounts[accountId].SoldAt = now;
            }

            db.LibraryItems.Add(new LibraryItem
            {
                UserId = order.UserId, ProductId = item.ProductId, OrderItem = item,
                KeyId = item.KeyId, AccountId = item.AccountId, PurchasedAt = now
            });
        }

        foreach (var group in order.Items.GroupBy(i => products[i.ProductId].GameId))
            products[group.First().ProductId].Game.SalesCount += group.Count();

        // Buying a wishlisted game removes it from the wishlist automatically.
        var gameIds = products.Values.Select(p => p.GameId).Distinct().ToList();
        var wished = await db.WishlistItems.Where(w => w.UserId == order.UserId && gameIds.Contains(w.GameId))
            .ToListAsync(ct);
        db.WishlistItems.RemoveRange(wished);

        order.Complete(now);

        notifications.Add(order.UserId, NotificationType.OrderCompleted, "Заказ выполнен",
            $"Заказ {order.Number} оплачен, ключи уже в вашей библиотеке.", "/profile?tab=library");

        // --- key-delivery rule: e-mail with keys ---
        var (deliveryEnabled, deliverySettings) = await automation.GetAsync(AutomationKeys.KeyDelivery, ct);
        if (deliveryEnabled && deliverySettings.GetBool("sendEmail"))
        {
            var rows = order.Items.Select(i =>
            {
                var p = products[i.ProductId];
                var secret = i.KeyId is { } k
                    ? $"<code style=\"font-size:15px;color:#fff\">{EmailTemplates.Encode(keys[k].Value)}</code>"
                    : $"логин <code>{EmailTemplates.Encode(accounts[i.AccountId!.Value].Login)}</code> / пароль <code>{EmailTemplates.Encode(accounts[i.AccountId!.Value].Password)}</code>";
                return ($"{EmailTemplates.Encode(p.Game.Title)} <span style=\"color:#8a8fb8\">({EmailTemplates.Encode(p.Edition)})</span>", secret);
            });
            emails.Enqueue(new EmailEnvelope(user.Email, $"Ваши ключи по заказу {order.Number}",
                EmailTemplates.Layout($"Заказ {order.Number} выполнен",
                    $"<p>Спасибо за покупку, {EmailTemplates.Encode(user.Username)}! Вот ваши товары:</p>" +
                    EmailTemplates.Table(rows) +
                    $"<p style=\"margin-top:16px\">Итого: <b>{EmailTemplates.Money(order.Total)}</b></p>",
                    "Открыть библиотеку", urls.Build("/profile?tab=library"))));
        }
        automationLog.Record(AutomationKeys.KeyDelivery, order.Items.Count,
            $"Заказ {order.Number}: выдано товаров — {order.Items.Count}");

        // --- cashback rule ---
        var (cashbackEnabled, cashbackSettings) = await automation.GetAsync(AutomationKeys.Cashback, ct);
        var percent = cashbackSettings.GetDecimal("percent");
        if (cashbackEnabled && percent > 0 && order.PaymentMethod != PaymentMethod.Balance)
        {
            var cashback = Math.Round(order.Total * percent / 100m, 2, MidpointRounding.ToZero);
            if (cashback > 0)
            {
                user.Credit(cashback);
                db.BalanceTransactions.Add(new BalanceTransaction
                {
                    UserId = user.Id, Amount = cashback, BalanceAfter = user.Balance,
                    Type = BalanceTransactionType.Cashback,
                    Description = $"Кэшбэк {percent:0.##}% за заказ {order.Number}", OrderId = order.Id, CreatedAt = now
                });
                notifications.Add(user.Id, NotificationType.Cashback, "Начислен кэшбэк",
                    $"+{cashback:0.00} BYN на баланс за заказ {order.Number}.", "/profile?tab=balance");
                automationLog.Record(AutomationKeys.Cashback, 1, $"Заказ {order.Number}: кэшбэк {cashback:0.00} BYN");
            }
        }
    }
}

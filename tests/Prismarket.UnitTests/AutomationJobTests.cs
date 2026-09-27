using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Prismarket.Application.Common;
using Prismarket.Application.Features.Automation;
using Prismarket.Application.Features.Automation.Jobs;
using Prismarket.Application.Features.Notifications;
using Prismarket.Application.Features.Orders;
using Prismarket.Domain.Entities;
using Prismarket.Domain.Enums;
using Prismarket.Infrastructure.Persistence;

namespace Prismarket.UnitTests;

/// <summary>Automation scenarios executed against the EF Core in-memory provider.</summary>
public class AutomationJobTests
{
    private static readonly DateTime Now = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
    private readonly TimeProvider _clock = new FixedClock(Now);
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IEmailQueue _emails = Substitute.For<IEmailQueue>();
    private readonly ICacheService _cache = Substitute.For<ICacheService>();
    private readonly IAppUrls _urls = Substitute.For<IAppUrls>();

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static JobSettings Settings(string key, string json = "{}") => new(json, AutomationCatalog.Get(key).Settings);

    private static Game NewGame(string title = "Test Game") => new()
    {
        Title = title, Slug = Slug.From(title),
        Developer = new Developer { Name = "Dev " + title }, Publisher = new Publisher { Name = "Pub " + title },
        Platform = new Platform { Name = "Steam " + title, Slug = "steam-" + Slug.From(title) }
    };

    [Fact]
    public async Task PromotionsScheduler_AppliesAndRestoresDiscounts()
    {
        await using var db = NewDb();
        var game = NewGame();
        var product = new Product { Game = game, Price = 100, DiscountPercent = 10 };
        var promotion = new Promotion
        {
            Name = "Sale", DiscountPercent = 40, StartsAt = Now.AddMinutes(-1), EndsAt = Now.AddHours(1),
            Products = { new PromotionProduct { Product = product } }
        };
        db.Promotions.Add(promotion);
        await db.SaveChangesAsync();

        var job = new PromotionsSchedulerJob(db, _cache, _clock);
        var started = await job.ExecuteAsync(JobSettings.Empty, default);

        Assert.Equal(1, started.ItemsProcessed);
        Assert.Equal(PromotionStatus.Active, promotion.Status);
        Assert.Equal(40, product.DiscountPercent);
        Assert.Equal(10, promotion.Products.Single().PreviousDiscountPercent);

        var later = new PromotionsSchedulerJob(db, _cache, new FixedClock(Now.AddHours(2)));
        await later.ExecuteAsync(JobSettings.Empty, default);

        Assert.Equal(PromotionStatus.Finished, promotion.Status);
        Assert.Equal(10, product.DiscountPercent);
    }

    [Fact]
    public async Task StockMonitor_HidesSoldOut_AndRestoresAfterRestock()
    {
        await using var db = NewDb();
        var game = NewGame();
        var product = new Product { Game = game, Price = 10 };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var job = new StockMonitorJob(db, _notifications, _emails, _urls, _cache, _clock);
        await job.ExecuteAsync(Settings(AutomationKeys.StockMonitor), default);

        Assert.False(product.IsAvailable);
        Assert.True(product.HiddenByStockMonitor);

        db.ProductKeys.Add(new ProductKey { ProductId = product.Id, Value = "KEY-1-AAAAA" });
        await db.SaveChangesAsync();
        await job.ExecuteAsync(Settings(AutomationKeys.StockMonitor), default);

        Assert.True(product.IsAvailable);
        Assert.False(product.HiddenByStockMonitor);
    }

    [Fact]
    public async Task StockMonitor_WarnsAdmins_OnLowStock()
    {
        await using var db = NewDb();
        var product = new Product { Game = NewGame(), Price = 10 };
        for (var i = 0; i < 3; i++) product.Keys.Add(new ProductKey { Value = $"LOW-{i}-XXXXX" });
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var result = await new StockMonitorJob(db, _notifications, _emails, _urls, _cache, _clock)
            .ExecuteAsync(Settings(AutomationKeys.StockMonitor), default);

        Assert.Equal(1, result.ItemsProcessed);
        Assert.Equal(Now, product.LowStockNotifiedAt);
        await _notifications.Received(1).AddForAdminsAsync(NotificationType.LowStock, Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WishlistPriceAlert_NotifiesOnlyOnSignificantDrop()
    {
        await using var db = NewDb();
        var user = new User { Username = "u", Email = "u@test", PasswordHash = "x" };
        var game = NewGame();
        var product = new Product { Game = game, Price = 100, DiscountPercent = 30 };
        db.Products.Add(product);
        db.WishlistItems.Add(new WishlistItem { User = user, Game = game, LastKnownPrice = 100 });
        await db.SaveChangesAsync();

        var job = new WishlistPriceAlertJob(db, _notifications, _emails, _urls, _clock);
        var result = await job.ExecuteAsync(Settings(AutomationKeys.WishlistPriceAlerts), default);

        Assert.Equal(1, result.ItemsProcessed);
        _notifications.Received(1).Add(user.Id, NotificationType.PriceDrop, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>());
        _emails.Received(1).Enqueue(Arg.Any<EmailEnvelope>());
        Assert.Equal(70, db.WishlistItems.Single().LastKnownPrice);

        // Second run: price did not change — no duplicate notification.
        var again = await job.ExecuteAsync(Settings(AutomationKeys.WishlistPriceAlerts), default);
        Assert.Equal(0, again.ItemsProcessed);
    }

    [Fact]
    public async Task Fulfillment_DeliversKeys_FillsLibrary_AndCreditsCashback()
    {
        await using var db = NewDb();
        var user = new User { Username = "buyer", Email = "b@test", PasswordHash = "x" };
        var game = NewGame();
        var product = new Product { Game = game, Price = 100 };
        var key = new ProductKey { Product = product, Value = "AAAAA-BBBBB-CCCCC", Status = StockItemStatus.Reserved };
        var order = new Order
        {
            Number = "PM-TEST-1", User = user, PaymentMethod = PaymentMethod.Card, Total = 100, Subtotal = 100,
            ExpiresAt = Now.AddMinutes(15), Items = { new OrderItem { Product = product, Key = key, UnitPrice = 100, FinalPrice = 100 } }
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        order.MarkPaid(Now);

        var settings = Substitute.For<IAutomationSettingsProvider>();
        settings.GetAsync(AutomationKeys.KeyDelivery, Arg.Any<CancellationToken>())
            .Returns((true, Settings(AutomationKeys.KeyDelivery)));
        settings.GetAsync(AutomationKeys.Cashback, Arg.Any<CancellationToken>())
            .Returns((true, Settings(AutomationKeys.Cashback, """{"percent":5}""")));

        var service = new OrderFulfillmentService(db, settings, Substitute.For<IAutomationEventLog>(), _notifications,
            _emails, _urls, _clock);
        await service.FulfillAsync(order, default);
        await db.SaveChangesAsync();

        Assert.Equal(OrderStatus.Completed, order.Status);
        Assert.Equal(StockItemStatus.Sold, key.Status);
        Assert.Single(db.LibraryItems);
        Assert.Equal(1, game.SalesCount);
        Assert.Equal(5m, user.Balance);
        Assert.Contains(db.BalanceTransactions, t => t.Type == BalanceTransactionType.Cashback && t.Amount == 5m);
        _emails.Received(1).Enqueue(Arg.Is<EmailEnvelope>(e => e.HtmlBody.Contains("AAAAA-BBBBB-CCCCC")));
    }

    [Fact]
    public async Task Fulfillment_NoCashback_ForBalancePayments()
    {
        await using var db = NewDb();
        var user = new User { Username = "buyer", Email = "b@test", PasswordHash = "x" };
        var product = new Product { Game = NewGame(), Price = 50 };
        var key = new ProductKey { Product = product, Value = "ZZZZZ-YYYYY-XXXXX", Status = StockItemStatus.Reserved };
        var order = new Order
        {
            Number = "PM-TEST-2", User = user, PaymentMethod = PaymentMethod.Balance, Total = 50, Subtotal = 50,
            Items = { new OrderItem { Product = product, Key = key, FinalPrice = 50 } }
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        order.MarkPaid(Now);

        var settings = Substitute.For<IAutomationSettingsProvider>();
        settings.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => (true, Settings(ci.Arg<string>())));

        await new OrderFulfillmentService(db, settings, Substitute.For<IAutomationEventLog>(), _notifications, _emails,
            _urls, _clock).FulfillAsync(order, default);

        Assert.Equal(0m, user.Balance);
    }

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
    }
}

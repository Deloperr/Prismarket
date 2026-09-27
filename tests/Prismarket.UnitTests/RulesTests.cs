using System.Security.Cryptography;
using System.Text;
using Prismarket.Application.Common;
using Prismarket.Application.Features.Automation;
using Prismarket.Application.Features.Automation.Jobs;
using Prismarket.Application.Features.Orders;
using Prismarket.Application.Features.Reports;
using Prismarket.Application.Features.Reviews;
using Prismarket.Application.Features.Support;
using Prismarket.Domain.Enums;
using Prismarket.Infrastructure.Payments;
using Prismarket.Infrastructure.Persistence;

namespace Prismarket.UnitTests;

public class RulesTests
{
    [Fact]
    public void JobSettings_UsesDefaults_WhenValueMissing()
    {
        var settings = new JobSettings("{}", AutomationCatalog.Get(AutomationKeys.StockMonitor).Settings);
        Assert.Equal(5, settings.GetInt("lowStockThreshold"));
        Assert.True(settings.GetBool("autoHide"));
    }

    [Fact]
    public void JobSettings_OverridesAndParsesStrings()
    {
        var settings = new JobSettings("""{"lowStockThreshold":"12","autoHide":false}""",
            AutomationCatalog.Get(AutomationKeys.StockMonitor).Settings);
        Assert.Equal(12, settings.GetInt("lowStockThreshold"));
        Assert.False(settings.GetBool("autoHide"));
    }

    [Fact]
    public void JobSettings_FallsBackOnGarbage()
    {
        var settings = new JobSettings("""{"percent":"abc"}""", AutomationCatalog.Get(AutomationKeys.Cashback).Settings);
        Assert.Equal(3m, settings.GetDecimal("percent"));
    }

    [Fact]
    public void AutomationCatalog_HasUniqueKeys_AndScheduledJobsHaveCron()
    {
        Assert.Equal(AutomationCatalog.All.Count, AutomationCatalog.All.Select(a => a.Key).Distinct().Count());
        Assert.All(AutomationCatalog.All.Where(a => a.Kind == AutomationKind.Scheduled),
            a => Assert.True(CronValidator.IsValid(a.DefaultCron!), a.Key));
    }

    [Theory]
    [InlineData("* * * * *", true)]
    [InlineData("*/15 * * * *", true)]
    [InlineData("0 8 * * 1-5", true)]
    [InlineData("0 8 * *", false)]
    [InlineData("every minute", false)]
    public void CronValidator_Works(string cron, bool valid) => Assert.Equal(valid, CronValidator.IsValid(cron));

    [Theory]
    [InlineData("Отличная игра", true)]
    [InlineData("Заходите в наше казино!", false)]
    [InlineData("пишите в t.me/scam", false)]
    public void ReviewModerator_DetectsStopWords(string comment, bool approved)
    {
        var settings = new JobSettings("{}", AutomationCatalog.Get(AutomationKeys.ReviewModeration).Settings);
        Assert.Equal(approved, new ReviewModerator().Check("Отзыв", comment, settings).Approved);
    }

    [Fact]
    public void ReviewModerator_RespectsMinLength()
    {
        var settings = new JobSettings("""{"minLength":20,"bannedWords":""}""",
            AutomationCatalog.Get(AutomationKeys.ReviewModeration).Settings);
        var verdict = new ReviewModerator().Check(null, "Коротко", settings);
        Assert.False(verdict.Approved);
        Assert.Contains("20", verdict.Reason);
    }

    [Theory]
    [InlineData("Не пришёл ключ", "Ключ не активируется в Steam", TicketCategory.KeyActivation)]
    [InlineData("Деньги списали", "Оплатил картой, заказа нет", TicketCategory.Payment)]
    [InlineData("Хочу вернуть деньги", "Верните пожалуйста", TicketCategory.Refund)]
    [InlineData("Забыл пароль", "не могу войти", TicketCategory.Account)]
    [InlineData("Привет", "Когда будет распродажа?", TicketCategory.General)]
    public void SupportBot_ClassifiesTickets(string subject, string message, TicketCategory expected)
    {
        var answer = new KeywordSupportBot().Analyze(subject, message);
        Assert.Equal(expected, answer.Category);
        Assert.Equal(expected == TicketCategory.General, answer.Reply is null);
    }

    [Theory]
    [InlineData(100, 90, 10)]
    [InlineData(100, 100, 0)]
    [InlineData(100, 120, 0)]
    [InlineData(59.90, 38.94, 34)]
    public void PriceDrop_Percent(decimal before, decimal after, int expected) =>
        Assert.Equal(expected, PriceDrop.Percent(before, after));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(4, 8)]
    [InlineData(10, 60)]
    public void EmailOutbox_ExponentialBackoff(int attempt, int minutes) =>
        Assert.Equal(TimeSpan.FromMinutes(minutes), EmailOutboxJob.Backoff(attempt));

    [Theory]
    [InlineData(100, 10, 10)]
    [InlineData(33.33, 15, 5)]
    [InlineData(10, 150, 10)]
    public void PromoDiscount_IsClampedAndRounded(decimal subtotal, int percent, decimal expected) =>
        Assert.Equal(expected, OrderService.CalculatePromoDiscount(subtotal, percent));

    [Theory]
    [InlineData("Cyberpunk 2077", "cyberpunk-2077")]
    [InlineData("Baldur's Gate 3", "baldur-s-gate-3")]
    [InlineData("Ведьмак 3: Дикая Охота", "vedmak-3-dikaya-ohota")]
    public void Slug_IsUrlFriendly(string title, string expected) => Assert.Equal(expected, Slug.From(title));

    [Theory]
    [InlineData("ProductKeys", "product_keys")]
    [InlineData("ReservedByOrderId", "reserved_by_order_id")]
    [InlineData("Id", "id")]
    [InlineData("PK_Users", "pk_users")]
    public void SnakeCase_Conversion(string input, string expected) => Assert.Equal(expected, AppDbContext.ToSnakeCase(input));

    [Fact]
    public void Csv_EscapesSeparatorsAndQuotes()
    {
        var bytes = ExportService.ToCsv([new Dictionary<string, object?> { ["title"] = "Game; \"Deluxe\"", ["price"] = 9.5m }]);
        var text = Encoding.UTF8.GetString(bytes).TrimStart('﻿');
        Assert.Contains("title;price", text);
        Assert.Contains("\"Game; \"\"Deluxe\"\"\";9.50", text);
    }

    [Fact]
    public void StripeSignature_ValidAndTampered()
    {
        const string secret = "whsec_test";
        const string payload = """{"id":"evt_1"}""";
        var now = DateTimeOffset.UtcNow;
        var ts = now.ToUnixTimeSeconds().ToString();
        var sig = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes($"{ts}.{payload}"))).ToLowerInvariant();

        Assert.True(StripePaymentStrategy.VerifySignature(payload, $"t={ts},v1={sig}", secret, now, TimeSpan.FromMinutes(5)));
        Assert.False(StripePaymentStrategy.VerifySignature(payload + " ", $"t={ts},v1={sig}", secret, now, TimeSpan.FromMinutes(5)));
        Assert.False(StripePaymentStrategy.VerifySignature(payload, $"t={ts},v1={sig}", secret, now.AddHours(1), TimeSpan.FromMinutes(5)));
    }
}

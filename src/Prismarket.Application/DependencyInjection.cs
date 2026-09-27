using Microsoft.Extensions.DependencyInjection;
using Prismarket.Application.Features.Admin;
using Prismarket.Application.Features.Auth;
using Prismarket.Application.Features.Automation;
using Prismarket.Application.Features.Automation.Jobs;
using Prismarket.Application.Features.Cart;
using Prismarket.Application.Features.Catalog;
using Prismarket.Application.Features.Currency;
using Prismarket.Application.Features.Notifications;
using Prismarket.Application.Features.Orders;
using Prismarket.Application.Features.Payments;
using Prismarket.Application.Features.Profile;
using Prismarket.Application.Features.Reports;
using Prismarket.Application.Features.Reviews;
using Prismarket.Application.Features.Support;
using Prismarket.Application.Features.Wishlist;

namespace Prismarket.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);

        // Use cases
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IOrderFulfillmentService, OrderFulfillmentService>();
        services.AddScoped<PaymentService>();
        services.AddScoped<IPaymentService>(sp => sp.GetRequiredService<PaymentService>());
        services.AddScoped<IPaymentServiceFacade>(sp => sp.GetRequiredService<PaymentService>());
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<IWishlistService, WishlistService>();
        services.AddScoped<IReviewService, ReviewService>();
        services.AddScoped<ISupportService, SupportService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<ICurrencyService, CurrencyService>();
        services.AddScoped<IExportService, ExportService>();
        services.AddScoped<IReportService, ReportService>();

        // Admin
        services.AddScoped<IAdminCatalogService, AdminCatalogService>();
        services.AddScoped<IAdminUserService, AdminUserService>();
        services.AddScoped<IAdminOrderService, AdminOrderService>();
        services.AddScoped<IPromotionAdminService, PromotionAdminService>();

        // Payment strategies (Strategy pattern + resolver)
        services.AddScoped<IPaymentStrategy, BalancePaymentStrategy>();
        services.AddScoped<IPaymentStrategy, TestCardPaymentStrategy>();
        services.AddScoped<IPaymentStrategy, EripPaymentStrategy>();
        services.AddScoped<IPaymentStrategyResolver, PaymentStrategyResolver>();

        // Rules
        services.AddSingleton<IReviewModerator, ReviewModerator>();
        services.AddSingleton<ISupportBot, KeywordSupportBot>();

        // Automation
        services.AddScoped<IAutomationSettingsProvider, AutomationSettingsProvider>();
        services.AddScoped<IAutomationEventLog, AutomationEventLog>();
        services.AddScoped<IAutomationAdminService, AutomationAdminService>();
        services.AddScoped<AutomationRunner>();

        services.AddScoped<IAutomationJob, OrderExpiryJob>();
        services.AddScoped<IAutomationJob, EmailOutboxJob>();
        services.AddScoped<IAutomationJob, PromotionsSchedulerJob>();
        services.AddScoped<IAutomationJob, StockMonitorJob>();
        services.AddScoped<IAutomationJob, WishlistPriceAlertJob>();
        services.AddScoped<IAutomationJob, AbandonedCartJob>();
        services.AddScoped<IAutomationJob, CurrencyRatesJob>();
        services.AddScoped<IAutomationJob, SalesReportJob>();
        services.AddScoped<IAutomationJob, SupportAutoCloseJob>();
        services.AddScoped<IAutomationJob, FeaturedRotationJob>();
        services.AddScoped<IAutomationJob, WinBackJob>();
        services.AddScoped<IAutomationJob, CleanupJob>();
        services.AddScoped<IAutomationJob, DatabaseBackupJob>();

        return services;
    }
}

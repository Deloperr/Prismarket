using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Prismarket.Application.Common;
using Prismarket.Application.Features.Automation;
using Prismarket.Application.Features.Orders;
using Prismarket.Application.Features.Payments;
using Prismarket.Application.Features.Reports;
using Prismarket.Infrastructure.Analytics;
using Prismarket.Infrastructure.Automation;
using Prismarket.Infrastructure.Payments;
using Prismarket.Infrastructure.Persistence;
using Prismarket.Infrastructure.Reports;
using Prismarket.Infrastructure.Security;
using Prismarket.Infrastructure.Services;
using QuestPDF.Infrastructure;

namespace Prismarket.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration,
        bool enableHangfireServer = true)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
                               ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.Section));
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.Section));
        services.Configure<AppOptions>(configuration.GetSection(AppOptions.Section));
        services.Configure<StripeOptions>(configuration.GetSection(StripeOptions.Section));

        // PostgreSQL: one data source shared by EF Core and Dapper.
        var dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
        services.AddSingleton(dataSource);
        services.AddScoped<AuditInterceptor>();
        services.AddDbContext<AppDbContext>((sp, options) => options
            .UseNpgsql(dataSource, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .AddInterceptors(sp.GetRequiredService<AuditInterceptor>()));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IStockReservationService, StockReservationService>();
        services.AddScoped<IAnalyticsQueries, AnalyticsQueries>();
        services.AddScoped<DataSeeder>();

        // NoSQL cache: Redis if configured, in-memory fallback otherwise.
        var redis = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redis))
            services.AddStackExchangeRedisCache(o => { o.Configuration = redis; o.InstanceName = "prismarket:"; });
        else
            services.AddDistributedMemoryCache();
        services.AddSingleton<ICacheService, DistributedCacheService>();

        // Security
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<ITwoFactorService, TotpTwoFactorService>();

        // Integrations
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        services.AddScoped<IEmailQueue, OutboxEmailQueue>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<IAppUrls, AppUrls>();
        services.AddSingleton<IDatabaseBackupService, PgDumpBackupService>();
        services.AddHttpClient<IExchangeRateProvider, NbrbExchangeRateProvider>(c =>
        {
            c.BaseAddress = new Uri("https://api.nbrb.by/");
            c.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddHttpClient<StripePaymentStrategy>(c => c.BaseAddress = new Uri("https://api.stripe.com/"));
        services.AddScoped<IPaymentStrategy>(sp => sp.GetRequiredService<StripePaymentStrategy>());

        // Reports (Strategy per format)
        QuestPDF.Settings.License = LicenseType.Community;
        services.AddSingleton<IReportRenderer, PdfReportRenderer>();
        services.AddSingleton<IReportRenderer, ExcelReportRenderer>();
        services.AddSingleton<IReportRenderer, WordReportRenderer>();

        // Background jobs (Hangfire + PostgreSQL storage)
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(connectionString),
                new PostgreSqlStorageOptions { SchemaName = "hangfire", PrepareSchemaIfNecessary = true }));
        if (enableHangfireServer)
            services.AddHangfireServer(o =>
            {
                o.WorkerCount = Math.Max(4, Environment.ProcessorCount);
                o.SchedulePollingInterval = TimeSpan.FromSeconds(5);
            });
        services.AddScoped<IAutomationScheduler, HangfireAutomationScheduler>();
        services.AddScoped<HangfireAutomationJob>();

        return services;
    }
}

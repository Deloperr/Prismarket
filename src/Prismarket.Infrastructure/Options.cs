namespace Prismarket.Infrastructure;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public string Issuer { get; set; } = "prismarket";
    public string Audience { get; set; } = "prismarket-client";
    public string Secret { get; set; } = null!;
    public int AccessTokenMinutes { get; set; } = 30;
}

public sealed class SmtpOptions
{
    public const string Section = "Smtp";
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    public bool UseSsl { get; set; }
    public string? User { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "noreply@prismarket.local";
    public string FromName { get; set; } = "Prismarket";
}

public sealed class AppOptions
{
    public const string Section = "App";
    public string FrontendUrl { get; set; } = "http://localhost:5173";
    public string UploadsPath { get; set; } = "wwwroot/uploads";
    public string BackupsPath { get; set; } = "backups";
    public bool SeedDemoData { get; set; } = true;
    /// <summary>Time zone for CRON schedules of automations.</summary>
    public string TimeZone { get; set; } = "Europe/Minsk";
}

public sealed class StripeOptions
{
    public const string Section = "Stripe";
    public string? SecretKey { get; set; }
    public string? WebhookSecret { get; set; }
    public bool Enabled => !string.IsNullOrWhiteSpace(SecretKey);
}

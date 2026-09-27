using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Prismarket.Domain.Entities;
using Prismarket.Domain.Enums;

namespace Prismarket.Application.Common;

/// <summary>
/// Unit of work + data access abstraction over EF Core. The Application layer depends on
/// this interface only (Dependency Inversion), the implementation lives in Infrastructure.
/// </summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Platform> Platforms { get; }
    DbSet<Developer> Developers { get; }
    DbSet<Publisher> Publishers { get; }
    DbSet<Genre> Genres { get; }
    DbSet<Game> Games { get; }
    DbSet<Product> Products { get; }
    DbSet<ProductKey> ProductKeys { get; }
    DbSet<ProductAccount> ProductAccounts { get; }
    DbSet<CartItem> CartItems { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<Payment> Payments { get; }
    DbSet<LibraryItem> LibraryItems { get; }
    DbSet<WishlistItem> WishlistItems { get; }
    DbSet<BalanceTransaction> BalanceTransactions { get; }
    DbSet<PromoCode> PromoCodes { get; }
    DbSet<Promotion> Promotions { get; }
    DbSet<PromotionProduct> PromotionProducts { get; }
    DbSet<Review> Reviews { get; }
    DbSet<SiteReview> SiteReviews { get; }
    DbSet<SupportTicket> SupportTickets { get; }
    DbSet<SupportMessage> SupportMessages { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<CurrencyRate> CurrencyRates { get; }
    DbSet<EmailMessage> EmailMessages { get; }
    DbSet<AutomationJob> AutomationJobs { get; }
    DbSet<AutomationRun> AutomationRuns { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default);
}

public interface ICurrentUser
{
    int? UserId { get; }
    bool IsAuthenticated { get; }
    bool IsAdmin { get; }
    string? IpAddress { get; }
    string? UserAgent { get; }

    int RequireUserId() => UserId ?? throw new UnauthorizedException();
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

public interface ITokenService
{
    (string Token, DateTime ExpiresAt) CreateAccessToken(User user);
    string GenerateSecureToken(int bytes = 32);
    string Hash(string value);
}

public interface ITwoFactorService
{
    string GenerateSecret();
    string BuildOtpAuthUri(string secret, string account);
    string BuildQrCodeDataUri(string otpAuthUri);
    bool Verify(string secret, string code);
}

public interface IFileStorage
{
    Task<string> SaveAsync(Stream content, string fileName, string folder, CancellationToken ct = default);
    Task DeleteAsync(string publicUrl, CancellationToken ct = default);
}

public sealed record EmailEnvelope(
    string To,
    string Subject,
    string HtmlBody,
    string? AttachmentName = null,
    byte[]? AttachmentContent = null,
    string? AttachmentContentType = null);

/// <summary>Low level SMTP sender (Infrastructure).</summary>
public interface IEmailSender
{
    Task SendAsync(EmailEnvelope email, CancellationToken ct = default);
}

/// <summary>Writes an e-mail to the outbox table; delivery is done by an automation job with retries.</summary>
public interface IEmailQueue
{
    void Enqueue(EmailEnvelope email);
}

/// <summary>Pushes realtime events to connected browsers (SignalR in Infrastructure/API).</summary>
public interface IRealtimePublisher
{
    Task NotifyUserAsync(int userId, string eventName, object payload, CancellationToken ct = default);
    Task NotifyAdminsAsync(string eventName, object payload, CancellationToken ct = default);
    Task NotifyTicketAsync(int ticketId, string eventName, object payload, CancellationToken ct = default);
}

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);

    async Task<T> GetOrCreateAsync<T>(string key, TimeSpan ttl, Func<CancellationToken, Task<T>> factory,
        CancellationToken ct = default)
    {
        var cached = await GetAsync<T>(key, ct);
        if (cached is not null) return cached;
        var value = await factory(ct);
        await SetAsync(key, value, ttl, ct);
        return value;
    }
}

public sealed record ExternalRate(string Code, decimal RateToByn);

/// <summary>Source of exchange rates (National Bank of the Republic of Belarus API).</summary>
public interface IExchangeRateProvider
{
    Task<IReadOnlyList<ExternalRate>> GetRatesAsync(IEnumerable<string> codes, CancellationToken ct = default);
}

public interface IDatabaseBackupService
{
    Task<string> CreateBackupAsync(CancellationToken ct = default);
    int DeleteOldBackups(int keepLast);
}

public interface IAppUrls
{
    string FrontendBaseUrl { get; }
    string Build(string relativePath) => $"{FrontendBaseUrl.TrimEnd('/')}/{relativePath.TrimStart('/')}";
}

public static class Roles
{
    public const string Admin = nameof(UserRole.Admin);
    public const string Customer = nameof(UserRole.Customer);
}

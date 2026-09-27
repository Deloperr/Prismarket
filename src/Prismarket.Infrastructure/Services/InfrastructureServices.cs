using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Npgsql;
using Prismarket.Application.Common;
using Prismarket.Domain.Entities;

namespace Prismarket.Infrastructure.Services;

public sealed class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    private readonly SmtpOptions _options = options.Value;

    public async Task SendAsync(EmailEnvelope email, CancellationToken ct = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        message.To.Add(MailboxAddress.Parse(email.To));
        message.Subject = email.Subject;

        var body = new BodyBuilder { HtmlBody = email.HtmlBody };
        if (email.AttachmentContent is not null && email.AttachmentName is not null)
            body.Attachments.Add(email.AttachmentName, email.AttachmentContent,
                ContentType.Parse(email.AttachmentContentType ?? "application/octet-stream"));
        message.Body = body.ToMessageBody();

        using var client = new SmtpClient();
        var security = _options.UseSsl ? SecureSocketOptions.SslOnConnect
            : _options.Port == 587 ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
        await client.ConnectAsync(_options.Host, _options.Port, security, ct);
        if (!string.IsNullOrEmpty(_options.User))
            await client.AuthenticateAsync(_options.User, _options.Password, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}

/// <summary>Transactional outbox: the e-mail row is saved in the same transaction as the business change.</summary>
public sealed class OutboxEmailQueue(IAppDbContext db, TimeProvider clock) : IEmailQueue
{
    public void Enqueue(EmailEnvelope email) => db.EmailMessages.Add(new EmailMessage
    {
        To = email.To,
        Subject = email.Subject,
        HtmlBody = email.HtmlBody,
        AttachmentName = email.AttachmentName,
        AttachmentContent = email.AttachmentContent,
        AttachmentContentType = email.AttachmentContentType,
        CreatedAt = clock.GetUtcNow().UtcDateTime
    });
}

public sealed class LocalFileStorage(IOptions<AppOptions> options) : IFileStorage
{
    public async Task<string> SaveAsync(Stream content, string fileName, string folder, CancellationToken ct = default)
    {
        var baseName = Slug.From(Path.GetFileNameWithoutExtension(fileName));
        if (baseName.Length > 40) baseName = baseName[..40];
        var safeName = $"{baseName}-{Guid.NewGuid():N}{Path.GetExtension(fileName).ToLowerInvariant()}";
        var directory = Path.Combine(options.Value.UploadsPath, folder);
        Directory.CreateDirectory(directory);
        await using var file = File.Create(Path.Combine(directory, safeName));
        await content.CopyToAsync(file, ct);
        return $"/uploads/{folder}/{safeName}";
    }

    public Task DeleteAsync(string publicUrl, CancellationToken ct = default)
    {
        if (!publicUrl.StartsWith("/uploads/", StringComparison.Ordinal)) return Task.CompletedTask;
        var relative = publicUrl["/uploads/".Length..].Replace('/', Path.DirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(options.Value.UploadsPath, relative));
        var root = Path.GetFullPath(options.Value.UploadsPath);
        if (path.StartsWith(root, StringComparison.Ordinal) && File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }
}

/// <summary>JSON cache on top of IDistributedCache: Redis when configured, in-memory otherwise.</summary>
public sealed class DistributedCacheService(IDistributedCache cache, ILogger<DistributedCacheService> logger) : ICacheService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        try
        {
            var bytes = await cache.GetAsync(key, ct);
            return bytes is null ? default : JsonSerializer.Deserialize<T>(bytes, Json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Cache must never break the request (e.g. Redis is down).
            logger.LogWarning(ex, "Cache read failed for {Key}", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
    {
        try
        {
            await cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value, Json),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Cache write failed for {Key}", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        try { await cache.RemoveAsync(key, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Cache remove failed for {Key}", key);
        }
    }
}

/// <summary>Official rates of the National Bank of the Republic of Belarus (https://api.nbrb.by).</summary>
public sealed class NbrbExchangeRateProvider(HttpClient http) : IExchangeRateProvider
{
    private sealed record NbrbRate(
        [property: JsonPropertyName("Cur_Abbreviation")] string Abbreviation,
        [property: JsonPropertyName("Cur_Scale")] int Scale,
        [property: JsonPropertyName("Cur_OfficialRate")] decimal OfficialRate);

    public async Task<IReadOnlyList<ExternalRate>> GetRatesAsync(IEnumerable<string> codes, CancellationToken ct = default)
    {
        // Requests are independent — fire them concurrently.
        var tasks = codes.Select(async code =>
        {
            var rate = await http.GetFromJsonAsync<NbrbRate>(
                $"exrates/rates/{Uri.EscapeDataString(code.ToUpperInvariant())}?parammode=2", ct);
            return rate is null ? null : new ExternalRate(rate.Abbreviation, Math.Round(rate.OfficialRate / rate.Scale, 6));
        });
        var results = await Task.WhenAll(tasks);
        return results.OfType<ExternalRate>().ToList();
    }
}

/// <summary>Runs pg_dump as an external process asynchronously.</summary>
public sealed class PgDumpBackupService(IConfiguration configuration, IOptions<AppOptions> options,
    TimeProvider clock, ILogger<PgDumpBackupService> logger) : IDatabaseBackupService
{
    public async Task<string> CreateBackupAsync(CancellationToken ct = default)
    {
        var builder = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("Postgres"));
        Directory.CreateDirectory(options.Value.BackupsPath);
        var file = Path.Combine(options.Value.BackupsPath,
            $"prismarket-{clock.GetUtcNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.dump");

        var psi = new ProcessStartInfo("pg_dump")
        {
            ArgumentList = { "-h", builder.Host!, "-p", builder.Port.ToString(CultureInfo.InvariantCulture),
                "-U", builder.Username!, "-d", builder.Database!, "-F", "c", "-f", file },
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.Environment["PGPASSWORD"] = builder.Password;

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("pg_dump is not available.");
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"pg_dump failed ({process.ExitCode}): {stderr}");

        logger.LogInformation("Database backup created: {File}", file);
        return file;
    }

    public int DeleteOldBackups(int keepLast)
    {
        if (!Directory.Exists(options.Value.BackupsPath)) return 0;
        var old = new DirectoryInfo(options.Value.BackupsPath).GetFiles("prismarket-*.dump")
            .OrderByDescending(f => f.CreationTimeUtc).Skip(keepLast).ToList();
        old.ForEach(f => f.Delete());
        return old.Count;
    }
}

public sealed class AppUrls(IOptions<AppOptions> options) : IAppUrls
{
    public string FrontendBaseUrl => options.Value.FrontendUrl;
}

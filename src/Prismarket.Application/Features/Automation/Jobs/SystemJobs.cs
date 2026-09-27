using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prismarket.Application.Common;
using Prismarket.Application.Features.Currency;
using Prismarket.Application.Features.Reports;
using Prismarket.Domain.Entities;
using Prismarket.Domain.Enums;

namespace Prismarket.Application.Features.Automation.Jobs;

/// <summary>
/// Drains the e-mail outbox. Messages are sent concurrently with <see cref="Parallel.ForEachAsync{TSource}(IEnumerable{TSource}, ParallelOptions, Func{TSource, CancellationToken, ValueTask})"/>
/// (bounded degree of parallelism); failures are retried with exponential backoff.
/// </summary>
public sealed class EmailOutboxJob(IAppDbContext db, IEmailSender sender, TimeProvider clock,
    ILogger<EmailOutboxJob> logger) : IAutomationJob
{
    public string Key => AutomationKeys.EmailOutbox;

    public async Task<AutomationJobResult> ExecuteAsync(JobSettings settings, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var maxAttempts = settings.GetInt("maxAttempts");

        var batch = await db.EmailMessages
            .Where(m => m.Status == EmailStatus.Pending && (m.NextAttemptAt == null || m.NextAttemptAt <= now))
            .OrderBy(m => m.CreatedAt)
            .Take(Math.Clamp(settings.GetInt("batchSize"), 1, 500))
            .ToListAsync(ct);
        if (batch.Count == 0) return AutomationJobResult.Nothing("Очередь писем пуста");

        // DbContext is not thread-safe: network I/O runs in parallel, DB updates happen afterwards.
        var errors = new ConcurrentDictionary<int, string>();
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(settings.GetInt("parallelism"), 1, 16),
            CancellationToken = ct
        };
        await Parallel.ForEachAsync(batch, options, async (message, token) =>
        {
            try
            {
                await sender.SendAsync(new EmailEnvelope(message.To, message.Subject, message.HtmlBody,
                    message.AttachmentName, message.AttachmentContent, message.AttachmentContentType), token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors[message.Id] = ex.Message;
                logger.LogWarning(ex, "Failed to send e-mail {Id} to {To}", message.Id, message.To);
            }
        });

        foreach (var message in batch)
        {
            message.Attempts++;
            if (errors.TryGetValue(message.Id, out var error))
            {
                message.LastError = error.Length > 500 ? error[..500] : error;
                if (message.Attempts >= maxAttempts) message.Status = EmailStatus.Failed;
                else message.NextAttemptAt = now.Add(Backoff(message.Attempts));
            }
            else
            {
                message.Status = EmailStatus.Sent;
                message.SentAt = clock.GetUtcNow().UtcDateTime;
                message.AttachmentContent = null; // do not keep large blobs after delivery
            }
        }
        await db.SaveChangesAsync(ct);

        var sent = batch.Count - errors.Count;
        return new AutomationJobResult(sent, $"Отправлено: {sent}, ошибок: {errors.Count}");
    }

    /// <summary>1, 2, 4, 8 ... minutes, capped at one hour.</summary>
    public static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, Math.Max(0, attempt - 1))));
}

/// <summary>Downloads official exchange rates from the National Bank of Belarus.</summary>
public sealed class CurrencyRatesJob(IAppDbContext db, IExchangeRateProvider provider, ICacheService cache,
    TimeProvider clock) : IAutomationJob
{
    public string Key => AutomationKeys.CurrencyRates;

    public async Task<AutomationJobResult> ExecuteAsync(JobSettings settings, CancellationToken ct)
    {
        var rates = await provider.GetRatesAsync(settings.GetList("currencies"), ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var existing = await db.CurrencyRates.ToDictionaryAsync(r => r.Code, ct);

        foreach (var rate in rates)
        {
            if (!existing.TryGetValue(rate.Code, out var entity))
            {
                entity = new CurrencyRate { Code = rate.Code };
                db.CurrencyRates.Add(entity);
            }
            entity.RateToByn = rate.RateToByn;
            entity.UpdatedAt = now;
            entity.Source = "nbrb.by";
        }
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(CurrencyService.CacheKey, ct);
        return new AutomationJobResult(rates.Count,
            string.Join(", ", rates.Select(r => $"{r.Code} = {r.RateToByn:0.####} BYN")));
    }
}

/// <summary>Builds the sales report for the last N days and e-mails it to every admin.</summary>
public sealed class SalesReportJob(IAppDbContext db, IReportService reports, IEmailQueue emails, TimeProvider clock)
    : IAutomationJob
{
    public string Key => AutomationKeys.SalesReport;

    public async Task<AutomationJobResult> ExecuteAsync(JobSettings settings, CancellationToken ct)
    {
        var to = clock.GetUtcNow().UtcDateTime.Date;
        var from = to.AddDays(-Math.Max(1, settings.GetInt("periodDays")));
        var formats = settings.GetString("format").ToLowerInvariant() switch
        {
            "pdf" => new[] { ReportFormat.Pdf },
            "excel" => new[] { ReportFormat.Excel },
            _ => new[] { ReportFormat.Pdf, ReportFormat.Excel }
        };

        // Independent renderings run concurrently (TPL).
        var files = await Task.WhenAll(formats.Select(f => reports.BuildSalesReportAsync(from, to, f, ct)));

        var admins = await db.Users.Where(u => u.Role == UserRole.Admin && u.IsActive).Select(u => u.Email)
            .ToListAsync(ct);
        foreach (var email in admins)
        foreach (var file in files)
        {
            emails.Enqueue(new EmailEnvelope(email, $"Отчёт о продажах {from:dd.MM} – {to.AddDays(-1):dd.MM.yyyy}",
                EmailTemplates.Layout("Автоматический отчёт о продажах",
                    $"<p>Во вложении отчёт за период {from:dd.MM.yyyy} – {to.AddDays(-1):dd.MM.yyyy} ({file.FileName}).</p>"),
                file.FileName, file.Content, file.ContentType));
        }
        await db.SaveChangesAsync(ct);
        return new AutomationJobResult(admins.Count * files.Length,
            $"Отчёт ({string.Join(", ", formats)}) отправлен администраторам: {admins.Count}");
    }
}

/// <summary>Closes support tickets where the customer has not answered for a long time.</summary>
public sealed class SupportAutoCloseJob(IAppDbContext db, IRealtimePublisher realtime, TimeProvider clock) : IAutomationJob
{
    public string Key => AutomationKeys.SupportAutoClose;

    public async Task<AutomationJobResult> ExecuteAsync(JobSettings settings, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var hours = settings.GetInt("inactiveHours");
        var threshold = now.AddHours(-hours);

        var tickets = await db.SupportTickets
            .Where(t => t.Status == TicketStatus.WaitingForCustomer && t.LastMessageAt <= threshold)
            .ToListAsync(ct);
        foreach (var ticket in tickets)
        {
            ticket.Status = TicketStatus.Closed;
            ticket.ClosedAt = now;
            db.SupportMessages.Add(new SupportMessage
            {
                TicketId = ticket.Id, IsBot = true, IsFromStaff = true, CreatedAt = now,
                Text = $"Обращение закрыто автоматически: нет ответа более {hours} ч. " +
                       "Если вопрос остался — просто напишите сюда, и мы откроем его снова."
            });
        }
        if (tickets.Count == 0) return AutomationJobResult.Nothing();
        await db.SaveChangesAsync(ct);
        await Task.WhenAll(tickets.Select(t =>
            realtime.NotifyTicketAsync(t.Id, "status", new { Status = TicketStatus.Closed }, ct)));
        return new AutomationJobResult(tickets.Count, $"Закрыто обращений: {tickets.Count}");
    }
}

/// <summary>Housekeeping: removes expired and old technical data.</summary>
public sealed class CleanupJob(IAppDbContext db, TimeProvider clock) : IAutomationJob
{
    public string Key => AutomationKeys.Cleanup;

    public async Task<AutomationJobResult> ExecuteAsync(JobSettings settings, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var tokens = await db.RefreshTokens.Where(t => t.ExpiresAt < now || (t.RevokedAt != null && t.RevokedAt < now.AddDays(-7)))
            .ExecuteDeleteAsync(ct);
        var notificationsCutoff = now.AddDays(-settings.GetInt("notificationRetentionDays"));
        var notifications = await db.Notifications.Where(n => n.IsRead && n.CreatedAt < notificationsCutoff)
            .ExecuteDeleteAsync(ct);
        var auditCutoff = now.AddDays(-settings.GetInt("auditRetentionDays"));
        var audit = await db.AuditLogs.Where(a => a.CreatedAt < auditCutoff).ExecuteDeleteAsync(ct);
        var runsCutoff = now.AddDays(-settings.GetInt("runRetentionDays"));
        var runs = await db.AutomationRuns.Where(r => r.StartedAt < runsCutoff).ExecuteDeleteAsync(ct);
        var emails = await db.EmailMessages.Where(m => m.Status == EmailStatus.Sent && m.SentAt < now.AddDays(-30))
            .ExecuteDeleteAsync(ct);

        var total = tokens + notifications + audit + runs + emails;
        return new AutomationJobResult(total,
            $"Удалено: токенов {tokens}, уведомлений {notifications}, записей аудита {audit}, запусков {runs}, писем {emails}");
    }
}

public sealed class DatabaseBackupJob(IDatabaseBackupService backups) : IAutomationJob
{
    public string Key => AutomationKeys.DatabaseBackup;

    public async Task<AutomationJobResult> ExecuteAsync(JobSettings settings, CancellationToken ct)
    {
        var file = await backups.CreateBackupAsync(ct);
        var removed = backups.DeleteOldBackups(Math.Max(1, settings.GetInt("keepLast")));
        return new AutomationJobResult(1, $"Создан {Path.GetFileName(file)}, удалено старых копий: {removed}");
    }
}

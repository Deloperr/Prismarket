using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Prismarket.Application.Common;
using Prismarket.Domain.Entities;
using Prismarket.Domain.Enums;

namespace Prismarket.Application.Features.Automation;

public sealed record AutomationJobResult(int ItemsProcessed, string Message)
{
    public static AutomationJobResult Nothing(string message = "Нечего обрабатывать") => new(0, message);
}

/// <summary>A scheduled automation. Each implementation is small and focused (SRP).</summary>
public interface IAutomationJob
{
    string Key { get; }
    Task<AutomationJobResult> ExecuteAsync(JobSettings settings, CancellationToken ct);
}

/// <summary>Registers / removes recurring jobs in the background scheduler (Hangfire).</summary>
public interface IAutomationScheduler
{
    void Schedule(string key, string cron);
    void Unschedule(string key);
    string TriggerNow(string key);
}

public interface IAutomationSettingsProvider
{
    Task<(bool Enabled, JobSettings Settings)> GetAsync(string key, CancellationToken ct = default);
}

/// <summary>Records executions of event-driven rules so they are visible in the admin panel.</summary>
public interface IAutomationEventLog
{
    void Record(string key, int itemsProcessed, string message);
}

public sealed class AutomationSettingsProvider(IAppDbContext db) : IAutomationSettingsProvider
{
    public async Task<(bool Enabled, JobSettings Settings)> GetAsync(string key, CancellationToken ct = default)
    {
        var definition = AutomationCatalog.Get(key);
        var job = await db.AutomationJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Key == key, ct);
        return (job?.IsEnabled ?? true, new JobSettings(job?.SettingsJson, definition.Settings));
    }
}

public sealed class AutomationEventLog(IAppDbContext db, TimeProvider clock) : IAutomationEventLog
{
    public void Record(string key, int itemsProcessed, string message)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        db.AutomationRuns.Add(new AutomationRun
        {
            JobKey = key, Trigger = AutomationTrigger.Event, Status = AutomationRunStatus.Succeeded,
            StartedAt = now, FinishedAt = now, ItemsProcessed = itemsProcessed, Message = message
        });
    }
}

/// <summary>
/// Entry point invoked by Hangfire. Wraps every job with: enabled check, run history, timing,
/// error capture. The job itself runs in its own DI scope so a failure never leaks tracked entities
/// into the history record.
/// </summary>
public sealed class AutomationRunner(
    IServiceScopeFactory scopeFactory,
    IAppDbContext db,
    TimeProvider clock,
    ILogger<AutomationRunner> logger)
{
    public async Task RunAsync(string key, AutomationTrigger trigger, CancellationToken ct)
    {
        var job = await db.AutomationJobs.FirstOrDefaultAsync(j => j.Key == key, ct);
        if (job is null)
        {
            logger.LogWarning("Automation {Key} is not registered", key);
            return;
        }
        if (!job.IsEnabled && trigger == AutomationTrigger.Schedule)
            return;

        var run = new AutomationRun
        {
            JobKey = key, Trigger = trigger, Status = AutomationRunStatus.Running,
            StartedAt = clock.GetUtcNow().UtcDateTime
        };
        db.AutomationRuns.Add(run);
        await db.SaveChangesAsync(ct);

        var sw = Stopwatch.StartNew();
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var implementation = scope.ServiceProvider.GetServices<IAutomationJob>().Single(j => j.Key == key);
            var settings = new JobSettings(job.SettingsJson, AutomationCatalog.Get(key).Settings);

            var result = await implementation.ExecuteAsync(settings, ct);

            run.Status = AutomationRunStatus.Succeeded;
            run.ItemsProcessed = result.ItemsProcessed;
            run.Message = result.Message;
            logger.LogInformation("Automation {Key} finished in {Elapsed} ms: {Message}", key,
                sw.ElapsedMilliseconds, result.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            run.Status = AutomationRunStatus.Failed;
            run.Message = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
            logger.LogError(ex, "Automation {Key} failed", key);
        }
        finally
        {
            run.FinishedAt = clock.GetUtcNow().UtcDateTime;
            job.LastRunAt = run.FinishedAt;
            job.LastStatus = run.Status;
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }
}

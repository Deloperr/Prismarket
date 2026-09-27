using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Prismarket.Application.Features.Automation;
using Prismarket.Domain.Enums;

namespace Prismarket.Infrastructure.Automation;

/// <summary>Adapter between the Application automation layer and Hangfire (Dependency Inversion).</summary>
public sealed class HangfireAutomationScheduler(
    IRecurringJobManager recurring,
    IBackgroundJobClient jobs,
    IOptions<AppOptions> options) : IAutomationScheduler
{
    private readonly TimeZoneInfo _timeZone = ResolveTimeZone(options.Value.TimeZone);

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
    }

    public void Schedule(string key, string cron) =>
        recurring.AddOrUpdate<HangfireAutomationJob>(key,
            job => job.ExecuteAsync(key, AutomationTrigger.Schedule, CancellationToken.None),
            cron, new RecurringJobOptions { TimeZone = _timeZone });

    public void Unschedule(string key) => recurring.RemoveIfExists(key);

    public string TriggerNow(string key) =>
        jobs.Enqueue<HangfireAutomationJob>(job => job.ExecuteAsync(key, AutomationTrigger.Manual, CancellationToken.None));
}

/// <summary>
/// Hangfire entry point. Guarantees that the same automation never runs twice at the same time
/// (distributed lock in the Hangfire storage — works across several API instances).
/// </summary>
public sealed class HangfireAutomationJob(AutomationRunner runner, JobStorage storage, ILogger<HangfireAutomationJob> logger)
{
    [AutomaticRetry(Attempts = 0)]
    [JobDisplayName("Automation: {0} ({1})")]
    public async Task ExecuteAsync(string key, AutomationTrigger trigger, CancellationToken ct)
    {
        IDisposable? distributedLock;
        using var connection = storage.GetConnection();
        try
        {
            distributedLock = connection.AcquireDistributedLock($"prismarket:automation:{key}", TimeSpan.FromSeconds(1));
        }
        catch (DistributedLockTimeoutException)
        {
            logger.LogInformation("Automation {Key} is already running — skipped", key);
            return;
        }

        using (distributedLock)
        {
            await runner.RunAsync(key, trigger, ct);
        }
    }
}

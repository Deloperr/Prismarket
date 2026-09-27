using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismarket.Application.Common;
using Prismarket.Domain.Entities;
using Prismarket.Domain.Enums;

namespace Prismarket.Application.Features.Automation;

public sealed record AutomationJobDto(
    string Key, string Name, string Description, AutomationKind Kind, string? Cron, bool IsEnabled,
    Dictionary<string, object?> Settings, IReadOnlyList<SettingDefinition> SettingDefinitions,
    DateTime? LastRunAt, AutomationRunStatus? LastStatus, int Runs24h, int Failed24h, int Items24h);

public sealed record AutomationRunDto(
    int Id, string JobKey, string JobName, AutomationTrigger Trigger, AutomationRunStatus Status,
    DateTime StartedAt, DateTime? FinishedAt, double? DurationMs, int ItemsProcessed, string? Message);

public sealed record UpdateAutomationRequest(bool? IsEnabled, string? Cron, Dictionary<string, JsonElement>? Settings);

public sealed record AutomationOverviewDto(int TotalJobs, int Enabled, int Runs24h, int Failed24h, int Items24h,
    IReadOnlyList<AutomationRunDto> LatestRuns);

public interface IAutomationAdminService
{
    Task SyncCatalogAsync(CancellationToken ct);
    Task<IReadOnlyList<AutomationJobDto>> GetJobsAsync(CancellationToken ct);
    Task<AutomationOverviewDto> GetOverviewAsync(CancellationToken ct);
    Task<AutomationJobDto> UpdateAsync(string key, UpdateAutomationRequest request, CancellationToken ct);
    Task<string> RunNowAsync(string key, CancellationToken ct);
    Task<PagedResult<AutomationRunDto>> GetRunsAsync(string? key, AutomationRunStatus? status, PageRequest page,
        CancellationToken ct);
}

public sealed class AutomationAdminService(IAppDbContext db, IAutomationScheduler scheduler, TimeProvider clock)
    : IAutomationAdminService
{
    /// <summary>Creates missing rows for new automations and (re)registers enabled CRON jobs.</summary>
    public async Task SyncCatalogAsync(CancellationToken ct)
    {
        var existing = await db.AutomationJobs.ToDictionaryAsync(j => j.Key, ct);
        foreach (var definition in AutomationCatalog.All)
        {
            if (!existing.TryGetValue(definition.Key, out var job))
            {
                job = new AutomationJob
                {
                    Key = definition.Key,
                    Cron = definition.DefaultCron,
                    IsEnabled = definition.Key != AutomationKeys.DatabaseBackup, // needs pg_dump, opt-in
                    SettingsJson = "{}",
                    UpdatedAt = clock.GetUtcNow().UtcDateTime
                };
                db.AutomationJobs.Add(job);
            }
            job.Name = definition.Name;
            job.Description = definition.Description;
            job.Kind = definition.Kind;

            if (definition.Kind == AutomationKind.Scheduled)
            {
                if (job.IsEnabled) scheduler.Schedule(job.Key, job.Cron ?? definition.DefaultCron!);
                else scheduler.Unschedule(job.Key);
            }
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AutomationJobDto>> GetJobsAsync(CancellationToken ct)
    {
        var since = clock.GetUtcNow().UtcDateTime.AddHours(-24);
        var stats = await db.AutomationRuns.AsNoTracking()
            .Where(r => r.StartedAt >= since)
            .GroupBy(r => r.JobKey)
            .Select(g => new
            {
                Key = g.Key,
                Runs = g.Count(),
                Failed = g.Count(r => r.Status == AutomationRunStatus.Failed),
                Items = g.Sum(r => r.ItemsProcessed)
            })
            .ToDictionaryAsync(x => x.Key, ct);

        var jobs = await db.AutomationJobs.AsNoTracking().ToListAsync(ct);
        var order = AutomationCatalog.All.Select((d, i) => (d.Key, i)).ToDictionary(x => x.Key, x => x.i);

        return jobs
            .Where(j => order.ContainsKey(j.Key))
            .OrderBy(j => j.Kind).ThenBy(j => order[j.Key])
            .Select(j =>
            {
                stats.TryGetValue(j.Key, out var s);
                return ToDto(j, s?.Runs ?? 0, s?.Failed ?? 0, s?.Items ?? 0);
            })
            .ToList();
    }

    public async Task<AutomationOverviewDto> GetOverviewAsync(CancellationToken ct)
    {
        var jobs = await GetJobsAsync(ct);
        var latest = await Runs(null, null).Take(10).ToListAsync(ct);
        return new AutomationOverviewDto(jobs.Count, jobs.Count(j => j.IsEnabled), jobs.Sum(j => j.Runs24h),
            jobs.Sum(j => j.Failed24h), jobs.Sum(j => j.Items24h), latest.Select(WithName).ToList());
    }

    public async Task<AutomationJobDto> UpdateAsync(string key, UpdateAutomationRequest request, CancellationToken ct)
    {
        var definition = AutomationCatalog.Get(key);
        var job = await db.AutomationJobs.FirstOrDefaultAsync(j => j.Key == key, ct)
                  ?? throw NotFoundException.For("Автоматизация", key);

        if (request.Cron is not null && definition.Kind == AutomationKind.Scheduled)
        {
            if (!CronValidator.IsValid(request.Cron))
                throw new BusinessRuleException("Некорректное CRON-выражение (ожидается 5 полей).");
            job.Cron = request.Cron.Trim();
        }
        if (request.IsEnabled is not null) job.IsEnabled = request.IsEnabled.Value;
        if (request.Settings is not null)
        {
            var allowed = definition.Settings.Select(s => s.Key).ToHashSet();
            var unknown = request.Settings.Keys.Where(k => !allowed.Contains(k)).ToList();
            if (unknown.Count > 0)
                throw new BusinessRuleException($"Неизвестные настройки: {string.Join(", ", unknown)}");
            job.SettingsJson = JsonSerializer.Serialize(request.Settings);
        }
        job.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);

        if (definition.Kind == AutomationKind.Scheduled)
        {
            if (job.IsEnabled) scheduler.Schedule(job.Key, job.Cron!);
            else scheduler.Unschedule(job.Key);
        }

        return ToDto(job, 0, 0, 0);
    }

    public async Task<string> RunNowAsync(string key, CancellationToken ct)
    {
        var definition = AutomationCatalog.Get(key);
        if (definition.Kind != AutomationKind.Scheduled)
            throw new BusinessRuleException("Событийные правила запускаются автоматически и не могут быть запущены вручную.");
        if (!await db.AutomationJobs.AnyAsync(j => j.Key == key, ct))
            throw NotFoundException.For("Автоматизация", key);
        return scheduler.TriggerNow(key);
    }

    public async Task<PagedResult<AutomationRunDto>> GetRunsAsync(string? key, AutomationRunStatus? status,
        PageRequest page, CancellationToken ct)
    {
        var result = await Runs(key, status).ToPagedAsync(page, ct);
        return new PagedResult<AutomationRunDto>(result.Items.Select(WithName).ToList(), result.Page,
            result.PageSize, result.TotalCount);
    }

    private IQueryable<AutomationRunDto> Runs(string? key, AutomationRunStatus? status)
    {
        var query = db.AutomationRuns.AsNoTracking();
        if (!string.IsNullOrEmpty(key)) query = query.Where(r => r.JobKey == key);
        if (status is not null) query = query.Where(r => r.Status == status);
        return query.OrderByDescending(r => r.StartedAt)
            .Select(r => new AutomationRunDto(r.Id, r.JobKey, "", r.Trigger, r.Status, r.StartedAt, r.FinishedAt,
                null, r.ItemsProcessed, r.Message));
    }

    private static AutomationRunDto WithName(AutomationRunDto r) => r with
    {
        JobName = AutomationCatalog.All.FirstOrDefault(d => d.Key == r.JobKey)?.Name ?? r.JobKey,
        DurationMs = r.FinishedAt is null ? null : (r.FinishedAt.Value - r.StartedAt).TotalMilliseconds
    };

    private static AutomationJobDto ToDto(AutomationJob j, int runs, int failed, int items)
    {
        var definition = AutomationCatalog.Get(j.Key);
        var settings = new JobSettings(j.SettingsJson, definition.Settings);
        return new AutomationJobDto(j.Key, j.Name, j.Description, j.Kind, j.Cron, j.IsEnabled,
            settings.ToDictionary(), definition.Settings, j.LastRunAt, j.LastStatus, runs, failed, items);
    }
}

public static class CronValidator
{
    /// <summary>Lightweight validation of a standard 5-field CRON expression.</summary>
    public static bool IsValid(string cron)
    {
        var parts = cron.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5) return false;
        return parts.All(p => p.All(ch => char.IsDigit(ch) || ch is '*' or '/' or ',' or '-'));
    }
}

using Prismarket.Domain.Common;
using Prismarket.Domain.Enums;

namespace Prismarket.Domain.Entities;

public class AuditLog : Entity
{
    public int? UserId { get; set; }
    public string Action { get; set; } = null!;
    public string EntityName { get; set; } = null!;
    public string? EntityId { get; set; }
    /// <summary>JSON document with old/new values.</summary>
    public string? Changes { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CurrencyRate
{
    /// <summary>ISO code, e.g. USD.</summary>
    public string Code { get; set; } = null!;
    /// <summary>How many BYN one unit of the currency costs.</summary>
    public decimal RateToByn { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string Source { get; set; } = "manual";
}

/// <summary>Transactional outbox for e-mails, drained by the email-outbox automation with retries.</summary>
public class EmailMessage : Entity
{
    public string To { get; set; } = null!;
    public string Subject { get; set; } = null!;
    public string HtmlBody { get; set; } = null!;
    public EmailStatus Status { get; set; } = EmailStatus.Pending;
    public int Attempts { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? SentAt { get; set; }
    public string? LastError { get; set; }

    /// <summary>Optional attachment (reports).</summary>
    public string? AttachmentName { get; set; }
    public byte[]? AttachmentContent { get; set; }
    public string? AttachmentContentType { get; set; }
}

/// <summary>Configuration of an automation job editable from the admin panel.</summary>
public class AutomationJob
{
    public string Key { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
    public AutomationKind Kind { get; set; }
    /// <summary>CRON expression for scheduled jobs; null for event-driven rules.</summary>
    public string? Cron { get; set; }
    public bool IsEnabled { get; set; } = true;
    /// <summary>JSON object with job specific settings (thresholds, percents, ...).</summary>
    public string SettingsJson { get; set; } = "{}";
    public DateTime? LastRunAt { get; set; }
    public AutomationRunStatus? LastStatus { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class AutomationRun : Entity
{
    public string JobKey { get; set; } = null!;
    public AutomationTrigger Trigger { get; set; }
    public AutomationRunStatus Status { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public int ItemsProcessed { get; set; }
    public string? Message { get; set; }

    public double? DurationMs => FinishedAt is null ? null : (FinishedAt.Value - StartedAt).TotalMilliseconds;
}

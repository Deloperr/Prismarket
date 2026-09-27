using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Prismarket.Application.Common;
using Prismarket.Domain.Common;
using Prismarket.Domain.Entities;

namespace Prismarket.Infrastructure.Persistence;

/// <summary>
/// Cross-cutting concern implemented once (DRY): fills CreatedAt/UpdatedAt and writes an audit trail
/// for entities marked with <see cref="ITrackChanges"/>. Replaces the SQL Server triggers of the old project.
/// </summary>
public sealed class AuditInterceptor(ICurrentUser currentUser, TimeProvider clock) : SaveChangesInterceptor
{
    private static readonly HashSet<string> SensitiveProperties =
        [nameof(User.PasswordHash), nameof(User.TwoFactorSecret), nameof(User.EmailConfirmationToken),
         nameof(User.PasswordResetToken), "Version"];

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null) Process(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null) Process(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void Process(DbContext context)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            if (entry.State == EntityState.Added && entry.Entity.CreatedAt == default) entry.Entity.CreatedAt = now;
            if (entry.State == EntityState.Modified) entry.Entity.UpdatedAt = now;
        }

        var audited = context.ChangeTracker.Entries()
            .Where(e => e.Entity is ITrackChanges && e.State is EntityState.Modified or EntityState.Deleted)
            .ToList();

        foreach (var entry in audited)
        {
            var changes = Describe(entry);
            if (changes.Count == 0) continue;

            context.Set<AuditLog>().Add(new AuditLog
            {
                UserId = currentUser.UserId,
                Action = entry.State.ToString(),
                EntityName = entry.Metadata.ClrType.Name,
                EntityId = entry.Properties.FirstOrDefault(p => p.Metadata.IsPrimaryKey())?.CurrentValue?.ToString(),
                Changes = JsonSerializer.Serialize(changes),
                IpAddress = currentUser.IpAddress,
                UserAgent = currentUser.UserAgent is { Length: > 200 } ua ? ua[..200] : currentUser.UserAgent,
                CreatedAt = now
            });
        }
    }

    private static Dictionary<string, object?> Describe(EntityEntry entry)
    {
        var result = new Dictionary<string, object?>();
        foreach (var property in entry.Properties)
        {
            var name = property.Metadata.Name;
            if (name is nameof(IAuditable.UpdatedAt) or nameof(IAuditable.CreatedAt)) continue;

            if (entry.State == EntityState.Deleted)
            {
                if (!SensitiveProperties.Contains(name)) result[name] = property.OriginalValue;
                continue;
            }

            if (!property.IsModified || Equals(property.OriginalValue, property.CurrentValue)) continue;
            result[name] = SensitiveProperties.Contains(name)
                ? new { old = "***", @new = "***" }
                : new { old = property.OriginalValue, @new = property.CurrentValue };
        }
        return result;
    }
}

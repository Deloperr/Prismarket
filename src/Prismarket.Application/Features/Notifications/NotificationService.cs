using Microsoft.EntityFrameworkCore;
using Prismarket.Application.Common;
using Prismarket.Domain.Entities;
using Prismarket.Domain.Enums;

namespace Prismarket.Application.Features.Notifications;

public sealed record NotificationDto(int Id, NotificationType Type, string Title, string Message, string? Link,
    bool IsRead, DateTime CreatedAt);

public interface INotificationService
{
    /// <summary>Adds a notification to the change tracker (saved together with the caller's unit of work)
    /// and pushes it to the browser after <see cref="FlushAsync"/>.</summary>
    void Add(int userId, NotificationType type, string title, string message, string? link = null);
    Task AddForAdminsAsync(NotificationType type, string title, string message, string? link = null, CancellationToken ct = default);
    Task FlushAsync(CancellationToken ct = default);

    Task<IReadOnlyList<NotificationDto>> GetMineAsync(int take, CancellationToken ct);
    Task<int> CountUnreadAsync(CancellationToken ct);
    Task MarkReadAsync(int? id, CancellationToken ct);
}

public sealed class NotificationService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IRealtimePublisher realtime,
    TimeProvider clock) : INotificationService
{
    private readonly List<Notification> _pending = [];

    public void Add(int userId, NotificationType type, string title, string message, string? link = null)
    {
        var notification = new Notification
        {
            UserId = userId, Type = type, Title = title, Message = message, Link = link,
            CreatedAt = clock.GetUtcNow().UtcDateTime
        };
        db.Notifications.Add(notification);
        _pending.Add(notification);
    }

    public async Task AddForAdminsAsync(NotificationType type, string title, string message, string? link = null,
        CancellationToken ct = default)
    {
        var adminIds = await db.Users.Where(u => u.Role == UserRole.Admin && u.IsActive)
            .Select(u => u.Id).ToListAsync(ct);
        foreach (var id in adminIds) Add(id, type, title, message, link);
    }

    /// <summary>Pushes saved notifications to connected clients in parallel (TPL: Task.WhenAll).</summary>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        if (_pending.Count == 0) return;
        var batch = _pending.ToArray();
        _pending.Clear();

        await Task.WhenAll(batch.Select(n => realtime.NotifyUserAsync(n.UserId, "notification", ToDto(n), ct)));
    }

    public async Task<IReadOnlyList<NotificationDto>> GetMineAsync(int take, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        return await db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(Math.Clamp(take, 1, 100))
            .Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Message, n.Link, n.IsRead, n.CreatedAt))
            .ToListAsync(ct);
    }

    public Task<int> CountUnreadAsync(CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        return db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct);
    }

    public async Task MarkReadAsync(int? id, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var query = db.Notifications.Where(n => n.UserId == userId && !n.IsRead);
        if (id is not null) query = query.Where(n => n.Id == id);
        await query.ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);
    }

    private static NotificationDto ToDto(Notification n) =>
        new(n.Id, n.Type, n.Title, n.Message, n.Link, n.IsRead, n.CreatedAt);
}

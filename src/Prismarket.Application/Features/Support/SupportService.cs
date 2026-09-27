using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Prismarket.Application.Common;
using Prismarket.Application.Features.Automation;
using Prismarket.Application.Features.Notifications;
using Prismarket.Domain.Entities;
using Prismarket.Domain.Enums;

namespace Prismarket.Application.Features.Support;

public sealed record TicketSummaryDto(int Id, string Subject, TicketStatus Status, TicketPriority Priority,
    TicketCategory Category, string Username, string? AssignedAdmin, string? OrderNumber, DateTime CreatedAt,
    DateTime LastMessageAt, int UnreadCount, string? LastMessage);

public sealed record SupportMessageDto(int Id, int TicketId, int? SenderId, string SenderName, bool IsBot,
    bool IsFromStaff, string Text, DateTime CreatedAt);

public sealed record TicketDetailDto(TicketSummaryDto Ticket, IReadOnlyList<SupportMessageDto> Messages);

public sealed record CreateTicketRequest(
    [Required, StringLength(200, MinimumLength = 3)] string Subject,
    [Required, StringLength(4000, MinimumLength = 2)] string Message,
    string? OrderNumber);

public sealed record PostMessageRequest([Required, StringLength(4000, MinimumLength = 1)] string Text);

public sealed record UpdateTicketRequest(TicketStatus? Status, TicketPriority? Priority, bool? AssignToMe);

public interface ISupportService
{
    Task<IReadOnlyList<TicketSummaryDto>> GetMyTicketsAsync(CancellationToken ct);
    Task<TicketDetailDto> CreateAsync(CreateTicketRequest request, CancellationToken ct);
    Task<TicketDetailDto> GetAsync(int ticketId, CancellationToken ct);
    Task<SupportMessageDto> PostMessageAsync(int ticketId, PostMessageRequest request, CancellationToken ct);
    Task CloseAsync(int ticketId, CancellationToken ct);

    Task<PagedResult<TicketSummaryDto>> AdminListAsync(TicketStatus? status, bool onlyMine, PageRequest page, CancellationToken ct);
    Task<TicketDetailDto> AdminUpdateAsync(int ticketId, UpdateTicketRequest request, CancellationToken ct);
}

public sealed class SupportService(
    IAppDbContext db,
    ICurrentUser currentUser,
    ISupportBot bot,
    IAutomationSettingsProvider automation,
    IAutomationEventLog automationLog,
    INotificationService notifications,
    IRealtimePublisher realtime,
    IEmailQueue emails,
    IAppUrls urls,
    TimeProvider clock) : ISupportService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<IReadOnlyList<TicketSummaryDto>> GetMyTicketsAsync(CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        return await Summaries(db.SupportTickets.Where(t => t.UserId == userId)
            .OrderByDescending(t => t.LastMessageAt), forStaff: false).ToListAsync(ct);
    }

    public async Task<TicketDetailDto> CreateAsync(CreateTicketRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        int? orderId = null;
        if (!string.IsNullOrWhiteSpace(request.OrderNumber))
        {
            orderId = await db.Orders.Where(o => o.Number == request.OrderNumber.Trim() && o.UserId == userId)
                .Select(o => (int?)o.Id).FirstOrDefaultAsync(ct);
        }

        var ticket = new SupportTicket
        {
            UserId = userId, Subject = request.Subject.Trim(), OrderId = orderId, LastMessageAt = Now,
            Status = TicketStatus.WaitingForSupport
        };
        ticket.Messages.Add(new SupportMessage { SenderId = userId, Text = request.Message.Trim(), CreatedAt = Now });
        db.SupportTickets.Add(ticket);

        // Event-driven automation: support bot (classification, auto-reply, auto-assignment).
        var (botEnabled, settings) = await automation.GetAsync(AutomationKeys.SupportBot, ct);
        if (botEnabled)
        {
            var answer = bot.Analyze(ticket.Subject, request.Message);
            ticket.Category = answer.Category;
            ticket.Priority = orderId is not null && answer.Priority == TicketPriority.Normal
                ? TicketPriority.High
                : answer.Priority;

            if (settings.GetBool("autoReply") && answer.Reply is not null)
            {
                ticket.Messages.Add(new SupportMessage
                {
                    IsBot = true, IsFromStaff = true, Text = answer.Reply, CreatedAt = Now.AddSeconds(1)
                });
            }

            if (settings.GetBool("autoAssign"))
            {
                ticket.AssignedAdminId = await db.Users
                    .Where(u => u.Role == UserRole.Admin && u.IsActive)
                    .OrderBy(u => db.SupportTickets.Count(t => t.AssignedAdminId == u.Id && t.Status != TicketStatus.Closed))
                    .ThenBy(u => u.Id)
                    .Select(u => (int?)u.Id)
                    .FirstOrDefaultAsync(ct);
            }

            automationLog.Record(AutomationKeys.SupportBot, 1,
                $"Обращение «{ticket.Subject}»: категория {answer.Category}, приоритет {ticket.Priority}" +
                (answer.Reply is not null ? ", отправлен автоответ" : ""));
        }

        await db.SaveChangesAsync(ct);

        if (ticket.AssignedAdminId is { } adminId)
            notifications.Add(adminId, NotificationType.SupportReply, "Новое обращение",
                ticket.Subject, $"/admin/support/{ticket.Id}");
        else
            await notifications.AddForAdminsAsync(NotificationType.SupportReply, "Новое обращение", ticket.Subject,
                $"/admin/support/{ticket.Id}", ct);
        await db.SaveChangesAsync(ct);
        await notifications.FlushAsync(ct);
        await realtime.NotifyAdminsAsync("ticket", new { ticket.Id, ticket.Subject }, ct);

        return await GetAsync(ticket.Id, ct);
    }

    public async Task<TicketDetailDto> GetAsync(int ticketId, CancellationToken ct)
    {
        var ticket = await LoadAccessibleAsync(ticketId, ct);
        var isStaffView = currentUser.IsAdmin && ticket.UserId != currentUser.UserId;

        // Mark messages from the other side as read.
        await db.SupportMessages
            .Where(m => m.TicketId == ticketId && !m.IsRead && m.IsFromStaff != isStaffView)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.IsRead, true), ct);

        var summary = await Summaries(db.SupportTickets.Where(t => t.Id == ticketId), isStaffView).FirstAsync(ct);
        var messages = await db.SupportMessages.AsNoTracking()
            .Where(m => m.TicketId == ticketId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new SupportMessageDto(m.Id, m.TicketId, m.SenderId,
                m.IsBot ? "Помощник Prismarket" : m.Sender!.Username, m.IsBot, m.IsFromStaff, m.Text, m.CreatedAt))
            .ToListAsync(ct);
        return new TicketDetailDto(summary, messages);
    }

    public async Task<SupportMessageDto> PostMessageAsync(int ticketId, PostMessageRequest request, CancellationToken ct)
    {
        var ticket = await LoadAccessibleAsync(ticketId, ct, track: true);
        if (ticket.Status == TicketStatus.Closed && !currentUser.IsAdmin)
            ticket.Status = TicketStatus.WaitingForSupport; // customer re-opens by writing

        var senderId = currentUser.RequireUserId();
        var fromStaff = currentUser.IsAdmin && ticket.UserId != senderId;
        var message = new SupportMessage
        {
            TicketId = ticketId, SenderId = senderId, IsFromStaff = fromStaff, Text = request.Text.Trim(),
            CreatedAt = Now
        };
        db.SupportMessages.Add(message);
        ticket.LastMessageAt = Now;
        ticket.Status = fromStaff ? TicketStatus.WaitingForCustomer : TicketStatus.WaitingForSupport;
        if (fromStaff) ticket.AssignedAdminId ??= senderId;

        if (fromStaff)
        {
            notifications.Add(ticket.UserId, NotificationType.SupportReply, "Ответ поддержки",
                $"Новое сообщение по обращению «{ticket.Subject}»", $"/support/{ticket.Id}");
            var customerEmail = await db.Users.Where(u => u.Id == ticket.UserId).Select(u => u.Email).FirstAsync(ct);
            emails.Enqueue(new EmailEnvelope(customerEmail, $"Ответ по обращению «{ticket.Subject}»",
                EmailTemplates.Layout("Поддержка ответила на ваше обращение",
                    $"<blockquote style=\"margin:0;padding:12px 16px;border-left:3px solid #7c8cff;background:rgba(255,255,255,.04)\">{EmailTemplates.Encode(message.Text)}</blockquote>",
                    "Открыть чат", urls.Build($"/support/{ticket.Id}"))));
        }
        else if (ticket.AssignedAdminId is { } adminId)
        {
            notifications.Add(adminId, NotificationType.SupportReply, "Сообщение от клиента", ticket.Subject,
                $"/admin/support/{ticket.Id}");
        }

        await db.SaveChangesAsync(ct);
        await notifications.FlushAsync(ct);

        var senderName = await db.Users.Where(u => u.Id == senderId).Select(u => u.Username).FirstAsync(ct);
        var dto = new SupportMessageDto(message.Id, ticketId, senderId, senderName, false, fromStaff, message.Text,
            message.CreatedAt);
        await realtime.NotifyTicketAsync(ticketId, "message", dto, ct);
        return dto;
    }

    public async Task CloseAsync(int ticketId, CancellationToken ct)
    {
        var ticket = await LoadAccessibleAsync(ticketId, ct, track: true);
        ticket.Status = TicketStatus.Closed;
        ticket.ClosedAt = Now;
        await db.SaveChangesAsync(ct);
        await realtime.NotifyTicketAsync(ticketId, "status", new { Status = ticket.Status }, ct);
    }

    public Task<PagedResult<TicketSummaryDto>> AdminListAsync(TicketStatus? status, bool onlyMine, PageRequest page,
        CancellationToken ct)
    {
        var query = db.SupportTickets.AsQueryable();
        if (status is not null) query = query.Where(t => t.Status == status);
        if (onlyMine) query = query.Where(t => t.AssignedAdminId == currentUser.UserId);
        // Order on the entity before projecting into the DTO (EF cannot sort by record constructor members).
        var ordered = query
            .OrderBy(t => t.Status == TicketStatus.Closed)
            .ThenByDescending(t => t.Priority)
            .ThenByDescending(t => t.LastMessageAt);
        return Summaries(ordered, forStaff: true).ToPagedAsync(page, ct);
    }

    public async Task<TicketDetailDto> AdminUpdateAsync(int ticketId, UpdateTicketRequest request, CancellationToken ct)
    {
        var ticket = await db.SupportTickets.FirstOrDefaultAsync(t => t.Id == ticketId, ct)
                     ?? throw NotFoundException.For("Обращение", ticketId);
        if (request.Status is not null)
        {
            ticket.Status = request.Status.Value;
            ticket.ClosedAt = request.Status == TicketStatus.Closed ? Now : null;
        }
        if (request.Priority is not null) ticket.Priority = request.Priority.Value;
        if (request.AssignToMe == true) ticket.AssignedAdminId = currentUser.RequireUserId();
        await db.SaveChangesAsync(ct);
        await realtime.NotifyTicketAsync(ticketId, "status", new { Status = ticket.Status }, ct);
        return await GetAsync(ticketId, ct);
    }

    private async Task<SupportTicket> LoadAccessibleAsync(int ticketId, CancellationToken ct, bool track = false)
    {
        var query = track ? db.SupportTickets : db.SupportTickets.AsNoTracking();
        var ticket = await query.FirstOrDefaultAsync(t => t.Id == ticketId, ct)
                     ?? throw NotFoundException.For("Обращение", ticketId);
        if (ticket.UserId != currentUser.RequireUserId() && !currentUser.IsAdmin) throw new ForbiddenException();
        return ticket;
    }

    private static IQueryable<TicketSummaryDto> Summaries(IQueryable<SupportTicket> query, bool forStaff) =>
        query.AsNoTracking().Select(t => new TicketSummaryDto(
            t.Id, t.Subject, t.Status, t.Priority, t.Category, t.User.Username,
            t.AssignedAdmin != null ? t.AssignedAdmin.Username : null,
            t.Order != null ? t.Order.Number : null,
            t.CreatedAt, t.LastMessageAt,
            t.Messages.Count(m => !m.IsRead && m.IsFromStaff != forStaff),
            t.Messages.OrderByDescending(m => m.CreatedAt).Select(m => m.Text).FirstOrDefault()));
}

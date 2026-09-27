using System.Security.Claims;
using Hangfire;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Prismarket.Application.Common;

namespace Prismarket.Api.Infrastructure;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public int? UserId =>
        int.TryParse(Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub)
                     ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
    public bool IsAdmin => Principal?.IsInRole(Roles.Admin) == true;
    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
    public string? UserAgent => accessor.HttpContext?.Request.Headers.UserAgent.ToString();
}

/// <summary>Maps business exceptions to RFC 7807 ProblemDetails responses.</summary>
public sealed class AppExceptionHandler(IProblemDetailsService problemDetails, ILogger<AppExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            AppException app => (app.StatusCode, app.Message),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict,
                "Данные были изменены другим запросом. Повторите действие."),
            DbUpdateException { InnerException: Npgsql.PostgresException { SqlState: "23505" } } =>
                (StatusCodes.Status409Conflict, "Запись с такими данными уже существует."),
            OperationCanceledException => (499, "Запрос отменён."),
            _ => (StatusCodes.Status500InternalServerError, "Внутренняя ошибка сервера.")
        };

        if (status >= 500) logger.LogError(exception, "Unhandled exception");

        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title }
        });
    }
}

/// <summary>Only admins may open the Hangfire dashboard (JWT is passed as ?access_token= or cookie).</summary>
public sealed class HangfireAdminFilter : IDashboardAsyncAuthorizationFilter
{
    public async Task<bool> AuthorizeAsync(DashboardContext context)
    {
        var http = context.GetHttpContext();
        var result = await http.AuthenticateAsync("Bearer");
        return result.Succeeded && result.Principal.IsInRole(Roles.Admin);
    }
}

/// <summary>SignalR hub: users receive personal notifications, admins — store events, everyone — ticket chats.</summary>
[Authorize]
public sealed class StoreHub(IAppDbContext db) : Hub
{
    public const string Path = "/hubs/store";
    public static string UserGroup(int userId) => $"user:{userId}";
    public const string AdminsGroup = "admins";
    public static string TicketGroup(int ticketId) => $"ticket:{ticketId}";

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirstValue(JwtRegisteredClaimNames.Sub)
                     ?? Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is not null) await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
        if (Context.User?.IsInRole(Roles.Admin) == true)
            await Groups.AddToGroupAsync(Context.ConnectionId, AdminsGroup);
        await base.OnConnectedAsync();
    }

    public async Task JoinTicket(int ticketId)
    {
        var userIdValue = Context.User?.FindFirstValue(JwtRegisteredClaimNames.Sub)
                          ?? Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdValue, out var userId)) return;
        var isAdmin = Context.User?.IsInRole(Roles.Admin) == true;
        var allowed = isAdmin || await db.SupportTickets.AnyAsync(t => t.Id == ticketId && t.UserId == userId);
        if (allowed) await Groups.AddToGroupAsync(Context.ConnectionId, TicketGroup(ticketId));
    }

    public Task LeaveTicket(int ticketId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, TicketGroup(ticketId));
}

public sealed class SignalRRealtimePublisher(IHubContext<StoreHub> hub, ILogger<SignalRRealtimePublisher> logger)
    : IRealtimePublisher
{
    public Task NotifyUserAsync(int userId, string eventName, object payload, CancellationToken ct = default) =>
        Safe(() => hub.Clients.Group(StoreHub.UserGroup(userId)).SendAsync(eventName, payload, ct));

    public Task NotifyAdminsAsync(string eventName, object payload, CancellationToken ct = default) =>
        Safe(() => hub.Clients.Group(StoreHub.AdminsGroup).SendAsync(eventName, payload, ct));

    public Task NotifyTicketAsync(int ticketId, string eventName, object payload, CancellationToken ct = default) =>
        Safe(() => hub.Clients.Group(StoreHub.TicketGroup(ticketId)).SendAsync(eventName, payload, ct));

    /// <summary>Realtime delivery is best-effort — never fail the business operation because of it.</summary>
    private async Task Safe(Func<Task> send)
    {
        try { await send(); }
        catch (Exception ex) { logger.LogWarning(ex, "Realtime notification failed"); }
    }
}

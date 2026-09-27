using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Prismarket.Application.Common;
using Prismarket.Application.Features.Auth;
using Prismarket.Application.Features.Notifications;
using Prismarket.Application.Features.Orders;
using Prismarket.Application.Features.Profile;
using Prismarket.Domain.Entities;
using Prismarket.Domain.Enums;

namespace Prismarket.Application.Features.Admin;

public sealed record AdminUserDto(int Id, string Username, string Email, UserRole Role, bool IsActive,
    bool EmailConfirmed, bool TwoFactorEnabled, decimal Balance, DateTime CreatedAt, DateTime? LastLoginAt,
    int OrdersCount, decimal TotalSpent);

public sealed record AdminUserDetailDto(AdminUserDto User, IReadOnlyList<LibraryItemDto> Library,
    IReadOnlyList<OrderDto> Orders, IReadOnlyList<BalanceTransactionDto> Transactions);

public sealed record UpdateUserRequest(UserRole? Role, bool? IsActive, bool? EmailConfirmed);

public sealed record AdjustBalanceRequest([Range(-100000, 100000)] decimal Amount, [Required, StringLength(200)] string Reason);

public sealed record AuditLogDto(int Id, int? UserId, string? Username, string Action, string EntityName,
    string? EntityId, string? Changes, string? IpAddress, DateTime CreatedAt);

public interface IAdminUserService
{
    Task<PagedResult<AdminUserDto>> ListAsync(string? search, UserRole? role, PageRequest page, CancellationToken ct);
    Task<AdminUserDetailDto> GetAsync(int id, CancellationToken ct);
    Task<AdminUserDto> UpdateAsync(int id, UpdateUserRequest request, CancellationToken ct);
    Task<AdminUserDto> AdjustBalanceAsync(int id, AdjustBalanceRequest request, CancellationToken ct);
    Task<PagedResult<AuditLogDto>> GetAuditLogAsync(string? entity, int? userId, PageRequest page, CancellationToken ct);
}

public interface IAdminOrderService
{
    Task<PagedResult<OrderDto>> ListAsync(OrderStatus? status, string? search, PageRequest page, CancellationToken ct);
    Task<OrderDto> RefundAsync(string number, string reason, CancellationToken ct);
    Task<OrderDto> MarkPaidAsync(string number, CancellationToken ct);
}

public sealed class AdminUserService(IAppDbContext db, ICurrentUser currentUser, INotificationService notifications,
    TimeProvider clock) : IAdminUserService
{
    public Task<PagedResult<AdminUserDto>> ListAsync(string? search, UserRole? role, PageRequest page,
        CancellationToken ct)
    {
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim().ToLowerInvariant()}%";
            query = query.Where(u => EF.Functions.Like(u.Username.ToLower(), term) || EF.Functions.Like(u.Email.ToLower(), term));
        }
        if (role is not null) query = query.Where(u => u.Role == role);
        return Project(query.OrderByDescending(u => u.CreatedAt)).ToPagedAsync(page, ct);
    }

    public async Task<AdminUserDetailDto> GetAsync(int id, CancellationToken ct)
    {
        var user = await Project(db.Users.Where(u => u.Id == id)).FirstOrDefaultAsync(ct)
                   ?? throw NotFoundException.For("Пользователь", id);

        var library = await db.LibraryItems.AsNoTracking().Where(l => l.UserId == id)
            .OrderByDescending(l => l.PurchasedAt)
            .Select(l => new LibraryItemDto(l.Id, l.Product.GameId, l.Product.Game.Slug, l.Product.Game.Title,
                l.Product.Game.HeaderImageUrl, l.Product.Game.Platform.Name, l.Product.Kind, l.Product.Edition,
                null, null, null, null, l.PurchasedAt, l.IsActivated, l.ActivatedAt, l.OrderItem.Order.Number))
            .ToListAsync(ct);

        var orders = await db.Orders.AsNoTracking().Where(o => o.UserId == id)
            .Include(o => o.Items).ThenInclude(i => i.Product).ThenInclude(p => p.Game)
            .Include(o => o.Payments).Include(o => o.PromoCode)
            .OrderByDescending(o => o.CreatedAt).Take(50).AsSplitQuery().ToListAsync(ct);

        var transactions = await db.BalanceTransactions.AsNoTracking().Where(t => t.UserId == id)
            .OrderByDescending(t => t.CreatedAt).Take(50)
            .Select(t => new BalanceTransactionDto(t.Id, t.Amount, t.BalanceAfter, t.Type, t.Description, t.CreatedAt))
            .ToListAsync(ct);

        return new AdminUserDetailDto(user, library, orders.Select(o => OrderMapping.ToDto(o, false)).ToList(),
            transactions);
    }

    public async Task<AdminUserDto> UpdateAsync(int id, UpdateUserRequest request, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw NotFoundException.For("Пользователь", id);
        if (id == currentUser.UserId && (request.Role == UserRole.Customer || request.IsActive == false))
            throw new BusinessRuleException("Нельзя снять права или заблокировать самого себя.");

        if (request.Role is not null) user.Role = request.Role.Value;
        if (request.EmailConfirmed is not null) user.EmailConfirmed = request.EmailConfirmed.Value;
        if (request.IsActive is not null)
        {
            user.IsActive = request.IsActive.Value;
            if (!user.IsActive)
                await db.RefreshTokens.Where(t => t.UserId == id && t.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, clock.GetUtcNow().UtcDateTime), ct);
        }
        await db.SaveChangesAsync(ct);
        return await Project(db.Users.Where(u => u.Id == id)).FirstAsync(ct);
    }

    public async Task<AdminUserDto> AdjustBalanceAsync(int id, AdjustBalanceRequest request, CancellationToken ct)
    {
        if (request.Amount == 0) throw new BusinessRuleException("Сумма не может быть нулевой.");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw NotFoundException.For("Пользователь", id);
        if (request.Amount > 0) user.Credit(request.Amount);
        else if (user.Balance + request.Amount < 0) throw new BusinessRuleException("Баланс не может стать отрицательным.");
        else user.Debit(-request.Amount);

        db.BalanceTransactions.Add(new BalanceTransaction
        {
            UserId = id, Amount = request.Amount, BalanceAfter = user.Balance,
            Type = BalanceTransactionType.AdminAdjustment, Description = request.Reason,
            CreatedAt = clock.GetUtcNow().UtcDateTime
        });
        notifications.Add(id, NotificationType.System, "Изменение баланса",
            $"{(request.Amount > 0 ? "+" : "")}{request.Amount:0.00} BYN: {request.Reason}", "/profile?tab=balance");
        await db.SaveChangesAsync(ct);
        await notifications.FlushAsync(ct);
        return await Project(db.Users.Where(u => u.Id == id)).FirstAsync(ct);
    }

    public Task<PagedResult<AuditLogDto>> GetAuditLogAsync(string? entity, int? userId, PageRequest page,
        CancellationToken ct)
    {
        var query = db.AuditLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(entity)) query = query.Where(a => a.EntityName == entity);
        if (userId is not null) query = query.Where(a => a.UserId == userId);
        return query.OrderByDescending(a => a.CreatedAt)
            .Select(a => new AuditLogDto(a.Id, a.UserId,
                db.Users.Where(u => u.Id == a.UserId).Select(u => u.Username).FirstOrDefault(),
                a.Action, a.EntityName, a.EntityId, a.Changes, a.IpAddress, a.CreatedAt))
            .ToPagedAsync(page, ct);
    }

    private static IQueryable<AdminUserDto> Project(IQueryable<User> query) => query.AsNoTracking()
        .Select(u => new AdminUserDto(u.Id, u.Username, u.Email, u.Role, u.IsActive, u.EmailConfirmed,
            u.TwoFactorEnabled, u.Balance, u.CreatedAt, u.LastLoginAt,
            u.Orders.Count(o => o.Status == OrderStatus.Completed),
            u.Orders.Where(o => o.Status == OrderStatus.Completed).Sum(o => o.Total)));
}

public sealed class AdminOrderService(
    IAppDbContext db,
    IPaymentServiceFacade payments,
    INotificationService notifications,
    TimeProvider clock) : IAdminOrderService
{
    public async Task<PagedResult<OrderDto>> ListAsync(OrderStatus? status, string? search, PageRequest page,
        CancellationToken ct)
    {
        var query = db.Orders.AsNoTracking()
            .Include(o => o.User)
            .Include(o => o.Items).ThenInclude(i => i.Product).ThenInclude(p => p.Game)
            .Include(o => o.Payments).Include(o => o.PromoCode)
            .AsSplitQuery();
        IQueryable<Order> filtered = query;
        if (status is not null) filtered = filtered.Where(o => o.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim().ToLowerInvariant()}%";
            filtered = filtered.Where(o => EF.Functions.Like(o.Number.ToLower(), term)
                                           || EF.Functions.Like(o.User.Username.ToLower(), term)
                                           || EF.Functions.Like(o.User.Email.ToLower(), term));
        }
        var result = await filtered.OrderByDescending(o => o.CreatedAt).ToPagedAsync(page, ct);
        return new PagedResult<OrderDto>(result.Items.Select(o => OrderMapping.ToDto(o, false, o.User.Username)).ToList(),
            result.Page, result.PageSize, result.TotalCount);
    }

    /// <summary>Refund to the internal balance. Keys stay sold (they were delivered) — the library entry is removed.</summary>
    public async Task<OrderDto> RefundAsync(string number, string reason, CancellationToken ct)
    {
        var order = await db.Orders.Include(o => o.Items).Include(o => o.User)
                        .FirstOrDefaultAsync(o => o.Number == number, ct) ?? throw NotFoundException.For("Заказ", number);
        if (order.Status != OrderStatus.Completed)
            throw new BusinessRuleException("Вернуть можно только выполненный заказ.");

        var itemIds = order.Items.Select(i => i.Id).ToList();
        await db.LibraryItems.Where(l => itemIds.Contains(l.OrderItemId)).ExecuteDeleteAsync(ct);

        order.Status = OrderStatus.Refunded;
        order.CancellationReason = reason;
        order.User.Credit(order.Total);
        db.BalanceTransactions.Add(new BalanceTransaction
        {
            UserId = order.UserId, Amount = order.Total, BalanceAfter = order.User.Balance,
            Type = BalanceTransactionType.Refund, Description = $"Возврат по заказу {order.Number}: {reason}",
            OrderId = order.Id, CreatedAt = clock.GetUtcNow().UtcDateTime
        });
        notifications.Add(order.UserId, NotificationType.System, "Возврат средств",
            $"По заказу {order.Number} возвращено {order.Total:0.00} BYN на баланс.", "/profile?tab=balance");
        await db.SaveChangesAsync(ct);
        await notifications.FlushAsync(ct);
        return (await ListAsync(null, number, new PageRequest(1, 1), ct)).Items.First();
    }

    /// <summary>Manual confirmation (e.g. ERIP payment checked by an operator).</summary>
    public async Task<OrderDto> MarkPaidAsync(string number, CancellationToken ct)
    {
        var order = await db.Orders.Include(o => o.Payments)
                        .FirstOrDefaultAsync(o => o.Number == number, ct) ?? throw NotFoundException.For("Заказ", number);
        var payment = order.Payments.Where(p => p.Status == PaymentStatus.Pending).OrderByDescending(p => p.Id)
                          .FirstOrDefault() ?? throw new BusinessRuleException("У заказа нет ожидающего платежа.");
        await payments.ConfirmAsAdminAsync(payment, ct);
        return (await ListAsync(null, number, new PageRequest(1, 1), ct)).Items.First();
    }
}

/// <summary>Narrow interface over payment completion for admin use (Interface Segregation).</summary>
public interface IPaymentServiceFacade
{
    Task ConfirmAsAdminAsync(Payment payment, CancellationToken ct);
}

using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prismarket.Application.Common;
using Prismarket.Application.Features.Automation;
using Prismarket.Application.Features.Notifications;
using Prismarket.Application.Features.Payments;
using Prismarket.Domain.Entities;
using Prismarket.Domain.Enums;

namespace Prismarket.Application.Features.Orders;

/// <summary>Reserves stock with row-level locks (implemented with raw SQL in Infrastructure).</summary>
public interface IStockReservationService
{
    Task<IReadOnlyList<ProductKey>> ReserveKeysAsync(int productId, int quantity, int orderId, CancellationToken ct);
    Task<IReadOnlyList<ProductAccount>> ReserveAccountsAsync(int productId, int quantity, int orderId, CancellationToken ct);
    Task<int> ReleaseAsync(int orderId, CancellationToken ct);
}

public interface IOrderService
{
    Task<PromoPreviewDto> PreviewPromoAsync(string code, CancellationToken ct);
    Task<CheckoutResultDto> CheckoutAsync(CheckoutRequest request, CancellationToken ct);
    Task<PagedResult<OrderDto>> GetMyOrdersAsync(PageRequest page, CancellationToken ct);
    Task<OrderDto> GetMyOrderAsync(string number, CancellationToken ct);
    Task<OrderDto> CancelMyOrderAsync(string number, CancellationToken ct);
    Task<PaymentDto> RetryPaymentAsync(string number, PaymentMethod method, CancellationToken ct);

    /// <summary>Cancels an unpaid order and returns reserved stock (used by user action and the expiry job).</summary>
    Task ExpireAsync(Order order, OrderStatus status, string reason, CancellationToken ct);
}

public sealed class OrderService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IStockReservationService stock,
    IPaymentStrategyResolver strategies,
    IPaymentService payments,
    IAutomationSettingsProvider automation,
    INotificationService notifications,
    IRealtimePublisher realtime,
    TimeProvider clock,
    ILogger<OrderService> logger) : IOrderService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<PromoPreviewDto> PreviewPromoAsync(string code, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var promo = await FindPromoAsync(code, userId, ct);
        var subtotal = await CartTotalAsync(userId, ct);
        var discount = CalculatePromoDiscount(subtotal, promo.DiscountPercent);
        return new PromoPreviewDto(promo.Code, promo.DiscountPercent, subtotal, discount, subtotal - discount);
    }

    public async Task<CheckoutResultDto> CheckoutAsync(CheckoutRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
        if (!user.EmailConfirmed)
            throw new BusinessRuleException("Подтвердите e-mail, чтобы совершать покупки — ключи приходят на почту.");

        var strategy = strategies.Resolve(request.PaymentMethod);

        var cart = await db.CartItems
            .Include(c => c.Product).ThenInclude(p => p.Game)
            .Where(c => c.UserId == userId)
            .ToListAsync(ct);
        if (cart.Count == 0) throw new BusinessRuleException("Корзина пуста.");

        var unavailable = cart.FirstOrDefault(c => !c.Product.IsAvailable || !c.Product.Game.IsAvailable);
        if (unavailable is not null)
            throw new BusinessRuleException($"«{unavailable.Product.Game.Title}» больше недоступна — удалите её из корзины.");

        PromoCode? promo = null;
        if (!string.IsNullOrWhiteSpace(request.PromoCode))
            promo = await FindPromoAsync(request.PromoCode, userId, ct);

        var (_, settings) = await automation.GetAsync(AutomationKeys.OrderExpiry, ct);
        var reservationMinutes = Math.Clamp(settings.GetInt("reservationMinutes"), 1, 24 * 60);

        await using var tx = await db.BeginTransactionAsync(ct);

        var order = new Order
        {
            Number = GenerateOrderNumber(),
            UserId = userId,
            PaymentMethod = request.PaymentMethod,
            PromoCode = promo,
            ExpiresAt = Now.AddMinutes(reservationMinutes)
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct); // need order.Id for reservations

        foreach (var line in cart)
        {
            var product = line.Product;
            var finalPrice = product.FinalPrice;

            if (product.Kind == ProductKind.Account)
            {
                var accounts = await stock.ReserveAccountsAsync(product.Id, line.Quantity, order.Id, ct);
                EnsureEnough(accounts.Count, line.Quantity, product);
                foreach (var account in accounts)
                    order.Items.Add(NewItem(product, finalPrice, keyId: null, accountId: account.Id));
            }
            else
            {
                var keys = await stock.ReserveKeysAsync(product.Id, line.Quantity, order.Id, ct);
                EnsureEnough(keys.Count, line.Quantity, product);
                foreach (var key in keys)
                    order.Items.Add(NewItem(product, finalPrice, keyId: key.Id, accountId: null));
            }
        }

        order.Subtotal = order.Items.Sum(i => i.FinalPrice);
        order.DiscountAmount = promo is null ? 0 : CalculatePromoDiscount(order.Subtotal, promo.DiscountPercent);
        order.Total = order.Subtotal - order.DiscountAmount;
        if (promo is not null) promo.UsedCount++;

        db.CartItems.RemoveRange(cart);

        var payment = new Payment
        {
            UserId = userId, Order = order, Purpose = PaymentPurpose.Order, Method = request.PaymentMethod,
            Amount = order.Total
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync(ct);

        if (order.Total == 0m)
        {
            // 100% promo code — nothing to pay.
            await payments.CompleteAsync(payment, ct);
        }
        else
        {
            var init = await strategy.InitiateAsync(payment, user, ct);
            PaymentService.Apply(payment, init);
            if (init.Completed) await payments.CompleteAsync(payment, ct);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await notifications.FlushAsync(ct);

        logger.LogInformation("Order {Number} created by user {UserId} for {Total} BYN via {Method}",
            order.Number, userId, order.Total, request.PaymentMethod);
        await realtime.NotifyAdminsAsync("order", new { order.Number, order.Total, Status = order.Status.ToString() }, ct);

        var dto = await LoadOrderDtoAsync(order.Id, userId, ct);
        return new CheckoutResultDto(dto, PaymentDto.From(payment));
    }

    public async Task<PagedResult<OrderDto>> GetMyOrdersAsync(PageRequest page, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var query = OrdersWithDetails().Where(o => o.UserId == userId).OrderByDescending(o => o.CreatedAt);
        var result = await query.ToPagedAsync(page, ct);
        return new PagedResult<OrderDto>(result.Items.Select(o => OrderMapping.ToDto(o, true)).ToList(),
            result.Page, result.PageSize, result.TotalCount);
    }

    public async Task<OrderDto> GetMyOrderAsync(string number, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var order = await OrdersWithDetails().FirstOrDefaultAsync(o => o.Number == number, ct)
                    ?? throw NotFoundException.For("Заказ", number);
        if (order.UserId != userId && !currentUser.IsAdmin) throw new ForbiddenException();
        return OrderMapping.ToDto(order, order.UserId == userId);
    }

    public async Task<OrderDto> CancelMyOrderAsync(string number, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var order = await db.Orders.Include(o => o.Payments)
                        .FirstOrDefaultAsync(o => o.Number == number && o.UserId == userId, ct)
                    ?? throw NotFoundException.For("Заказ", number);
        if (order.Status != OrderStatus.AwaitingPayment)
            throw new BusinessRuleException("Отменить можно только неоплаченный заказ.");

        await ExpireAsync(order, OrderStatus.Cancelled, "Отменён покупателем", ct);
        return await LoadOrderDtoAsync(order.Id, userId, ct);
    }

    public async Task<PaymentDto> RetryPaymentAsync(string number, PaymentMethod method, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var order = await db.Orders.Include(o => o.Payments)
                        .FirstOrDefaultAsync(o => o.Number == number && o.UserId == userId, ct)
                    ?? throw NotFoundException.For("Заказ", number);
        if (order.Status != OrderStatus.AwaitingPayment || order.ExpiresAt <= Now)
            throw new BusinessRuleException("Заказ уже нельзя оплатить.");

        var strategy = strategies.Resolve(method);
        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);

        await using var tx = await db.BeginTransactionAsync(ct);
        foreach (var pending in order.Payments.Where(p => p.Status == PaymentStatus.Pending))
            pending.Status = PaymentStatus.Cancelled;

        var payment = new Payment
        {
            UserId = userId, OrderId = order.Id, Purpose = PaymentPurpose.Order, Method = method, Amount = order.Total
        };
        order.PaymentMethod = method;
        db.Payments.Add(payment);
        await db.SaveChangesAsync(ct);

        var init = await strategy.InitiateAsync(payment, user, ct);
        PaymentService.Apply(payment, init);
        if (init.Completed) await payments.CompleteAsync(payment, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await notifications.FlushAsync(ct);
        payment.Order = order;
        return PaymentDto.From(payment);
    }

    public async Task ExpireAsync(Order order, OrderStatus status, string reason, CancellationToken ct)
    {
        order.Cancel(status, reason);
        foreach (var payment in order.Payments.Where(p => p.Status == PaymentStatus.Pending))
            payment.Status = PaymentStatus.Cancelled;

        if (order.PromoCodeId is not null)
            await db.PromoCodes.Where(p => p.Id == order.PromoCodeId && p.UsedCount > 0)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.UsedCount, p => p.UsedCount - 1), ct);

        await stock.ReleaseAsync(order.Id, ct);
        await db.SaveChangesAsync(ct);
    }

    private IQueryable<Order> OrdersWithDetails() => db.Orders.AsNoTracking()
        .Include(o => o.Items).ThenInclude(i => i.Product).ThenInclude(p => p.Game)
        .Include(o => o.Items).ThenInclude(i => i.Key)
        .Include(o => o.Items).ThenInclude(i => i.Account)
        .Include(o => o.Payments)
        .Include(o => o.PromoCode)
        .AsSplitQuery();

    private async Task<OrderDto> LoadOrderDtoAsync(int orderId, int userId, CancellationToken ct)
    {
        var order = await OrdersWithDetails().FirstAsync(o => o.Id == orderId, ct);
        return OrderMapping.ToDto(order, order.UserId == userId);
    }

    private async Task<PromoCode> FindPromoAsync(string code, int userId, CancellationToken ct)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var promo = await db.PromoCodes.FirstOrDefaultAsync(p => p.Code == normalized, ct);
        if (promo is null || !promo.CanBeUsedBy(userId, Now))
            throw new BusinessRuleException("Промокод недействителен или истёк.");
        return promo;
    }

    private async Task<decimal> CartTotalAsync(int userId, CancellationToken ct)
    {
        var lines = await db.CartItems.AsNoTracking().Where(c => c.UserId == userId)
            .Select(c => new { c.Product.Price, c.Product.DiscountPercent, c.Quantity }).ToListAsync(ct);
        return lines.Sum(l => Product.ApplyDiscount(l.Price, l.DiscountPercent) * l.Quantity);
    }

    public static decimal CalculatePromoDiscount(decimal subtotal, int percent) =>
        Math.Round(subtotal * Math.Clamp(percent, 0, 100) / 100m, 2, MidpointRounding.AwayFromZero);

    private static OrderItem NewItem(Product product, decimal finalPrice, int? keyId, int? accountId) => new()
    {
        ProductId = product.Id,
        UnitPrice = product.Price,
        DiscountPercent = product.DiscountPercent,
        FinalPrice = finalPrice,
        KeyId = keyId,
        AccountId = accountId
    };

    private static void EnsureEnough(int reserved, int requested, Product product)
    {
        if (reserved < requested)
            throw new BusinessRuleException(
                $"Недостаточно ключей для «{product.Game.Title} ({product.Edition})»: в наличии {reserved}.");
    }

    private string GenerateOrderNumber()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var suffix = new string(Enumerable.Range(0, 5)
            .Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
        return $"PM-{Now:yyMMdd}-{suffix}";
    }
}

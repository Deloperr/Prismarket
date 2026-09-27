using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prismarket.Application.Common;
using Prismarket.Application.Features.Admin;
using Prismarket.Application.Features.Notifications;
using Prismarket.Application.Features.Orders;
using Prismarket.Domain.Entities;
using Prismarket.Domain.Enums;

namespace Prismarket.Application.Features.Payments;

public sealed record PaymentMethodDto(PaymentMethod Method, string Name, string Description, bool SupportsDeposit);

public sealed record PaymentDto(
    int Id, PaymentPurpose Purpose, PaymentMethod Method, PaymentStatus Status, decimal Amount,
    string? RedirectUrl, string? Instructions, string? OrderNumber, DateTime CreatedAt, DateTime? CompletedAt)
{
    public static PaymentDto From(Payment p) => new(p.Id, p.Purpose, p.Method, p.Status, p.Amount, p.RedirectUrl,
        p.Instructions, p.Order?.Number, p.CreatedAt, p.CompletedAt);
}

public sealed record DepositRequest([Range(1, 5000)] decimal Amount, PaymentMethod Method);

public interface IPaymentService
{
    IReadOnlyList<PaymentMethodDto> GetMethods();
    Task<PaymentDto> CreateDepositAsync(DepositRequest request, CancellationToken ct);
    Task<PaymentDto> GetAsync(int paymentId, CancellationToken ct);

    /// <summary>Called by the test gateway page (for the current user).</summary>
    Task<PaymentDto> ConfirmTestPaymentAsync(int paymentId, bool success, CancellationToken ct);

    /// <summary>Called by provider webhooks (Stripe) — no user context.</summary>
    Task HandleProviderResultAsync(PaymentMethod method, string providerReference, bool success, CancellationToken ct);

    /// <summary>Marks payment as succeeded and runs downstream logic (deposit credit or order fulfillment).</summary>
    Task CompleteAsync(Payment payment, CancellationToken ct);
}

public sealed class PaymentService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IPaymentStrategyResolver strategies,
    IOrderFulfillmentService fulfillment,
    INotificationService notifications,
    TimeProvider clock,
    ILogger<PaymentService> logger) : IPaymentService, IPaymentServiceFacade
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public IReadOnlyList<PaymentMethodDto> GetMethods() =>
        strategies.Available.Select(s => new PaymentMethodDto(s.Method, s.DisplayName, s.Description,
            s.SupportsDeposit)).ToList();

    public async Task<PaymentDto> CreateDepositAsync(DepositRequest request, CancellationToken ct)
    {
        var strategy = strategies.Resolve(request.Method);
        if (!strategy.SupportsDeposit)
            throw new BusinessRuleException("Этот способ не подходит для пополнения баланса.");

        var user = await db.Users.FirstAsync(u => u.Id == currentUser.RequireUserId(), ct);
        var payment = new Payment
        {
            UserId = user.Id, Purpose = PaymentPurpose.Deposit, Method = request.Method,
            Amount = Math.Round(request.Amount, 2)
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync(ct);

        var init = await strategy.InitiateAsync(payment, user, ct);
        Apply(payment, init);
        if (init.Completed) await CompleteAsync(payment, ct);
        await db.SaveChangesAsync(ct);
        await notifications.FlushAsync(ct);
        return PaymentDto.From(payment);
    }

    public async Task<PaymentDto> GetAsync(int paymentId, CancellationToken ct)
    {
        var payment = await LoadOwnPaymentAsync(paymentId, ct);
        return PaymentDto.From(payment);
    }

    public async Task<PaymentDto> ConfirmTestPaymentAsync(int paymentId, bool success, CancellationToken ct)
    {
        var payment = await LoadOwnPaymentAsync(paymentId, ct);
        if (payment.Method is not (PaymentMethod.Card or PaymentMethod.Erip))
            throw new BusinessRuleException("Подтверждать вручную можно только тестовые платежи.");

        await ApplyResultAsync(payment, success, ct);
        return PaymentDto.From(payment);
    }

    public async Task HandleProviderResultAsync(PaymentMethod method, string providerReference, bool success,
        CancellationToken ct)
    {
        var payment = await db.Payments.Include(p => p.Order)
            .FirstOrDefaultAsync(p => p.Method == method && p.ProviderReference == providerReference, ct);
        if (payment is null)
        {
            logger.LogWarning("Webhook for unknown payment {Method}/{Reference}", method, providerReference);
            return;
        }
        await ApplyResultAsync(payment, success, ct);
    }

    public Task ConfirmAsAdminAsync(Payment payment, CancellationToken ct) => ApplyResultAsync(payment, true, ct);

    private async Task ApplyResultAsync(Payment payment, bool success, CancellationToken ct)
    {
        // Idempotency: providers may deliver the same callback several times.
        if (payment.Status != PaymentStatus.Pending) return;

        await using var tx = await db.BeginTransactionAsync(ct);
        if (success)
        {
            await CompleteAsync(payment, ct);
        }
        else
        {
            payment.Status = PaymentStatus.Failed;
            payment.FailureReason = "Платёж отклонён";
            payment.CompletedAt = Now;
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await notifications.FlushAsync(ct);
    }

    public async Task CompleteAsync(Payment payment, CancellationToken ct)
    {
        payment.Status = PaymentStatus.Succeeded;
        payment.CompletedAt = Now;
        var user = await db.Users.FirstAsync(u => u.Id == payment.UserId, ct);

        if (payment.Purpose == PaymentPurpose.Deposit)
        {
            CreditBalance(user, payment.Amount, BalanceTransactionType.Deposit, "Пополнение баланса", null);
            notifications.Add(user.Id, NotificationType.System, "Баланс пополнен",
                $"На баланс зачислено {payment.Amount:0.00} BYN.", "/profile?tab=balance");
            return;
        }

        var order = await db.Orders.Include(o => o.Items)
            .FirstAsync(o => o.Id == payment.OrderId, ct);

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            // Paid too late (order expired/cancelled): money is not lost, it goes to the wallet.
            if (payment.Method != PaymentMethod.Balance)
            {
                CreditBalance(user, payment.Amount, BalanceTransactionType.Refund,
                    $"Заказ {order.Number} уже закрыт — оплата зачислена на баланс", order.Id);
                notifications.Add(user.Id, NotificationType.System, "Оплата зачислена на баланс",
                    $"Заказ {order.Number} был отменён до поступления оплаты, поэтому {payment.Amount:0.00} BYN зачислены на ваш баланс.",
                    "/profile?tab=balance");
            }
            return;
        }

        order.MarkPaid(Now);
        await fulfillment.FulfillAsync(order, ct);
    }

    private void CreditBalance(User user, decimal amount, BalanceTransactionType type, string description, int? orderId)
    {
        user.Credit(amount);
        db.BalanceTransactions.Add(new BalanceTransaction
        {
            UserId = user.Id, Amount = amount, BalanceAfter = user.Balance, Type = type,
            Description = description, OrderId = orderId, CreatedAt = Now
        });
    }

    private async Task<Payment> LoadOwnPaymentAsync(int paymentId, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var payment = await db.Payments.Include(p => p.Order)
            .FirstOrDefaultAsync(p => p.Id == paymentId, ct) ?? throw NotFoundException.For("Платёж", paymentId);
        if (payment.UserId != userId && !currentUser.IsAdmin) throw new ForbiddenException();
        return payment;
    }

    internal static void Apply(Payment payment, PaymentInitResult init)
    {
        payment.ProviderReference = init.ProviderReference;
        payment.RedirectUrl = init.RedirectUrl;
        payment.Instructions = init.Instructions;
    }
}

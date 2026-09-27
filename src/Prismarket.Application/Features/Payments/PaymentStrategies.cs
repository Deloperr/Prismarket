using Prismarket.Application.Common;
using Prismarket.Domain.Entities;
using Prismarket.Domain.Enums;

namespace Prismarket.Application.Features.Payments;

public sealed record PaymentInitResult(
    bool Completed,
    string? ProviderReference = null,
    string? RedirectUrl = null,
    string? Instructions = null);

/// <summary>
/// Strategy pattern: every payment provider implements the same contract. Adding a new provider means
/// adding a class and registering it in DI — no existing code changes (Open/Closed principle).
/// </summary>
public interface IPaymentStrategy
{
    PaymentMethod Method { get; }
    string DisplayName { get; }
    string Description { get; }
    bool IsEnabled { get; }
    bool SupportsDeposit { get; }

    Task<PaymentInitResult> InitiateAsync(Payment payment, User user, CancellationToken ct);
}

public interface IPaymentStrategyResolver
{
    IPaymentStrategy Resolve(PaymentMethod method);
    IReadOnlyList<IPaymentStrategy> Available { get; }
}

public sealed class PaymentStrategyResolver(IEnumerable<IPaymentStrategy> strategies) : IPaymentStrategyResolver
{
    private readonly Dictionary<PaymentMethod, IPaymentStrategy> _strategies = strategies.ToDictionary(s => s.Method);

    public IReadOnlyList<IPaymentStrategy> Available => _strategies.Values.Where(s => s.IsEnabled).ToList();

    public IPaymentStrategy Resolve(PaymentMethod method) =>
        _strategies.TryGetValue(method, out var strategy) && strategy.IsEnabled
            ? strategy
            : throw new BusinessRuleException("Этот способ оплаты сейчас недоступен.");
}

/// <summary>Payment from the internal wallet — completes instantly.</summary>
public sealed class BalancePaymentStrategy(IAppDbContext db, TimeProvider clock) : IPaymentStrategy
{
    public PaymentMethod Method => PaymentMethod.Balance;
    public string DisplayName => "Баланс Prismarket";
    public string Description => "Мгновенное списание с внутреннего баланса";
    public bool IsEnabled => true;
    public bool SupportsDeposit => false;

    public Task<PaymentInitResult> InitiateAsync(Payment payment, User user, CancellationToken ct)
    {
        if (payment.Purpose != PaymentPurpose.Order)
            throw new BusinessRuleException("Нельзя пополнить баланс с баланса.");
        if (user.Balance < payment.Amount)
            throw new BusinessRuleException(
                $"Недостаточно средств на балансе: нужно {payment.Amount:0.00} BYN, доступно {user.Balance:0.00} BYN.");

        user.Debit(payment.Amount);
        db.BalanceTransactions.Add(new BalanceTransaction
        {
            UserId = user.Id,
            Amount = -payment.Amount,
            BalanceAfter = user.Balance,
            Type = BalanceTransactionType.Purchase,
            Description = "Оплата заказа",
            OrderId = payment.OrderId,
            CreatedAt = clock.GetUtcNow().UtcDateTime
        });
        return Task.FromResult(new PaymentInitResult(true, $"balance-{payment.Id}"));
    }
}

/// <summary>
/// Test card gateway: redirects to a built-in payment page. Emulates how a real acquiring
/// provider works (redirect → callback) without real money.
/// </summary>
public sealed class TestCardPaymentStrategy(IAppUrls urls) : IPaymentStrategy
{
    public PaymentMethod Method => PaymentMethod.Card;
    public string DisplayName => "Банковская карта";
    public string Description => "Visa / Mastercard / Белкарт (тестовый шлюз)";
    public bool IsEnabled => true;
    public bool SupportsDeposit => true;

    public Task<PaymentInitResult> InitiateAsync(Payment payment, User user, CancellationToken ct) =>
        Task.FromResult(new PaymentInitResult(false, $"card-{payment.Id}-{Guid.NewGuid():N}"[..24],
            urls.Build($"/pay/{payment.Id}")));
}

/// <summary>ERIP (Belarusian settlement system): the customer pays an invoice by its number.</summary>
public sealed class EripPaymentStrategy(IAppUrls urls) : IPaymentStrategy
{
    public PaymentMethod Method => PaymentMethod.Erip;
    public string DisplayName => "ЕРИП";
    public string Description => "Оплата через «Расчёт» (ЕРИП) в интернет-банке";
    public bool IsEnabled => true;
    public bool SupportsDeposit => true;

    public Task<PaymentInitResult> InitiateAsync(Payment payment, User user, CancellationToken ct)
    {
        var invoice = $"{DateTime.UtcNow:yyMMdd}{payment.Id:D6}";
        var instructions =
            $"Интернет-банк → «Платежи» → ЕРИП → Интернет-магазины/сервисы → P → Prismarket. " +
            $"Номер счёта: {invoice}. Сумма: {payment.Amount:0.00} BYN.";
        return Task.FromResult(new PaymentInitResult(false, invoice, urls.Build($"/pay/{payment.Id}"), instructions));
    }
}

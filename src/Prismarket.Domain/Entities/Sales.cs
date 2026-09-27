using Prismarket.Domain.Common;
using Prismarket.Domain.Enums;

namespace Prismarket.Domain.Entities;

public class CartItem : Entity
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int Quantity { get; set; } = 1;
    public DateTime AddedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Set by the abandoned-cart automation after the reminder was sent.</summary>
    public DateTime? ReminderSentAt { get; set; }
}

public class Order : AuditableEntity, ITrackChanges
{
    public string Number { get; set; } = null!;
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public OrderStatus Status { get; set; } = OrderStatus.AwaitingPayment;
    public PaymentMethod PaymentMethod { get; set; }

    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal Total { get; set; }

    public int? PromoCodeId { get; set; }
    public PromoCode? PromoCode { get; set; }

    public DateTime ExpiresAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? CancellationReason { get; set; }
    public uint Version { get; set; }

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();

    public bool IsFinal => Status is OrderStatus.Completed or OrderStatus.Cancelled
        or OrderStatus.Expired or OrderStatus.Refunded;

    public void MarkPaid(DateTime now)
    {
        if (Status != OrderStatus.AwaitingPayment)
            throw new InvalidOperationException($"Order {Number} cannot be paid in status {Status}.");
        Status = OrderStatus.Paid;
        PaidAt = now;
    }

    public void Complete(DateTime now)
    {
        if (Status != OrderStatus.Paid)
            throw new InvalidOperationException($"Order {Number} must be paid before completion.");
        Status = OrderStatus.Completed;
        CompletedAt = now;
    }

    public void Cancel(OrderStatus finalStatus, string reason)
    {
        if (finalStatus is not (OrderStatus.Cancelled or OrderStatus.Expired))
            throw new ArgumentException("Only Cancelled or Expired are allowed.", nameof(finalStatus));
        if (Status != OrderStatus.AwaitingPayment)
            throw new InvalidOperationException($"Order {Number} cannot be cancelled in status {Status}.");
        Status = finalStatus;
        CancellationReason = reason;
    }
}

public class OrderItem : Entity
{
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public decimal UnitPrice { get; set; }
    public int DiscountPercent { get; set; }
    public decimal FinalPrice { get; set; }

    public int? KeyId { get; set; }
    public ProductKey? Key { get; set; }
    public int? AccountId { get; set; }
    public ProductAccount? Account { get; set; }
}

public class Payment : AuditableEntity
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int? OrderId { get; set; }
    public Order? Order { get; set; }

    public PaymentPurpose Purpose { get; set; }
    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public decimal Amount { get; set; }

    /// <summary>Id of the transaction at the external provider (Stripe session id, ERIP invoice, ...).</summary>
    public string? ProviderReference { get; set; }
    public string? RedirectUrl { get; set; }
    public string? Instructions { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? FailureReason { get; set; }
    public uint Version { get; set; }
}

public class LibraryItem : Entity
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int OrderItemId { get; set; }
    public OrderItem OrderItem { get; set; } = null!;
    public int? KeyId { get; set; }
    public ProductKey? Key { get; set; }
    public int? AccountId { get; set; }
    public ProductAccount? Account { get; set; }

    public DateTime PurchasedAt { get; set; }
    public bool IsActivated { get; set; }
    public DateTime? ActivatedAt { get; set; }
}

public class WishlistItem : Entity
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int GameId { get; set; }
    public Game Game { get; set; } = null!;
    public DateTime AddedAt { get; set; }

    /// <summary>Lowest price known at the moment of the last notification (or when added).</summary>
    public decimal? LastKnownPrice { get; set; }
    public DateTime? LastNotifiedAt { get; set; }
}

public class PromoCode : AuditableEntity, ITrackChanges
{
    public string Code { get; set; } = null!;
    public int DiscountPercent { get; set; }
    public int? MaxUses { get; set; }
    public int UsedCount { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Personal promo code (e.g. generated by the win-back automation).</summary>
    public int? UserId { get; set; }
    public string? Source { get; set; }

    public bool CanBeUsedBy(int userId, DateTime now) =>
        IsActive
        && (ExpiresAt is null || ExpiresAt > now)
        && (MaxUses is null || UsedCount < MaxUses)
        && (UserId is null || UserId == userId);
}

public class Promotion : AuditableEntity, ITrackChanges
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public int DiscountPercent { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public PromotionStatus Status { get; set; } = PromotionStatus.Scheduled;
    public string? BannerImageUrl { get; set; }

    public ICollection<PromotionProduct> Products { get; set; } = new List<PromotionProduct>();
}

public class PromotionProduct
{
    public int PromotionId { get; set; }
    public Promotion Promotion { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    /// <summary>Discount the product had before the promotion started, restored when it ends.</summary>
    public int? PreviousDiscountPercent { get; set; }
}

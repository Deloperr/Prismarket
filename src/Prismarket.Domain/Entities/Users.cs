using Prismarket.Domain.Common;
using Prismarket.Domain.Enums;

namespace Prismarket.Domain.Entities;

public class User : AuditableEntity, ITrackChanges
{
    public string Username { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string? AvatarUrl { get; set; }
    public UserRole Role { get; set; } = UserRole.Customer;
    public bool IsActive { get; set; } = true;

    public bool EmailConfirmed { get; set; }
    public string? EmailConfirmationToken { get; set; }
    public string? PasswordResetToken { get; set; }
    public DateTime? PasswordResetExpiresAt { get; set; }

    public bool TwoFactorEnabled { get; set; }
    public string? TwoFactorSecret { get; set; }

    public decimal Balance { get; set; }
    public DateTime? LastLoginAt { get; set; }

    /// <summary>When the win-back automation last sent a promo code to this user.</summary>
    public DateTime? WinBackSentAt { get; set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin) — protects the balance from lost updates.</summary>
    public uint Version { get; set; }

    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<LibraryItem> Library { get; set; } = new List<LibraryItem>();
    public ICollection<WishlistItem> Wishlist { get; set; } = new List<WishlistItem>();
    public ICollection<CartItem> Cart { get; set; } = new List<CartItem>();

    public bool IsAdmin => Role == UserRole.Admin;

    public void Credit(decimal amount)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be positive.");
        Balance += amount;
    }

    public void Debit(decimal amount)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be positive.");
        if (Balance < amount) throw new InvalidOperationException("Insufficient balance.");
        Balance -= amount;
    }
}

public class RefreshToken : Entity
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string TokenHash { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? CreatedByIp { get; set; }

    public bool IsActive(DateTime now) => RevokedAt is null && ExpiresAt > now;
}

public class BalanceTransaction : Entity
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public BalanceTransactionType Type { get; set; }
    public string? Description { get; set; }
    public int? OrderId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Notification : Entity
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public NotificationType Type { get; set; }
    public string Title { get; set; } = null!;
    public string Message { get; set; } = null!;
    public string? Link { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

using Prismarket.Domain.Common;
using Prismarket.Domain.Enums;

namespace Prismarket.Domain.Entities;

public class Review : AuditableEntity
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int GameId { get; set; }
    public Game Game { get; set; } = null!;

    public int Rating { get; set; }
    public string? Title { get; set; }
    public string? Comment { get; set; }
    public bool IsVerifiedPurchase { get; set; }
    public ReviewStatus Status { get; set; } = ReviewStatus.Published;
    public string? ModerationNote { get; set; }
}

public class SiteReview : AuditableEntity
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int Rating { get; set; }
    public string? Comment { get; set; }
}

public class SupportTicket : AuditableEntity
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int? AssignedAdminId { get; set; }
    public User? AssignedAdmin { get; set; }
    public int? OrderId { get; set; }
    public Order? Order { get; set; }

    public string Subject { get; set; } = null!;
    public TicketStatus Status { get; set; } = TicketStatus.Open;
    public TicketPriority Priority { get; set; } = TicketPriority.Normal;
    public TicketCategory Category { get; set; } = TicketCategory.General;
    public DateTime LastMessageAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    public ICollection<SupportMessage> Messages { get; set; } = new List<SupportMessage>();
}

public class SupportMessage : Entity
{
    public int TicketId { get; set; }
    public SupportTicket Ticket { get; set; } = null!;
    public int? SenderId { get; set; }
    public User? Sender { get; set; }

    /// <summary>Message written by the automatic support assistant.</summary>
    public bool IsBot { get; set; }
    public bool IsFromStaff { get; set; }
    public string Text { get; set; } = null!;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

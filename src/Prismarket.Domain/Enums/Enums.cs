namespace Prismarket.Domain.Enums;

public enum UserRole
{
    Customer = 0,
    Admin = 1
}

public enum ProductKind
{
    Key = 0,
    Account = 1,
    Gift = 2
}

public enum StockItemStatus
{
    Available = 0,
    Reserved = 1,
    Sold = 2
}

public enum OrderStatus
{
    AwaitingPayment = 0,
    Paid = 1,
    Completed = 2,
    Cancelled = 3,
    Expired = 4,
    Refunded = 5
}

public enum PaymentMethod
{
    Balance = 0,
    Card = 1,
    Erip = 2,
    Stripe = 3
}

public enum PaymentPurpose
{
    Order = 0,
    Deposit = 1
}

public enum PaymentStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
    Cancelled = 3
}

public enum BalanceTransactionType
{
    Deposit = 0,
    Purchase = 1,
    Refund = 2,
    Cashback = 3,
    Bonus = 4,
    AdminAdjustment = 5
}

public enum ReviewStatus
{
    Published = 0,
    PendingModeration = 1,
    Rejected = 2
}

public enum TicketStatus
{
    Open = 0,
    WaitingForCustomer = 1,
    WaitingForSupport = 2,
    Closed = 3
}

public enum TicketPriority
{
    Low = 0,
    Normal = 1,
    High = 2
}

public enum TicketCategory
{
    General = 0,
    Payment = 1,
    KeyActivation = 2,
    Refund = 3,
    Account = 4
}

public enum PromotionStatus
{
    Scheduled = 0,
    Active = 1,
    Finished = 2,
    Cancelled = 3
}

public enum NotificationType
{
    System = 0,
    OrderCompleted = 1,
    PriceDrop = 2,
    BackInStock = 3,
    SupportReply = 4,
    Promo = 5,
    LowStock = 6,
    Cashback = 7
}

public enum EmailStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2
}

public enum AutomationRunStatus
{
    Running = 0,
    Succeeded = 1,
    Failed = 2,
    Skipped = 3
}

public enum AutomationTrigger
{
    Schedule = 0,
    Manual = 1,
    Event = 2
}

public enum AutomationKind
{
    /// <summary>Runs by CRON schedule (Hangfire recurring job).</summary>
    Scheduled = 0,
    /// <summary>Reacts to a domain event (order paid, ticket created, review posted...).</summary>
    EventDriven = 1
}

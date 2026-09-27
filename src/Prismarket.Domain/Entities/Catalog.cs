using Prismarket.Domain.Common;
using Prismarket.Domain.Enums;

namespace Prismarket.Domain.Entities;

public class Platform : AuditableEntity
{
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? Website { get; set; }
    public string? LogoUrl { get; set; }

    public ICollection<Game> Games { get; set; } = new List<Game>();
}

public class Developer : AuditableEntity
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string? Website { get; set; }

    public ICollection<Game> Games { get; set; } = new List<Game>();
}

public class Publisher : AuditableEntity
{
    public string Name { get; set; } = null!;
    public string? Website { get; set; }

    public ICollection<Game> Games { get; set; } = new List<Game>();
}

public class Genre : AuditableEntity
{
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? Description { get; set; }

    public ICollection<Game> Games { get; set; } = new List<Game>();
}

public class Game : AuditableEntity, ITrackChanges
{
    public string Title { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? ShortDescription { get; set; }
    public string? Description { get; set; }
    public DateOnly? ReleaseDate { get; set; }
    public string? AgeRating { get; set; }
    public string? SystemRequirements { get; set; }
    public string? CoverImageUrl { get; set; }
    public string? HeaderImageUrl { get; set; }
    public string? TrailerUrl { get; set; }
    public bool IsAvailable { get; set; } = true;

    /// <summary>Set automatically by the "featured rotation" automation (top sellers).</summary>
    public bool IsFeatured { get; set; }

    /// <summary>Denormalized rating, recalculated automatically when reviews change.</summary>
    public decimal AverageRating { get; set; }
    public int RatingsCount { get; set; }
    public int SalesCount { get; set; }

    public int DeveloperId { get; set; }
    public Developer Developer { get; set; } = null!;
    public int PublisherId { get; set; }
    public Publisher Publisher { get; set; } = null!;
    public int PlatformId { get; set; }
    public Platform Platform { get; set; } = null!;

    public ICollection<Genre> Genres { get; set; } = new List<Genre>();
    public ICollection<Product> Products { get; set; } = new List<Product>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
    public ICollection<WishlistItem> WishlistItems { get; set; } = new List<WishlistItem>();

    /// <summary>Lowest current price across available editions (null if nothing is on sale).</summary>
    public decimal? MinPrice() => Products
        .Where(p => p.IsAvailable)
        .Select(p => (decimal?)p.FinalPrice)
        .Min();
}

public class Product : AuditableEntity, ITrackChanges
{
    public int GameId { get; set; }
    public Game Game { get; set; } = null!;

    public ProductKind Kind { get; set; }
    public string Edition { get; set; } = "Standard";

    /// <summary>Price in the base currency (BYN).</summary>
    public decimal Price { get; set; }
    public int DiscountPercent { get; set; }

    public bool IsAvailable { get; set; } = true;

    /// <summary>True when the stock monitor hid the product because it ran out of stock.</summary>
    public bool HiddenByStockMonitor { get; set; }

    /// <summary>Per-product low stock threshold; falls back to automation settings when null.</summary>
    public int? LowStockThreshold { get; set; }
    public DateTime? LowStockNotifiedAt { get; set; }

    public ICollection<ProductKey> Keys { get; set; } = new List<ProductKey>();
    public ICollection<ProductAccount> Accounts { get; set; } = new List<ProductAccount>();

    public decimal FinalPrice => ApplyDiscount(Price, DiscountPercent);

    public static decimal ApplyDiscount(decimal price, int discountPercent)
    {
        if (discountPercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(discountPercent));
        return Math.Round(price * (100 - discountPercent) / 100m, 2, MidpointRounding.AwayFromZero);
    }
}

public class ProductKey : AuditableEntity
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string Value { get; set; } = null!;
    public StockItemStatus Status { get; set; } = StockItemStatus.Available;
    public int? ReservedByOrderId { get; set; }
    public DateTime? SoldAt { get; set; }

    public void Reserve(int orderId)
    {
        if (Status != StockItemStatus.Available)
            throw new InvalidOperationException($"Key {Id} is not available.");
        Status = StockItemStatus.Reserved;
        ReservedByOrderId = orderId;
    }

    public void Release()
    {
        if (Status == StockItemStatus.Reserved)
        {
            Status = StockItemStatus.Available;
            ReservedByOrderId = null;
        }
    }

    public void MarkSold(DateTime now)
    {
        Status = StockItemStatus.Sold;
        SoldAt = now;
    }
}

public class ProductAccount : AuditableEntity
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string Login { get; set; } = null!;
    public string Password { get; set; } = null!;
    public string? Email { get; set; }
    public string? AdditionalInfo { get; set; }
    public StockItemStatus Status { get; set; } = StockItemStatus.Available;
    public int? ReservedByOrderId { get; set; }
    public DateTime? SoldAt { get; set; }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prismarket.Domain.Entities;

namespace Prismarket.Infrastructure.Persistence;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.Property(x => x.Username).HasMaxLength(32);
        b.Property(x => x.Email).HasMaxLength(100);
        b.Property(x => x.PasswordHash).HasMaxLength(100);
        b.Property(x => x.TwoFactorSecret).HasMaxLength(64);
        b.Property(x => x.EmailConfirmationToken).HasMaxLength(128);
        b.Property(x => x.PasswordResetToken).HasMaxLength(128);
        b.Property(x => x.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");
        b.HasIndex(x => x.Email).IsUnique();
        b.HasIndex(x => x.Username).IsUnique();
        b.HasIndex(x => x.EmailConfirmationToken);
        b.HasIndex(x => x.PasswordResetToken);
        b.ToTable(t => t.HasCheckConstraint("ck_users_balance_non_negative", "balance >= 0"));
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.Property(x => x.TokenHash).HasMaxLength(128);
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class GameConfiguration : IEntityTypeConfiguration<Game>
{
    public void Configure(EntityTypeBuilder<Game> b)
    {
        b.Property(x => x.Title).HasMaxLength(200);
        b.Property(x => x.Slug).HasMaxLength(220);
        b.Property(x => x.Description).HasColumnType("text");
        b.Property(x => x.SystemRequirements).HasColumnType("text");
        b.Property(x => x.AverageRating).HasPrecision(3, 2);
        b.HasIndex(x => x.Slug).IsUnique();
        b.HasIndex(x => x.IsFeatured);
        b.HasIndex(x => x.Title);

        b.HasOne(x => x.Developer).WithMany(x => x.Games).HasForeignKey(x => x.DeveloperId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Publisher).WithMany(x => x.Games).HasForeignKey(x => x.PublisherId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Platform).WithMany(x => x.Games).HasForeignKey(x => x.PlatformId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Genres).WithMany(x => x.Games).UsingEntity(j => j.ToTable("game_genres"));
    }
}

internal sealed class LookupConfiguration :
    IEntityTypeConfiguration<Platform>, IEntityTypeConfiguration<Genre>,
    IEntityTypeConfiguration<Developer>, IEntityTypeConfiguration<Publisher>
{
    public void Configure(EntityTypeBuilder<Platform> b)
    {
        b.Property(x => x.Name).HasMaxLength(60);
        b.HasIndex(x => x.Name).IsUnique();
        b.HasIndex(x => x.Slug).IsUnique();
    }

    public void Configure(EntityTypeBuilder<Genre> b)
    {
        b.Property(x => x.Name).HasMaxLength(60);
        b.HasIndex(x => x.Name).IsUnique();
        b.HasIndex(x => x.Slug).IsUnique();
    }

    public void Configure(EntityTypeBuilder<Developer> b)
    {
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.Description).HasColumnType("text");
        b.HasIndex(x => x.Name).IsUnique();
    }

    public void Configure(EntityTypeBuilder<Publisher> b)
    {
        b.Property(x => x.Name).HasMaxLength(100);
        b.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.Property(x => x.Edition).HasMaxLength(60);
        b.Ignore(x => x.FinalPrice);
        b.HasOne(x => x.Game).WithMany(x => x.Products).HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.Cascade);
        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_products_price", "price >= 0");
            t.HasCheckConstraint("ck_products_discount", "discount_percent BETWEEN 0 AND 100");
        });
    }
}

internal sealed class ProductKeyConfiguration : IEntityTypeConfiguration<ProductKey>
{
    public void Configure(EntityTypeBuilder<ProductKey> b)
    {
        b.Property(x => x.Value).HasMaxLength(100);
        b.HasIndex(x => x.Value).IsUnique();
        b.HasIndex(x => new { x.ProductId, x.Status });
        b.HasIndex(x => x.ReservedByOrderId);
        b.HasOne(x => x.Product).WithMany(x => x.Keys).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ProductAccountConfiguration : IEntityTypeConfiguration<ProductAccount>
{
    public void Configure(EntityTypeBuilder<ProductAccount> b)
    {
        b.Property(x => x.Login).HasMaxLength(100);
        b.Property(x => x.Password).HasMaxLength(100);
        b.Property(x => x.AdditionalInfo).HasColumnType("text");
        b.HasIndex(x => new { x.ProductId, x.Status });
        b.HasIndex(x => x.ReservedByOrderId);
        b.HasOne(x => x.Product).WithMany(x => x.Accounts).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> b)
    {
        b.HasIndex(x => new { x.UserId, x.ProductId }).IsUnique();
        b.HasOne(x => x.User).WithMany(x => x.Cart).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        b.ToTable(t => t.HasCheckConstraint("ck_cart_items_quantity", "quantity BETWEEN 1 AND 10"));
    }
}

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.Property(x => x.Number).HasMaxLength(20);
        b.Property(x => x.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");
        b.HasIndex(x => x.Number).IsUnique();
        b.HasIndex(x => new { x.Status, x.ExpiresAt });
        b.HasIndex(x => x.CreatedAt);
        b.HasOne(x => x.User).WithMany(x => x.Orders).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.PromoCode).WithMany().HasForeignKey(x => x.PromoCodeId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> b)
    {
        b.HasOne(x => x.Order).WithMany(x => x.Items).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Key).WithMany().HasForeignKey(x => x.KeyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.KeyId);
        b.HasIndex(x => x.AccountId);
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.Property(x => x.ProviderReference).HasMaxLength(200);
        b.Property(x => x.Instructions).HasColumnType("text");
        b.Property(x => x.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");
        b.HasIndex(x => new { x.Method, x.ProviderReference });
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Order).WithMany(x => x.Payments).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class LibraryItemConfiguration : IEntityTypeConfiguration<LibraryItem>
{
    public void Configure(EntityTypeBuilder<LibraryItem> b)
    {
        b.HasIndex(x => x.UserId);
        b.HasOne(x => x.User).WithMany(x => x.Library).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.OrderItem).WithMany().HasForeignKey(x => x.OrderItemId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Key).WithMany().HasForeignKey(x => x.KeyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class WishlistItemConfiguration : IEntityTypeConfiguration<WishlistItem>
{
    public void Configure(EntityTypeBuilder<WishlistItem> b)
    {
        b.HasIndex(x => new { x.UserId, x.GameId }).IsUnique();
        b.HasOne(x => x.User).WithMany(x => x.Wishlist).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Game).WithMany(x => x.WishlistItems).HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class BalanceTransactionConfiguration : IEntityTypeConfiguration<BalanceTransaction>
{
    public void Configure(EntityTypeBuilder<BalanceTransaction> b)
    {
        b.HasIndex(x => new { x.UserId, x.CreatedAt });
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PromoConfiguration : IEntityTypeConfiguration<PromoCode>, IEntityTypeConfiguration<Promotion>,
    IEntityTypeConfiguration<PromotionProduct>
{
    public void Configure(EntityTypeBuilder<PromoCode> b)
    {
        b.Property(x => x.Code).HasMaxLength(40);
        b.Property(x => x.Source).HasMaxLength(40);
        b.HasIndex(x => x.Code).IsUnique();
    }

    public void Configure(EntityTypeBuilder<Promotion> b)
    {
        b.Property(x => x.Name).HasMaxLength(120);
        b.HasIndex(x => new { x.Status, x.StartsAt, x.EndsAt });
    }

    public void Configure(EntityTypeBuilder<PromotionProduct> b)
    {
        b.HasKey(x => new { x.PromotionId, x.ProductId });
        b.HasOne(x => x.Promotion).WithMany(x => x.Products).HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>, IEntityTypeConfiguration<SiteReview>
{
    public void Configure(EntityTypeBuilder<Review> b)
    {
        b.Property(x => x.Title).HasMaxLength(120);
        b.Property(x => x.Comment).HasColumnType("text");
        b.HasIndex(x => new { x.GameId, x.UserId }).IsUnique();
        b.HasIndex(x => x.Status);
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Game).WithMany(x => x.Reviews).HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.Cascade);
        b.ToTable(t => t.HasCheckConstraint("ck_reviews_rating", "rating BETWEEN 1 AND 5"));
    }

    public void Configure(EntityTypeBuilder<SiteReview> b)
    {
        b.Property(x => x.Comment).HasColumnType("text");
        b.HasIndex(x => x.UserId).IsUnique();
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.ToTable(t => t.HasCheckConstraint("ck_site_reviews_rating", "rating BETWEEN 1 AND 5"));
    }
}

internal sealed class SupportConfiguration : IEntityTypeConfiguration<SupportTicket>, IEntityTypeConfiguration<SupportMessage>
{
    public void Configure(EntityTypeBuilder<SupportTicket> b)
    {
        b.Property(x => x.Subject).HasMaxLength(200);
        b.HasIndex(x => new { x.Status, x.LastMessageAt });
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.AssignedAdmin).WithMany().HasForeignKey(x => x.AssignedAdminId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.SetNull);
    }

    public void Configure(EntityTypeBuilder<SupportMessage> b)
    {
        b.Property(x => x.Text).HasColumnType("text");
        b.HasIndex(x => x.TicketId);
        b.HasOne(x => x.Ticket).WithMany(x => x.Messages).HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Sender).WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class SystemConfiguration :
    IEntityTypeConfiguration<Notification>, IEntityTypeConfiguration<AuditLog>,
    IEntityTypeConfiguration<CurrencyRate>, IEntityTypeConfiguration<EmailMessage>,
    IEntityTypeConfiguration<AutomationJob>, IEntityTypeConfiguration<AutomationRun>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.Property(x => x.Message).HasColumnType("text");
        b.HasIndex(x => new { x.UserId, x.IsRead, x.CreatedAt });
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.Property(x => x.Changes).HasColumnType("jsonb");
        b.Property(x => x.Action).HasMaxLength(20);
        b.Property(x => x.EntityName).HasMaxLength(60);
        b.HasIndex(x => x.CreatedAt);
        b.HasIndex(x => new { x.EntityName, x.EntityId });
    }

    public void Configure(EntityTypeBuilder<CurrencyRate> b)
    {
        b.HasKey(x => x.Code);
        b.Property(x => x.Code).HasMaxLength(3);
        b.Property(x => x.RateToByn).HasPrecision(12, 6);
    }

    public void Configure(EntityTypeBuilder<EmailMessage> b)
    {
        b.Property(x => x.HtmlBody).HasColumnType("text");
        b.Property(x => x.Subject).HasMaxLength(250);
        b.Property(x => x.LastError).HasColumnType("text");
        b.HasIndex(x => new { x.Status, x.NextAttemptAt });
    }

    public void Configure(EntityTypeBuilder<AutomationJob> b)
    {
        b.HasKey(x => x.Key);
        b.Property(x => x.Key).HasMaxLength(60);
        b.Property(x => x.Description).HasColumnType("text");
        b.Property(x => x.SettingsJson).HasColumnType("jsonb");
        b.Property(x => x.Cron).HasMaxLength(60);
    }

    public void Configure(EntityTypeBuilder<AutomationRun> b)
    {
        b.Property(x => x.JobKey).HasMaxLength(60);
        b.Property(x => x.Message).HasColumnType("text");
        b.Ignore(x => x.DurationMs);
        b.HasIndex(x => new { x.JobKey, x.StartedAt });
        b.HasIndex(x => x.StartedAt);
    }
}

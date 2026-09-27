using Prismarket.Domain.Entities;
using Prismarket.Domain.Enums;

namespace Prismarket.UnitTests;

public class DomainTests
{
    [Theory]
    [InlineData(100, 0, 100)]
    [InlineData(100, 25, 75)]
    [InlineData(59.90, 35, 38.94)]
    [InlineData(19.99, 100, 0)]
    public void ApplyDiscount_RoundsToCents(decimal price, int discount, decimal expected) =>
        Assert.Equal(expected, Product.ApplyDiscount(price, discount));

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void ApplyDiscount_RejectsInvalidPercent(int discount) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Product.ApplyDiscount(10, discount));

    [Fact]
    public void Debit_MoreThanBalance_Throws()
    {
        var user = new User { Balance = 10 };
        Assert.Throws<InvalidOperationException>(() => user.Debit(10.01m));
        Assert.Equal(10, user.Balance);
    }

    [Fact]
    public void CreditAndDebit_ChangeBalance()
    {
        var user = new User();
        user.Credit(50);
        user.Debit(20.5m);
        Assert.Equal(29.5m, user.Balance);
    }

    [Fact]
    public void Order_FollowsLifecycle()
    {
        var now = DateTime.UtcNow;
        var order = new Order { Number = "PM-1" };

        order.MarkPaid(now);
        order.Complete(now);

        Assert.Equal(OrderStatus.Completed, order.Status);
        Assert.True(order.IsFinal);
        Assert.Throws<InvalidOperationException>(() => order.MarkPaid(now));
    }

    [Fact]
    public void Order_CannotCompleteWithoutPayment() =>
        Assert.Throws<InvalidOperationException>(() => new Order { Number = "PM-2" }.Complete(DateTime.UtcNow));

    [Fact]
    public void Order_CancelOnlyToFinalStatuses()
    {
        var order = new Order { Number = "PM-3" };
        Assert.Throws<ArgumentException>(() => order.Cancel(OrderStatus.Completed, "x"));
        order.Cancel(OrderStatus.Expired, "timeout");
        Assert.Equal(OrderStatus.Expired, order.Status);
    }

    [Fact]
    public void Key_ReserveReleaseSell()
    {
        var key = new ProductKey { Id = 1, Value = "AAAAA-BBBBB-CCCCC" };
        key.Reserve(orderId: 7);
        Assert.Equal(StockItemStatus.Reserved, key.Status);
        Assert.Throws<InvalidOperationException>(() => key.Reserve(8));

        key.Release();
        Assert.Equal(StockItemStatus.Available, key.Status);
        Assert.Null(key.ReservedByOrderId);

        key.MarkSold(DateTime.UtcNow);
        Assert.Equal(StockItemStatus.Sold, key.Status);
    }

    [Fact]
    public void PromoCode_Rules()
    {
        var now = DateTime.UtcNow;
        Assert.True(new PromoCode { IsActive = true }.CanBeUsedBy(1, now));
        Assert.False(new PromoCode { IsActive = false }.CanBeUsedBy(1, now));
        Assert.False(new PromoCode { IsActive = true, ExpiresAt = now.AddMinutes(-1) }.CanBeUsedBy(1, now));
        Assert.False(new PromoCode { IsActive = true, MaxUses = 3, UsedCount = 3 }.CanBeUsedBy(1, now));
        Assert.False(new PromoCode { IsActive = true, UserId = 2 }.CanBeUsedBy(1, now));
        Assert.True(new PromoCode { IsActive = true, UserId = 2 }.CanBeUsedBy(2, now));
    }

    [Fact]
    public void Game_MinPrice_IgnoresUnavailableEditions()
    {
        var game = new Game
        {
            Products =
            {
                new Product { Price = 100, DiscountPercent = 50, IsAvailable = false },
                new Product { Price = 80, DiscountPercent = 10, IsAvailable = true },
                new Product { Price = 120, DiscountPercent = 0, IsAvailable = true }
            }
        };
        Assert.Equal(72m, game.MinPrice());
    }
}

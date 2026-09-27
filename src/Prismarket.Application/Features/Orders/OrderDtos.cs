using Prismarket.Application.Features.Payments;
using Prismarket.Domain.Entities;
using Prismarket.Domain.Enums;

namespace Prismarket.Application.Features.Orders;

public sealed record CheckoutRequest(PaymentMethod PaymentMethod, string? PromoCode);

public sealed record PromoPreviewRequest(string Code);

public sealed record PromoPreviewDto(string Code, int DiscountPercent, decimal Subtotal, decimal Discount, decimal Total);

public sealed record OrderItemDto(
    int Id, int ProductId, int GameId, string GameSlug, string GameTitle, string? ImageUrl, ProductKind Kind,
    string Edition, decimal UnitPrice, int DiscountPercent, decimal FinalPrice,
    string? KeyValue, string? AccountLogin, string? AccountPassword);

public sealed record OrderDto(
    int Id, string Number, OrderStatus Status, PaymentMethod PaymentMethod,
    decimal Subtotal, decimal DiscountAmount, decimal Total, string? PromoCode,
    DateTime CreatedAt, DateTime ExpiresAt, DateTime? PaidAt, DateTime? CompletedAt, string? CancellationReason,
    IReadOnlyList<OrderItemDto> Items, PaymentDto? Payment, string? CustomerName = null);

public sealed record CheckoutResultDto(OrderDto Order, PaymentDto Payment);

internal static class OrderMapping
{
    public static OrderDto ToDto(Order o, bool revealSecrets, string? customer = null)
    {
        var reveal = revealSecrets && o.Status == OrderStatus.Completed;
        var payment = o.Payments.OrderByDescending(p => p.Id).FirstOrDefault();
        return new OrderDto(o.Id, o.Number, o.Status, o.PaymentMethod, o.Subtotal, o.DiscountAmount, o.Total,
            o.PromoCode?.Code, o.CreatedAt, o.ExpiresAt, o.PaidAt, o.CompletedAt, o.CancellationReason,
            o.Items.OrderBy(i => i.Id).Select(i => new OrderItemDto(i.Id, i.ProductId, i.Product.GameId,
                i.Product.Game.Slug, i.Product.Game.Title,
                i.Product.Game.HeaderImageUrl ?? i.Product.Game.CoverImageUrl,
                i.Product.Kind, i.Product.Edition, i.UnitPrice, i.DiscountPercent, i.FinalPrice,
                reveal ? i.Key?.Value : null,
                reveal ? i.Account?.Login : null,
                reveal ? i.Account?.Password : null)).ToList(),
            payment is null ? null : PaymentDto.From(payment),
            customer);
    }
}

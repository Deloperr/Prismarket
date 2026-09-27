using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Prismarket.Application.Common;
using Prismarket.Application.Features.Payments;
using Prismarket.Domain.Entities;
using Prismarket.Domain.Enums;

namespace Prismarket.Infrastructure.Payments;

/// <summary>
/// Stripe Checkout via the REST API (no SDK). Enabled only when Stripe:SecretKey is configured.
/// Completion arrives through the webhook <c>checkout.session.completed</c>.
/// </summary>
public sealed class StripePaymentStrategy(HttpClient http, IOptions<StripeOptions> options, IAppUrls urls)
    : IPaymentStrategy
{
    public PaymentMethod Method => PaymentMethod.Stripe;
    public string DisplayName => "Stripe";
    public string Description => "Международные карты через Stripe Checkout";
    public bool IsEnabled => options.Value.Enabled;
    public bool SupportsDeposit => true;

    public async Task<PaymentInitResult> InitiateAsync(Payment payment, User user, CancellationToken ct)
    {
        var amountMinor = ((long)Math.Round(payment.Amount * 100m)).ToString(CultureInfo.InvariantCulture);
        var form = new Dictionary<string, string>
        {
            ["mode"] = "payment",
            ["success_url"] = urls.Build($"/pay/{payment.Id}?status=success"),
            ["cancel_url"] = urls.Build($"/pay/{payment.Id}?status=cancel"),
            ["customer_email"] = user.Email,
            ["client_reference_id"] = payment.Id.ToString(CultureInfo.InvariantCulture),
            ["line_items[0][quantity]"] = "1",
            ["line_items[0][price_data][currency]"] = "byn",
            ["line_items[0][price_data][unit_amount]"] = amountMinor,
            ["line_items[0][price_data][product_data][name]"] = payment.Purpose == PaymentPurpose.Deposit
                ? "Пополнение баланса Prismarket"
                : $"Заказ Prismarket #{payment.OrderId}"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/checkout/sessions")
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.SecretKey);

        using var response = await http.SendAsync(request, ct);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (!response.IsSuccessStatusCode)
            throw new BusinessRuleException("Stripe отклонил создание платежа: " +
                                            (json.TryGetProperty("error", out var e) ? e.GetProperty("message").GetString() : response.ReasonPhrase));

        return new PaymentInitResult(false, json.GetProperty("id").GetString(), json.GetProperty("url").GetString());
    }

    /// <summary>Verifies the <c>Stripe-Signature</c> header (HMAC-SHA256 of "{timestamp}.{payload}").</summary>
    public static bool VerifySignature(string payload, string header, string secret, DateTimeOffset now,
        TimeSpan tolerance)
    {
        var parts = header.Split(',').Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToList();
        var timestamp = parts.FirstOrDefault(p => p[0] == "t")?[1];
        var signatures = parts.Where(p => p[0] == "v1").Select(p => p[1]).ToList();
        if (timestamp is null || signatures.Count == 0) return false;
        if (!long.TryParse(timestamp, out var unix)) return false;
        if ((now - DateTimeOffset.FromUnixTimeSeconds(unix)).Duration() > tolerance) return false;

        var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes($"{timestamp}.{payload}"))).ToLowerInvariant();
        return signatures.Any(s => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(s), Encoding.UTF8.GetBytes(expected)));
    }
}

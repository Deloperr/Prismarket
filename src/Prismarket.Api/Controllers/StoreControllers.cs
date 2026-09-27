using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Prismarket.Application.Common;
using Prismarket.Application.Features.Auth;
using Prismarket.Application.Features.Cart;
using Prismarket.Application.Features.Catalog;
using Prismarket.Application.Features.Currency;
using Prismarket.Application.Features.Orders;
using Prismarket.Application.Features.Payments;

namespace Prismarket.Api.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public sealed class AuthController(IAuthService auth, IWebHostEnvironment env) : ControllerBase
{
    private const string RefreshCookie = "pm_refresh";

    public sealed record TokenResponse(bool RequiresTwoFactor, string? AccessToken, DateTime? ExpiresAt, UserDto? User);

    [HttpPost("register")]
    public async Task<TokenResponse> Register(RegisterRequest request, CancellationToken ct) =>
        Issue(await auth.RegisterAsync(request, ct));

    [HttpPost("login")]
    public async Task<TokenResponse> Login(LoginRequest request, CancellationToken ct) =>
        Issue(await auth.LoginAsync(request, ct));

    /// <summary>Exchanges the http-only refresh cookie for a new access token (token rotation).</summary>
    [HttpPost("refresh")]
    [DisableRateLimiting]
    public async Task<TokenResponse> Refresh(CancellationToken ct)
    {
        var token = Request.Cookies[RefreshCookie] ?? throw new UnauthorizedException("Сессия отсутствует.");
        return Issue(await auth.RefreshAsync(token, ct));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await auth.LogoutAsync(Request.Cookies[RefreshCookie], ct);
        Response.Cookies.Delete(RefreshCookie, CookieOptions(DateTime.UnixEpoch));
        return NoContent();
    }

    [HttpPost("confirm-email")]
    public async Task<IActionResult> ConfirmEmail([FromQuery] string token, CancellationToken ct)
    {
        await auth.ConfirmEmailAsync(token, ct);
        return NoContent();
    }

    [Authorize, HttpPost("resend-confirmation")]
    public async Task<IActionResult> ResendConfirmation(CancellationToken ct)
    {
        await auth.ResendConfirmationAsync(ct);
        return NoContent();
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        await auth.RequestPasswordResetAsync(request, ct);
        return NoContent();
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        await auth.ResetPasswordAsync(request, ct);
        return NoContent();
    }

    private TokenResponse Issue(AuthResult result)
    {
        if (result.RefreshToken is not null)
            Response.Cookies.Append(RefreshCookie, result.RefreshToken, CookieOptions(result.RefreshTokenExpiresAt!.Value));
        return new TokenResponse(result.RequiresTwoFactor, result.AccessToken, result.AccessTokenExpiresAt, result.User);
    }

    private CookieOptions CookieOptions(DateTime expires) => new()
    {
        HttpOnly = true,
        Secure = !env.IsDevelopment(),
        SameSite = SameSiteMode.Strict,
        Path = "/api/auth",
        Expires = expires
    };
}

[ApiController]
[Route("api/catalog")]
public sealed class CatalogController(ICatalogService catalog) : ControllerBase
{
    [HttpGet("home")]
    public Task<HomeDto> Home(CancellationToken ct) => catalog.GetHomeAsync(ct);

    [HttpGet("games")]
    public Task<PagedResult<GameCardDto>> Games([FromQuery] GameQuery query, CancellationToken ct) =>
        catalog.SearchAsync(query, ct);

    [HttpGet("games/{idOrSlug}")]
    public Task<GameDetailDto> Game(string idOrSlug, CancellationToken ct) => catalog.GetGameAsync(idOrSlug, ct);

    [HttpGet("recommendations")]
    public Task<IReadOnlyList<GameCardDto>> Recommendations([FromQuery] int take = 8, CancellationToken ct = default) =>
        catalog.GetRecommendationsAsync(take, ct);

    [HttpGet("genres")]
    public Task<IReadOnlyList<LookupDto>> Genres(CancellationToken ct) => catalog.GetGenresAsync(ct);

    [HttpGet("platforms")]
    public Task<IReadOnlyList<LookupDto>> Platforms(CancellationToken ct) => catalog.GetPlatformsAsync(ct);

    [HttpGet("developers")]
    public Task<IReadOnlyList<LookupDto>> Developers(CancellationToken ct) => catalog.GetDevelopersAsync(ct);

    [HttpGet("publishers")]
    public Task<IReadOnlyList<LookupDto>> Publishers(CancellationToken ct) => catalog.GetPublishersAsync(ct);
}

[ApiController]
[Authorize]
[Route("api/cart")]
public sealed class CartController(ICartService cart, IOrderService orders) : ControllerBase
{
    public sealed record QuantityRequest(int Quantity);

    [HttpGet]
    public Task<CartDto> Get(CancellationToken ct) => cart.GetAsync(ct);

    [HttpPost("items")]
    public Task<CartDto> Add(CartItemRequest request, CancellationToken ct) => cart.AddAsync(request, ct);

    [HttpPut("items/{productId:int}")]
    public Task<CartDto> SetQuantity(int productId, QuantityRequest request, CancellationToken ct) =>
        cart.SetQuantityAsync(productId, request.Quantity, ct);

    [HttpDelete("items/{productId:int}")]
    public Task<CartDto> Remove(int productId, CancellationToken ct) => cart.RemoveAsync(productId, ct);

    [HttpDelete]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        await cart.ClearAsync(ct);
        return NoContent();
    }

    /// <summary>Merges the guest cart (stored in the browser) after login.</summary>
    [HttpPost("merge")]
    public Task<CartDto> Merge(IReadOnlyList<CartItemRequest> items, CancellationToken ct) => cart.MergeAsync(items, ct);

    [HttpPost("promo")]
    public Task<PromoPreviewDto> PreviewPromo(PromoPreviewRequest request, CancellationToken ct) =>
        orders.PreviewPromoAsync(request.Code, ct);
}

[ApiController]
[Authorize]
[Route("api/orders")]
public sealed class OrdersController(IOrderService orders) : ControllerBase
{
    public sealed record PayRequest(Domain.Enums.PaymentMethod Method);

    [HttpPost("checkout")]
    public Task<CheckoutResultDto> Checkout(CheckoutRequest request, CancellationToken ct) =>
        orders.CheckoutAsync(request, ct);

    [HttpGet]
    public Task<PagedResult<OrderDto>> List([FromQuery] PageRequest page, CancellationToken ct) =>
        orders.GetMyOrdersAsync(page, ct);

    [HttpGet("{number}")]
    public Task<OrderDto> Get(string number, CancellationToken ct) => orders.GetMyOrderAsync(number, ct);

    [HttpPost("{number}/cancel")]
    public Task<OrderDto> Cancel(string number, CancellationToken ct) => orders.CancelMyOrderAsync(number, ct);

    [HttpPost("{number}/pay")]
    public Task<PaymentDto> Pay(string number, PayRequest request, CancellationToken ct) =>
        orders.RetryPaymentAsync(number, request.Method, ct);
}

[ApiController]
[Route("api/payments")]
public sealed class PaymentsController(IPaymentService payments) : ControllerBase
{
    public sealed record TestConfirmRequest(bool Success);

    [HttpGet("methods")]
    public IReadOnlyList<PaymentMethodDto> Methods() => payments.GetMethods();

    [Authorize, HttpPost("deposit")]
    public Task<PaymentDto> Deposit(DepositRequest request, CancellationToken ct) => payments.CreateDepositAsync(request, ct);

    [Authorize, HttpGet("{id:int}")]
    public Task<PaymentDto> Get(int id, CancellationToken ct) => payments.GetAsync(id, ct);

    /// <summary>Completes a payment on the built-in test gateway page (card / ERIP emulation).</summary>
    [Authorize, HttpPost("{id:int}/test-confirm")]
    public Task<PaymentDto> TestConfirm(int id, TestConfirmRequest request, CancellationToken ct) =>
        payments.ConfirmTestPaymentAsync(id, request.Success, ct);
}

[ApiController]
[Route("api/currency")]
public sealed class CurrencyController(ICurrencyService currency) : ControllerBase
{
    [HttpGet("rates")]
    [ResponseCache(Duration = 300)]
    public Task<IReadOnlyList<CurrencyRateDto>> Rates(CancellationToken ct) => currency.GetRatesAsync(ct);
}

using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Prismarket.Application.Common;
using Prismarket.Application.Features.Auth;
using Prismarket.Application.Features.Notifications;
using Prismarket.Application.Features.Payments;
using Prismarket.Application.Features.Profile;
using Prismarket.Application.Features.Reviews;
using Prismarket.Application.Features.Support;
using Prismarket.Application.Features.Wishlist;
using Prismarket.Domain.Enums;
using Prismarket.Infrastructure;
using Prismarket.Infrastructure.Payments;

namespace Prismarket.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/profile")]
public sealed class ProfileController(IProfileService profile) : ControllerBase
{
    public sealed record ActivateRequest(bool Activated);

    [HttpGet]
    public Task<UserDto> Me(CancellationToken ct) => profile.GetMeAsync(ct);

    [HttpGet("stats")]
    public Task<ProfileStatsDto> Stats(CancellationToken ct) => profile.GetStatsAsync(ct);

    [HttpPut]
    public Task<UserDto> Update(UpdateProfileRequest request, CancellationToken ct) => profile.UpdateAsync(request, ct);

    [HttpPost("avatar")]
    [RequestSizeLimit(3 * 1024 * 1024)]
    public async Task<UserDto> Avatar(IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        return await profile.UploadAvatarAsync(stream, file.FileName, file.Length, ct);
    }

    [HttpPost("2fa/setup")]
    public Task<TwoFactorSetupDto> SetupTwoFactor(CancellationToken ct) => profile.SetupTwoFactorAsync(ct);

    [HttpPost("2fa/enable")]
    public async Task<IActionResult> EnableTwoFactor(TwoFactorEnableRequest request, CancellationToken ct)
    {
        await profile.EnableTwoFactorAsync(request, ct);
        return NoContent();
    }

    [HttpPost("2fa/disable")]
    public async Task<IActionResult> DisableTwoFactor(TwoFactorDisableRequest request, CancellationToken ct)
    {
        await profile.DisableTwoFactorAsync(request, ct);
        return NoContent();
    }

    [HttpGet("library")]
    public Task<IReadOnlyList<LibraryItemDto>> Library(CancellationToken ct) => profile.GetLibraryAsync(ct);

    [HttpPost("library/{id:int}/activated")]
    public Task<LibraryItemDto> Activate(int id, ActivateRequest request, CancellationToken ct) =>
        profile.MarkActivatedAsync(id, request.Activated, ct);

    [HttpGet("balance")]
    public Task<PagedResult<BalanceTransactionDto>> Balance([FromQuery] PageRequest page, CancellationToken ct) =>
        profile.GetBalanceHistoryAsync(page, ct);
}

[ApiController]
[Authorize]
[Route("api/wishlist")]
public sealed class WishlistController(IWishlistService wishlist) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<WishlistItemDto>> Get(CancellationToken ct) => wishlist.GetAsync(ct);

    [HttpGet("ids")]
    public Task<IReadOnlyList<int>> Ids(CancellationToken ct) => wishlist.GetGameIdsAsync(ct);

    [HttpPost("{gameId:int}")]
    public async Task<IActionResult> Add(int gameId, CancellationToken ct)
    {
        await wishlist.AddAsync(gameId, ct);
        return NoContent();
    }

    [HttpDelete("{gameId:int}")]
    public async Task<IActionResult> Remove(int gameId, CancellationToken ct)
    {
        await wishlist.RemoveAsync(gameId, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/reviews")]
public sealed class ReviewsController(IReviewService reviews) : ControllerBase
{
    [HttpGet("game/{gameId:int}")]
    public Task<PagedResult<ReviewDto>> ForGame(int gameId, [FromQuery] PageRequest page, CancellationToken ct) =>
        reviews.GetForGameAsync(gameId, page, ct);

    [Authorize, HttpGet("game/{gameId:int}/mine")]
    public Task<ReviewDto?> Mine(int gameId, CancellationToken ct) => reviews.GetMineForGameAsync(gameId, ct);

    [Authorize, HttpPost]
    public Task<ReviewDto> Upsert(CreateReviewRequest request, CancellationToken ct) => reviews.UpsertAsync(request, ct);

    [Authorize, HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await reviews.DeleteMineAsync(id, ct);
        return NoContent();
    }

    [HttpGet("site")]
    public Task<SiteReviewSummaryDto> Site([FromQuery] int take = 12, CancellationToken ct = default) =>
        reviews.GetSiteReviewsAsync(take, ct);

    [Authorize, HttpGet("site/mine")]
    public Task<SiteReviewDto?> MySite(CancellationToken ct) => reviews.GetMySiteReviewAsync(ct);

    [Authorize, HttpPut("site")]
    public Task<SiteReviewDto> UpsertSite(SiteReviewRequest request, CancellationToken ct) =>
        reviews.UpsertSiteReviewAsync(request, ct);
}

[ApiController]
[Authorize]
[Route("api/support/tickets")]
public sealed class SupportController(ISupportService support) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<TicketSummaryDto>> Mine(CancellationToken ct) => support.GetMyTicketsAsync(ct);

    [HttpPost]
    public Task<TicketDetailDto> Create(CreateTicketRequest request, CancellationToken ct) => support.CreateAsync(request, ct);

    [HttpGet("{id:int}")]
    public Task<TicketDetailDto> Get(int id, CancellationToken ct) => support.GetAsync(id, ct);

    [HttpPost("{id:int}/messages")]
    public Task<SupportMessageDto> Post(int id, PostMessageRequest request, CancellationToken ct) =>
        support.PostMessageAsync(id, request, ct);

    [HttpPost("{id:int}/close")]
    public async Task<IActionResult> Close(int id, CancellationToken ct)
    {
        await support.CloseAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Authorize]
[Route("api/notifications")]
public sealed class NotificationsController(INotificationService notifications) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<NotificationDto>> Get([FromQuery] int take = 30, CancellationToken ct = default) =>
        notifications.GetMineAsync(take, ct);

    [HttpGet("unread-count")]
    public Task<int> Unread(CancellationToken ct) => notifications.CountUnreadAsync(ct);

    [HttpPost("read")]
    public async Task<IActionResult> Read([FromQuery] int? id, CancellationToken ct)
    {
        await notifications.MarkReadAsync(id, ct);
        return NoContent();
    }
}

/// <summary>Stripe webhook: verifies the signature and completes the payment.</summary>
[ApiController]
[Route("api/webhooks")]
public sealed class WebhooksController(IPaymentService payments, IOptions<StripeOptions> stripe, TimeProvider clock)
    : ControllerBase
{
    [HttpPost("stripe")]
    public async Task<IActionResult> Stripe(CancellationToken ct)
    {
        if (!stripe.Value.Enabled || string.IsNullOrEmpty(stripe.Value.WebhookSecret)) return NotFound();

        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(ct);
        var signature = Request.Headers["Stripe-Signature"].ToString();
        if (!StripePaymentStrategy.VerifySignature(payload, signature, stripe.Value.WebhookSecret, clock.GetUtcNow(),
                TimeSpan.FromMinutes(5)))
            return BadRequest();

        using var json = JsonDocument.Parse(payload);
        var type = json.RootElement.GetProperty("type").GetString();
        var session = json.RootElement.GetProperty("data").GetProperty("object");
        var sessionId = session.GetProperty("id").GetString()!;

        switch (type)
        {
            case "checkout.session.completed" when session.GetProperty("payment_status").GetString() == "paid":
                await payments.HandleProviderResultAsync(PaymentMethod.Stripe, sessionId, true, ct);
                break;
            case "checkout.session.expired":
                await payments.HandleProviderResultAsync(PaymentMethod.Stripe, sessionId, false, ct);
                break;
        }
        return Ok();
    }
}

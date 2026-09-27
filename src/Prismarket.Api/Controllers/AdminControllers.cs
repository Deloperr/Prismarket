using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Prismarket.Application.Common;
using Prismarket.Application.Features.Admin;
using Prismarket.Application.Features.Automation;
using Prismarket.Application.Features.Catalog;
using Prismarket.Application.Features.Orders;
using Prismarket.Application.Features.Reports;
using Prismarket.Application.Features.Reviews;
using Prismarket.Application.Features.Support;
using Prismarket.Domain.Enums;

namespace Prismarket.Api.Controllers;

[ApiController]
[Authorize(Roles = Roles.Admin)]
[Route("api/admin")]
public sealed class AdminDashboardController(IAnalyticsQueries analytics, IReportService reports, IExportService exports)
    : ControllerBase
{
    [HttpGet("dashboard")]
    public Task<DashboardDto> Dashboard(CancellationToken ct) => analytics.GetDashboardAsync(ct);

    /// <summary>Sales report for [from; to) in PDF / Excel / Word.</summary>
    [HttpGet("reports/sales")]
    public async Task<IActionResult> SalesReport([FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] ReportFormat format = ReportFormat.Pdf, CancellationToken ct = default)
    {
        var end = (to ?? DateTime.UtcNow.Date.AddDays(1)).ToUniversalTime();
        var start = (from ?? end.AddDays(-30)).ToUniversalTime();
        var file = await reports.BuildSalesReportAsync(DateTime.SpecifyKind(start, DateTimeKind.Utc),
            DateTime.SpecifyKind(end, DateTimeKind.Utc), format, ct);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpGet("export/games")]
    public async Task<IActionResult> ExportGames([FromQuery] ExportFormat format = ExportFormat.Json, CancellationToken ct = default)
    {
        var file = await exports.ExportGamesAsync(format, ct);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpGet("export/orders")]
    public async Task<IActionResult> ExportOrders([FromQuery] ExportFormat format = ExportFormat.Csv,
        [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null, CancellationToken ct = default)
    {
        var file = await exports.ExportOrdersAsync(format, from?.ToUniversalTime(), to?.ToUniversalTime(), ct);
        return File(file.Content, file.ContentType, file.FileName);
    }
}

[ApiController]
[Authorize(Roles = Roles.Admin)]
[Route("api/admin/games")]
public sealed class AdminGamesController(IAdminCatalogService catalog) : ControllerBase
{
    public sealed record GenerateKeysRequest(int Count);

    [HttpGet]
    public Task<PagedResult<AdminGameDto>> List([FromQuery] string? search, [FromQuery] PageRequest page, CancellationToken ct) =>
        catalog.ListGamesAsync(search, page, ct);

    [HttpGet("{id:int}")]
    public Task<AdminGameDto> Get(int id, CancellationToken ct) => catalog.GetGameAsync(id, ct);

    [HttpPost]
    public Task<AdminGameDto> Create(GameInput input, CancellationToken ct) => catalog.CreateGameAsync(input, ct);

    [HttpPut("{id:int}")]
    public Task<AdminGameDto> Update(int id, GameInput input, CancellationToken ct) => catalog.UpdateGameAsync(id, input, ct);

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await catalog.DeleteGameAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:int}/image/{kind:regex(^(cover|header)$)}")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<AdminGameDto> Image(int id, string kind, IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        return await catalog.UploadImageAsync(id, kind, stream, file.FileName, ct);
    }

    [HttpGet("products/{productId:int}/keys")]
    public Task<PagedResult<KeyDto>> Keys(int productId, [FromQuery] StockItemStatus? status, [FromQuery] PageRequest page,
        CancellationToken ct) => catalog.ListKeysAsync(productId, status, page, ct);

    [HttpPost("products/{productId:int}/keys")]
    public Task<ImportResultDto> ImportKeys(int productId, ImportKeysRequest request, CancellationToken ct) =>
        catalog.ImportKeysAsync(productId, request, ct);

    [HttpPost("products/{productId:int}/keys/file")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<ImportResultDto> ImportKeysFile(int productId, IFormFile file, CancellationToken ct)
    {
        using var reader = new StreamReader(file.OpenReadStream());
        return await catalog.ImportKeysAsync(productId, new ImportKeysRequest(await reader.ReadToEndAsync(ct)), ct);
    }

    [HttpPost("products/{productId:int}/keys/generate")]
    public Task<ImportResultDto> GenerateKeys(int productId, GenerateKeysRequest request, CancellationToken ct) =>
        catalog.GenerateDemoKeysAsync(productId, request.Count, ct);

    [HttpPost("products/{productId:int}/accounts")]
    public Task<ImportResultDto> ImportAccounts(int productId, ImportAccountsRequest request, CancellationToken ct) =>
        catalog.ImportAccountsAsync(productId, request, ct);

    [HttpDelete("keys/{keyId:int}")]
    public async Task<IActionResult> DeleteKey(int keyId, CancellationToken ct)
    {
        await catalog.DeleteKeyAsync(keyId, ct);
        return NoContent();
    }

    [HttpPost("/api/admin/lookups/{type:regex(^(genres|platforms|developers|publishers)$)}")]
    public Task<LookupDto> CreateLookup(string type, LookupInput input, CancellationToken ct) =>
        catalog.CreateLookupAsync(type, input, ct);
}

[ApiController]
[Authorize(Roles = Roles.Admin)]
[Route("api/admin")]
public sealed class AdminManagementController(
    IAdminUserService users,
    IAdminOrderService orders,
    IReviewService reviews,
    ISupportService support,
    IPromotionAdminService promotions) : ControllerBase
{
    public sealed record RefundRequest(string Reason);
    public sealed record ReviewStatusRequest(ReviewStatus Status);

    // --- users ---
    [HttpGet("users")]
    public Task<PagedResult<AdminUserDto>> Users([FromQuery] string? search, [FromQuery] UserRole? role,
        [FromQuery] PageRequest page, CancellationToken ct) => users.ListAsync(search, role, page, ct);

    [HttpGet("users/{id:int}")]
    public Task<AdminUserDetailDto> GetUser(int id, CancellationToken ct) => users.GetAsync(id, ct);

    [HttpPatch("users/{id:int}")]
    public Task<AdminUserDto> UpdateUser(int id, UpdateUserRequest request, CancellationToken ct) =>
        users.UpdateAsync(id, request, ct);

    [HttpPost("users/{id:int}/balance")]
    public Task<AdminUserDto> AdjustBalance(int id, AdjustBalanceRequest request, CancellationToken ct) =>
        users.AdjustBalanceAsync(id, request, ct);

    [HttpGet("audit")]
    public Task<PagedResult<AuditLogDto>> Audit([FromQuery] string? entity, [FromQuery] int? userId,
        [FromQuery] PageRequest page, CancellationToken ct) => users.GetAuditLogAsync(entity, userId, page, ct);

    // --- orders ---
    [HttpGet("orders")]
    public Task<PagedResult<OrderDto>> Orders([FromQuery] OrderStatus? status, [FromQuery] string? search,
        [FromQuery] PageRequest page, CancellationToken ct) => orders.ListAsync(status, search, page, ct);

    [HttpPost("orders/{number}/refund")]
    public Task<OrderDto> Refund(string number, RefundRequest request, CancellationToken ct) =>
        orders.RefundAsync(number, request.Reason, ct);

    [HttpPost("orders/{number}/mark-paid")]
    public Task<OrderDto> MarkPaid(string number, CancellationToken ct) => orders.MarkPaidAsync(number, ct);

    // --- reviews ---
    [HttpGet("reviews")]
    public Task<PagedResult<ReviewDto>> Reviews([FromQuery] ReviewStatus? status, [FromQuery] PageRequest page,
        CancellationToken ct) => reviews.AdminListAsync(status, page, ct);

    [HttpPatch("reviews/{id:int}")]
    public Task<ReviewDto> SetReviewStatus(int id, ReviewStatusRequest request, CancellationToken ct) =>
        reviews.AdminSetStatusAsync(id, request.Status, ct);

    [HttpDelete("reviews/{id:int}")]
    public async Task<IActionResult> DeleteReview(int id, CancellationToken ct)
    {
        await reviews.AdminDeleteAsync(id, ct);
        return NoContent();
    }

    // --- support ---
    [HttpGet("support/tickets")]
    public Task<PagedResult<TicketSummaryDto>> Tickets([FromQuery] TicketStatus? status, [FromQuery] bool onlyMine,
        [FromQuery] PageRequest page, CancellationToken ct) => support.AdminListAsync(status, onlyMine, page, ct);

    [HttpPatch("support/tickets/{id:int}")]
    public Task<TicketDetailDto> UpdateTicket(int id, UpdateTicketRequest request, CancellationToken ct) =>
        support.AdminUpdateAsync(id, request, ct);

    // --- promotions & promo codes ---
    [HttpGet("promotions")]
    public Task<IReadOnlyList<PromotionDto>> Promotions(CancellationToken ct) => promotions.ListAsync(ct);

    [HttpPost("promotions")]
    public Task<PromotionDto> CreatePromotion(PromotionInput input, CancellationToken ct) => promotions.CreateAsync(input, ct);

    [HttpPut("promotions/{id:int}")]
    public Task<PromotionDto> UpdatePromotion(int id, PromotionInput input, CancellationToken ct) =>
        promotions.UpdateAsync(id, input, ct);

    [HttpPost("promotions/{id:int}/cancel")]
    public async Task<IActionResult> CancelPromotion(int id, CancellationToken ct)
    {
        await promotions.CancelAsync(id, ct);
        return NoContent();
    }

    [HttpGet("promo-codes")]
    public Task<PagedResult<PromoCodeDto>> PromoCodes([FromQuery] PageRequest page, CancellationToken ct) =>
        promotions.ListPromoCodesAsync(page, ct);

    [HttpPost("promo-codes")]
    public Task<PromoCodeDto> CreatePromoCode(PromoCodeInput input, CancellationToken ct) =>
        promotions.CreatePromoCodeAsync(input, ct);

    [HttpPut("promo-codes/{id:int}")]
    public Task<PromoCodeDto> UpdatePromoCode(int id, PromoCodeInput input, CancellationToken ct) =>
        promotions.UpdatePromoCodeAsync(id, input, ct);

    [HttpDelete("promo-codes/{id:int}")]
    public async Task<IActionResult> DeletePromoCode(int id, CancellationToken ct)
    {
        await promotions.DeletePromoCodeAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Authorize(Roles = Roles.Admin)]
[Route("api/admin/automation")]
public sealed class AdminAutomationController(IAutomationAdminService automation) : ControllerBase
{
    public sealed record RunResponse(string JobId);

    [HttpGet]
    public Task<IReadOnlyList<AutomationJobDto>> Jobs(CancellationToken ct) => automation.GetJobsAsync(ct);

    [HttpGet("overview")]
    public Task<AutomationOverviewDto> Overview(CancellationToken ct) => automation.GetOverviewAsync(ct);

    [HttpPatch("{key}")]
    public Task<AutomationJobDto> Update(string key, UpdateAutomationRequest request, CancellationToken ct) =>
        automation.UpdateAsync(key, request, ct);

    [HttpPost("{key}/run")]
    public async Task<RunResponse> Run(string key, CancellationToken ct) => new(await automation.RunNowAsync(key, ct));

    [HttpGet("runs")]
    public Task<PagedResult<AutomationRunDto>> Runs([FromQuery] string? key, [FromQuery] AutomationRunStatus? status,
        [FromQuery] PageRequest page, CancellationToken ct) => automation.GetRunsAsync(key, status, page, ct);
}

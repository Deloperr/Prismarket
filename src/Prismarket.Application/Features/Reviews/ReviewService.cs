using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Prismarket.Application.Common;
using Prismarket.Application.Features.Automation;
using Prismarket.Application.Features.Notifications;
using Prismarket.Domain.Entities;
using Prismarket.Domain.Enums;

namespace Prismarket.Application.Features.Reviews;

public sealed record ReviewDto(int Id, int GameId, string GameTitle, int UserId, string Username, string? AvatarUrl,
    int Rating, string? Title, string? Comment, bool IsVerifiedPurchase, ReviewStatus Status, string? ModerationNote,
    DateTime CreatedAt, DateTime? UpdatedAt);

public sealed record CreateReviewRequest(
    [Range(1, int.MaxValue)] int GameId,
    [Range(1, 5)] int Rating,
    [StringLength(120)] string? Title,
    [StringLength(3000)] string? Comment);

public sealed record SiteReviewDto(int Id, string Username, string? AvatarUrl, int Rating, string? Comment,
    DateTime CreatedAt);

public sealed record SiteReviewRequest([Range(1, 5)] int Rating, [StringLength(2000)] string? Comment);

public sealed record SiteReviewSummaryDto(decimal Average, int Count, IReadOnlyList<SiteReviewDto> Latest);

/// <summary>Result of automatic moderation.</summary>
public sealed record ModerationVerdict(bool Approved, string? Reason);

public interface IReviewModerator
{
    ModerationVerdict Check(string? title, string? comment, JobSettings settings);
}

/// <summary>Rule-based moderation: stop-words and minimal length. Pure function — easy to unit test.</summary>
public sealed class ReviewModerator : IReviewModerator
{
    public ModerationVerdict Check(string? title, string? comment, JobSettings settings)
    {
        var text = $"{title} {comment}".ToLowerInvariant();
        var banned = settings.GetList("bannedWords").Select(w => w.ToLowerInvariant())
            .FirstOrDefault(w => w.Length > 0 && text.Contains(w));
        if (banned is not null) return new ModerationVerdict(false, $"Найдено стоп-слово «{banned}»");

        var minLength = settings.GetInt("minLength");
        if (minLength > 0 && (comment?.Trim().Length ?? 0) < minLength)
            return new ModerationVerdict(false, $"Комментарий короче {minLength} символов");

        return new ModerationVerdict(true, null);
    }
}

public interface IReviewService
{
    Task<PagedResult<ReviewDto>> GetForGameAsync(int gameId, PageRequest page, CancellationToken ct);
    Task<ReviewDto?> GetMineForGameAsync(int gameId, CancellationToken ct);
    Task<ReviewDto> UpsertAsync(CreateReviewRequest request, CancellationToken ct);
    Task DeleteMineAsync(int reviewId, CancellationToken ct);

    Task<PagedResult<ReviewDto>> AdminListAsync(ReviewStatus? status, PageRequest page, CancellationToken ct);
    Task<ReviewDto> AdminSetStatusAsync(int reviewId, ReviewStatus status, CancellationToken ct);
    Task AdminDeleteAsync(int reviewId, CancellationToken ct);

    Task<SiteReviewSummaryDto> GetSiteReviewsAsync(int take, CancellationToken ct);
    Task<SiteReviewDto?> GetMySiteReviewAsync(CancellationToken ct);
    Task<SiteReviewDto> UpsertSiteReviewAsync(SiteReviewRequest request, CancellationToken ct);

    Task RecalculateRatingAsync(int gameId, CancellationToken ct);
}

public sealed class ReviewService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IReviewModerator moderator,
    IAutomationSettingsProvider automation,
    IAutomationEventLog automationLog,
    INotificationService notifications) : IReviewService
{
    public Task<PagedResult<ReviewDto>> GetForGameAsync(int gameId, PageRequest page, CancellationToken ct) =>
        Project(db.Reviews.Where(r => r.GameId == gameId && r.Status == ReviewStatus.Published)
                .OrderByDescending(r => r.IsVerifiedPurchase).ThenByDescending(r => r.CreatedAt))
            .ToPagedAsync(page, ct);

    public async Task<ReviewDto?> GetMineForGameAsync(int gameId, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        return await Project(db.Reviews.Where(r => r.GameId == gameId && r.UserId == userId)).FirstOrDefaultAsync(ct);
    }

    public async Task<ReviewDto> UpsertAsync(CreateReviewRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        if (!await db.Games.AnyAsync(g => g.Id == request.GameId, ct))
            throw NotFoundException.For("Игра", request.GameId);

        var review = await db.Reviews.FirstOrDefaultAsync(r => r.GameId == request.GameId && r.UserId == userId, ct);
        if (review is null)
        {
            review = new Review { GameId = request.GameId, UserId = userId };
            db.Reviews.Add(review);
        }

        review.Rating = request.Rating;
        review.Title = request.Title?.Trim();
        review.Comment = request.Comment?.Trim();
        review.IsVerifiedPurchase = await db.LibraryItems
            .AnyAsync(l => l.UserId == userId && l.Product.GameId == request.GameId, ct);

        // Event-driven automation: auto-moderation.
        var (enabled, settings) = await automation.GetAsync(AutomationKeys.ReviewModeration, ct);
        if (enabled)
        {
            var verdict = moderator.Check(review.Title, review.Comment, settings);
            review.Status = verdict.Approved ? ReviewStatus.Published : ReviewStatus.PendingModeration;
            review.ModerationNote = verdict.Reason;
            automationLog.Record(AutomationKeys.ReviewModeration, 1, verdict.Approved
                ? "Отзыв опубликован автоматически"
                : $"Отзыв отправлен на модерацию: {verdict.Reason}");
            if (!verdict.Approved)
                await notifications.AddForAdminsAsync(NotificationType.System, "Отзыв на модерации",
                    verdict.Reason ?? "Требуется проверка", "/admin/reviews", ct);
        }
        else
        {
            review.Status = ReviewStatus.Published;
        }

        await db.SaveChangesAsync(ct);
        await RecalculateRatingAsync(request.GameId, ct);
        await notifications.FlushAsync(ct);
        return await Project(db.Reviews.Where(r => r.Id == review.Id)).FirstAsync(ct);
    }

    public async Task DeleteMineAsync(int reviewId, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId && r.UserId == userId, ct)
                     ?? throw NotFoundException.For("Отзыв", reviewId);
        db.Reviews.Remove(review);
        await db.SaveChangesAsync(ct);
        await RecalculateRatingAsync(review.GameId, ct);
    }

    public Task<PagedResult<ReviewDto>> AdminListAsync(ReviewStatus? status, PageRequest page, CancellationToken ct)
    {
        var query = db.Reviews.AsQueryable();
        if (status is not null) query = query.Where(r => r.Status == status);
        return Project(query.OrderByDescending(r => r.CreatedAt)).ToPagedAsync(page, ct);
    }

    public async Task<ReviewDto> AdminSetStatusAsync(int reviewId, ReviewStatus status, CancellationToken ct)
    {
        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId, ct)
                     ?? throw NotFoundException.For("Отзыв", reviewId);
        review.Status = status;
        await db.SaveChangesAsync(ct);
        await RecalculateRatingAsync(review.GameId, ct);
        return await Project(db.Reviews.Where(r => r.Id == reviewId)).FirstAsync(ct);
    }

    public async Task AdminDeleteAsync(int reviewId, CancellationToken ct)
    {
        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId, ct)
                     ?? throw NotFoundException.For("Отзыв", reviewId);
        db.Reviews.Remove(review);
        await db.SaveChangesAsync(ct);
        await RecalculateRatingAsync(review.GameId, ct);
    }

    public async Task<SiteReviewSummaryDto> GetSiteReviewsAsync(int take, CancellationToken ct)
    {
        var avg = await db.SiteReviews.Select(r => (decimal?)r.Rating).AverageAsync(ct) ?? 0;
        var count = await db.SiteReviews.CountAsync(ct);
        var latest = await db.SiteReviews.AsNoTracking().OrderByDescending(r => r.CreatedAt)
            .Take(Math.Clamp(take, 1, 50))
            .Select(r => new SiteReviewDto(r.Id, r.User.Username, r.User.AvatarUrl, r.Rating, r.Comment, r.CreatedAt))
            .ToListAsync(ct);
        return new SiteReviewSummaryDto(Math.Round(avg, 2), count, latest);
    }

    public async Task<SiteReviewDto?> GetMySiteReviewAsync(CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        return await db.SiteReviews.AsNoTracking().Where(r => r.UserId == userId)
            .Select(r => new SiteReviewDto(r.Id, r.User.Username, r.User.AvatarUrl, r.Rating, r.Comment, r.CreatedAt))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<SiteReviewDto> UpsertSiteReviewAsync(SiteReviewRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var review = await db.SiteReviews.FirstOrDefaultAsync(r => r.UserId == userId, ct);
        if (review is null)
        {
            review = new SiteReview { UserId = userId };
            db.SiteReviews.Add(review);
        }
        review.Rating = request.Rating;
        review.Comment = request.Comment?.Trim();
        await db.SaveChangesAsync(ct);
        return (await GetMySiteReviewAsync(ct))!;
    }

    /// <summary>Keeps the denormalized rating on the game in sync with published reviews.</summary>
    public async Task RecalculateRatingAsync(int gameId, CancellationToken ct)
    {
        var stats = await db.Reviews.Where(r => r.GameId == gameId && r.Status == ReviewStatus.Published)
            .GroupBy(r => r.GameId)
            .Select(g => new { Avg = g.Average(r => (decimal)r.Rating), Count = g.Count() })
            .FirstOrDefaultAsync(ct);

        await db.Games.Where(g => g.Id == gameId).ExecuteUpdateAsync(s => s
            .SetProperty(g => g.AverageRating, stats == null ? 0 : Math.Round(stats.Avg, 2))
            .SetProperty(g => g.RatingsCount, stats == null ? 0 : stats.Count), ct);
    }

    private static IQueryable<ReviewDto> Project(IQueryable<Review> query) => query.AsNoTracking()
        .Select(r => new ReviewDto(r.Id, r.GameId, r.Game.Title, r.UserId, r.User.Username, r.User.AvatarUrl,
            r.Rating, r.Title, r.Comment, r.IsVerifiedPurchase, r.Status, r.ModerationNote, r.CreatedAt, r.UpdatedAt));
}

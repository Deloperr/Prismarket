using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Prismarket.Application.Common;
using Prismarket.Domain.Entities;
using Prismarket.Domain.Enums;

namespace Prismarket.Application.Features.Catalog;

public interface ICatalogService
{
    Task<PagedResult<GameCardDto>> SearchAsync(GameQuery query, CancellationToken ct);
    Task<GameDetailDto> GetGameAsync(string idOrSlug, CancellationToken ct);
    Task<HomeDto> GetHomeAsync(CancellationToken ct);
    Task<IReadOnlyList<GameCardDto>> GetRecommendationsAsync(int take, CancellationToken ct);
    Task<IReadOnlyList<LookupDto>> GetGenresAsync(CancellationToken ct);
    Task<IReadOnlyList<LookupDto>> GetPlatformsAsync(CancellationToken ct);
    Task<IReadOnlyList<LookupDto>> GetDevelopersAsync(CancellationToken ct);
    Task<IReadOnlyList<LookupDto>> GetPublishersAsync(CancellationToken ct);
}

public sealed class CatalogService(IAppDbContext db, ICurrentUser currentUser, ICacheService cache, TimeProvider clock)
    : ICatalogService
{
    internal const string HomeCacheKey = "catalog:home";
    internal const string LookupsCachePrefix = "catalog:lookup:";

    /// <summary>Single projection reused by every list endpoint (DRY). Translated to SQL by EF Core.</summary>
    internal static readonly Expression<Func<Game, GameCardProjection>> CardProjection = g => new GameCardProjection
    {
        Id = g.Id,
        Slug = g.Slug,
        Title = g.Title,
        CoverImageUrl = g.CoverImageUrl,
        HeaderImageUrl = g.HeaderImageUrl,
        Platform = g.Platform.Name,
        Genres = g.Genres.OrderBy(x => x.Name).Select(x => x.Name).ToList(),
        AverageRating = g.AverageRating,
        RatingsCount = g.RatingsCount,
        IsFeatured = g.IsFeatured,
        Products = g.Products.Where(p => p.IsAvailable).Select(p => new ProductPriceProjection
        {
            Price = p.Price,
            DiscountPercent = p.DiscountPercent,
            InStock = p.Keys.Count(k => k.Status == StockItemStatus.Available)
                      + p.Accounts.Count(a => a.Status == StockItemStatus.Available)
        }).ToList()
    };

    public async Task<PagedResult<GameCardDto>> SearchAsync(GameQuery q, CancellationToken ct)
    {
        IQueryable<Game> games = db.Games.AsNoTracking().Where(g => g.IsAvailable);

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var term = $"%{q.Search.Trim().ToLowerInvariant()}%";
            games = games.Where(g => EF.Functions.Like(g.Title.ToLower(), term)
                                     || EF.Functions.Like(g.Developer.Name.ToLower(), term)
                                     || EF.Functions.Like(g.Publisher.Name.ToLower(), term));
        }
        if (q.GenreId is not null) games = games.Where(g => g.Genres.Any(x => x.Id == q.GenreId));
        if (q.PlatformId is not null) games = games.Where(g => g.PlatformId == q.PlatformId);
        if (q.OnSale == true) games = games.Where(g => g.Products.Any(p => p.IsAvailable && p.DiscountPercent > 0));
        if (q.InStock == true)
            games = games.Where(g => g.Products.Any(p => p.IsAvailable &&
                (p.Keys.Any(k => k.Status == StockItemStatus.Available) ||
                 p.Accounts.Any(a => a.Status == StockItemStatus.Available))));

        // Price expressed in SQL: min over available editions of price * (100 - discount) / 100
        if (q.MinPrice is not null)
            games = games.Where(g => g.Products.Where(p => p.IsAvailable)
                .Min(p => (decimal?)(p.Price * (100 - p.DiscountPercent) / 100)) >= q.MinPrice);
        if (q.MaxPrice is not null)
            games = games.Where(g => g.Products.Where(p => p.IsAvailable)
                .Min(p => (decimal?)(p.Price * (100 - p.DiscountPercent) / 100)) <= q.MaxPrice);

        games = q.Sort switch
        {
            "price_asc" => games.OrderBy(g => g.Products.Where(p => p.IsAvailable)
                .Min(p => (decimal?)(p.Price * (100 - p.DiscountPercent) / 100))),
            "price_desc" => games.OrderByDescending(g => g.Products.Where(p => p.IsAvailable)
                .Min(p => (decimal?)(p.Price * (100 - p.DiscountPercent) / 100))),
            "rating" => games.OrderByDescending(g => g.AverageRating).ThenByDescending(g => g.RatingsCount),
            "new" => games.OrderByDescending(g => g.ReleaseDate),
            "title" => games.OrderBy(g => g.Title),
            "discount" => games.OrderByDescending(g => g.Products.Max(p => (int?)p.DiscountPercent)),
            _ => games.OrderByDescending(g => g.IsFeatured).ThenByDescending(g => g.SalesCount).ThenBy(g => g.Title)
        };

        var page = await games.Select(CardProjection)
            .ToPagedAsync(new PageRequest(q.Page, q.PageSize), ct);

        return new PagedResult<GameCardDto>(page.Items.Select(ToCard).ToList(), page.Page, page.PageSize,
            page.TotalCount);
    }

    public async Task<GameDetailDto> GetGameAsync(string idOrSlug, CancellationToken ct)
    {
        var query = db.Games.AsNoTracking()
            .Include(g => g.Developer).Include(g => g.Publisher).Include(g => g.Platform)
            .Include(g => g.Genres)
            .AsSplitQuery();

        var game = int.TryParse(idOrSlug, out var id)
            ? await query.FirstOrDefaultAsync(g => g.Id == id, ct)
            : await query.FirstOrDefaultAsync(g => g.Slug == idOrSlug, ct);
        if (game is null) throw NotFoundException.For("Игра", idOrSlug);

        var products = await db.Products.AsNoTracking()
            .Where(p => p.GameId == game.Id && (p.IsAvailable || p.HiddenByStockMonitor))
            .OrderBy(p => p.Price)
            .Select(p => new ProductDto(p.Id, p.Kind, p.Edition, p.Price, p.DiscountPercent,
                0, p.IsAvailable,
                p.Keys.Count(k => k.Status == StockItemStatus.Available) +
                p.Accounts.Count(a => a.Status == StockItemStatus.Available)))
            .ToListAsync(ct);
        products = products.Select(p => p with { FinalPrice = Product.ApplyDiscount(p.Price, p.DiscountPercent) })
            .ToList();

        var distribution = await db.Reviews.AsNoTracking()
            .Where(r => r.GameId == game.Id && r.Status == ReviewStatus.Published)
            .GroupBy(r => r.Rating)
            .Select(g => new { Rating = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var ratingDistribution = new int[5];
        foreach (var d in distribution)
            if (d.Rating is >= 1 and <= 5) ratingDistribution[d.Rating - 1] = d.Count;

        var now = clock.GetUtcNow().UtcDateTime;
        var productIds = products.Select(p => p.Id).ToList();
        var promotion = await db.PromotionProducts.AsNoTracking()
            .Where(pp => productIds.Contains(pp.ProductId) && pp.Promotion.Status == PromotionStatus.Active
                         && pp.Promotion.EndsAt > now)
            .Select(pp => new ActivePromotionDto(pp.Promotion.Id, pp.Promotion.Name, pp.Promotion.DiscountPercent,
                pp.Promotion.EndsAt))
            .FirstOrDefaultAsync(ct);

        var inWishlist = false;
        var owned = false;
        if (currentUser.UserId is { } userId)
        {
            inWishlist = await db.WishlistItems.AnyAsync(w => w.UserId == userId && w.GameId == game.Id, ct);
            owned = await db.LibraryItems.AnyAsync(l => l.UserId == userId && l.Product.GameId == game.Id, ct);
        }

        return new GameDetailDto(game.Id, game.Slug, game.Title, game.ShortDescription, game.Description,
            game.ReleaseDate, game.AgeRating, game.SystemRequirements, game.CoverImageUrl, game.HeaderImageUrl,
            game.TrailerUrl, game.IsAvailable, game.IsFeatured,
            new LookupDto(game.Developer.Id, game.Developer.Name),
            new LookupDto(game.Publisher.Id, game.Publisher.Name),
            new LookupDto(game.Platform.Id, game.Platform.Name, game.Platform.Slug),
            game.Genres.OrderBy(x => x.Name).Select(x => new LookupDto(x.Id, x.Name, x.Slug)).ToList(),
            products, game.AverageRating, game.RatingsCount, ratingDistribution, game.SalesCount,
            inWishlist, owned, promotion);
    }

    public Task<HomeDto> GetHomeAsync(CancellationToken ct) =>
        cache.GetOrCreateAsync(HomeCacheKey, TimeSpan.FromMinutes(2), BuildHomeAsync, ct);

    private async Task<HomeDto> BuildHomeAsync(CancellationToken ct)
    {
        var baseQuery = db.Games.AsNoTracking().Where(g => g.IsAvailable);

        // DbContext is not thread-safe, so queries run sequentially here.
        // Parallel aggregation over independent connections is shown in the Dapper analytics service.
        var featured = await baseQuery.Where(g => g.IsFeatured).OrderByDescending(g => g.SalesCount)
            .Take(6).Select(CardProjection).ToListAsync(ct);
        var newReleases = await baseQuery.OrderByDescending(g => g.ReleaseDate).Take(8)
            .Select(CardProjection).ToListAsync(ct);
        var topSellers = await baseQuery.OrderByDescending(g => g.SalesCount).Take(8)
            .Select(CardProjection).ToListAsync(ct);
        var onSale = await baseQuery.Where(g => g.Products.Any(p => p.IsAvailable && p.DiscountPercent > 0))
            .OrderByDescending(g => g.Products.Max(p => p.DiscountPercent)).Take(8)
            .Select(CardProjection).ToListAsync(ct);

        var now = clock.GetUtcNow().UtcDateTime;
        var promotions = await db.Promotions.AsNoTracking()
            .Where(p => p.Status == PromotionStatus.Active || (p.Status == PromotionStatus.Scheduled && p.StartsAt > now))
            .OrderBy(p => p.StartsAt).Take(3)
            .Select(p => new PromotionBannerDto(p.Id, p.Name, p.Description, p.DiscountPercent, p.StartsAt, p.EndsAt,
                p.BannerImageUrl, p.Products.Count))
            .ToListAsync(ct);

        var stats = new StoreStatsDto(
            await baseQuery.CountAsync(ct),
            await db.LibraryItems.CountAsync(ct),
            await db.Users.CountAsync(u => u.Role == UserRole.Customer, ct),
            await db.SiteReviews.Select(r => (decimal?)r.Rating).AverageAsync(ct) ?? 0m);

        if (featured.Count == 0) featured = topSellers.Take(6).ToList();

        return new HomeDto(featured.Select(ToCard).ToList(), newReleases.Select(ToCard).ToList(),
            topSellers.Select(ToCard).ToList(), onSale.Select(ToCard).ToList(), promotions, stats);
    }

    /// <summary>Simple content-based recommendations: genres from the user's library and wishlist.</summary>
    public async Task<IReadOnlyList<GameCardDto>> GetRecommendationsAsync(int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, 24);
        if (currentUser.UserId is not { } userId)
        {
            var popular = await db.Games.AsNoTracking().Where(g => g.IsAvailable)
                .OrderByDescending(g => g.SalesCount).Take(take).Select(CardProjection).ToListAsync(ct);
            return popular.Select(ToCard).ToList();
        }

        var ownedGameIds = db.LibraryItems.Where(l => l.UserId == userId).Select(l => l.Product.GameId);
        var wishedGameIds = db.WishlistItems.Where(w => w.UserId == userId).Select(w => w.GameId);

        var favouriteGenres = await db.Games.AsNoTracking()
            .Where(g => ownedGameIds.Contains(g.Id) || wishedGameIds.Contains(g.Id))
            .SelectMany(g => g.Genres)
            .GroupBy(x => x.Id)
            .OrderByDescending(x => x.Count())
            .Take(3)
            .Select(x => x.Key)
            .ToListAsync(ct);

        var query = db.Games.AsNoTracking()
            .Where(g => g.IsAvailable && !ownedGameIds.Contains(g.Id));
        if (favouriteGenres.Count > 0)
            query = query.Where(g => g.Genres.Any(x => favouriteGenres.Contains(x.Id)));

        var result = await query.OrderByDescending(g => g.AverageRating).ThenByDescending(g => g.SalesCount)
            .Take(take).Select(CardProjection).ToListAsync(ct);
        return result.Select(ToCard).ToList();
    }

    public Task<IReadOnlyList<LookupDto>> GetGenresAsync(CancellationToken ct) =>
        Lookup("genres", db.Genres.OrderBy(x => x.Name).Select(x => new LookupDto(x.Id, x.Name, x.Slug)), ct);

    public Task<IReadOnlyList<LookupDto>> GetPlatformsAsync(CancellationToken ct) =>
        Lookup("platforms", db.Platforms.OrderBy(x => x.Name).Select(x => new LookupDto(x.Id, x.Name, x.Slug)), ct);

    public Task<IReadOnlyList<LookupDto>> GetDevelopersAsync(CancellationToken ct) =>
        Lookup("developers", db.Developers.OrderBy(x => x.Name).Select(x => new LookupDto(x.Id, x.Name, null)), ct);

    public Task<IReadOnlyList<LookupDto>> GetPublishersAsync(CancellationToken ct) =>
        Lookup("publishers", db.Publishers.OrderBy(x => x.Name).Select(x => new LookupDto(x.Id, x.Name, null)), ct);

    private Task<IReadOnlyList<LookupDto>> Lookup(string name, IQueryable<LookupDto> query, CancellationToken ct) =>
        cache.GetOrCreateAsync<IReadOnlyList<LookupDto>>(LookupsCachePrefix + name, TimeSpan.FromMinutes(10),
            async token => await query.AsNoTracking().ToListAsync(token), ct);

    internal static GameCardDto ToCard(GameCardProjection g)
    {
        var cheapest = g.Products
            .Select(p => new { p.Price, Final = Product.ApplyDiscount(p.Price, p.DiscountPercent) })
            .OrderBy(p => p.Final)
            .FirstOrDefault();

        return new GameCardDto(g.Id, g.Slug, g.Title, g.CoverImageUrl, g.HeaderImageUrl, g.Platform, g.Genres,
            cheapest?.Final, cheapest?.Price,
            g.Products.Count == 0 ? 0 : g.Products.Max(p => p.DiscountPercent),
            g.AverageRating, g.RatingsCount, g.Products.Any(p => p.InStock > 0), g.IsFeatured);
    }
}

internal sealed class GameCardProjection
{
    public int Id { get; init; }
    public string Slug { get; init; } = null!;
    public string Title { get; init; } = null!;
    public string? CoverImageUrl { get; init; }
    public string? HeaderImageUrl { get; init; }
    public string Platform { get; init; } = null!;
    public List<string> Genres { get; init; } = [];
    public decimal AverageRating { get; init; }
    public int RatingsCount { get; init; }
    public bool IsFeatured { get; init; }
    public List<ProductPriceProjection> Products { get; init; } = [];
}

internal sealed class ProductPriceProjection
{
    public decimal Price { get; init; }
    public int DiscountPercent { get; init; }
    public int InStock { get; init; }
}

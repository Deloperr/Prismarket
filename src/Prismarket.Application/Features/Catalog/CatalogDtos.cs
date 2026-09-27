using Prismarket.Domain.Enums;

namespace Prismarket.Application.Features.Catalog;

public sealed record LookupDto(int Id, string Name, string? Slug = null);

public sealed record ProductDto(
    int Id, ProductKind Kind, string Edition, decimal Price, int DiscountPercent, decimal FinalPrice,
    bool IsAvailable, int InStock);

public sealed record GameCardDto(
    int Id, string Slug, string Title, string? CoverImageUrl, string? HeaderImageUrl,
    string Platform, IReadOnlyList<string> Genres,
    decimal? Price, decimal? OriginalPrice, int MaxDiscount,
    decimal AverageRating, int RatingsCount, bool InStock, bool IsFeatured);

public sealed record GameDetailDto(
    int Id, string Slug, string Title, string? ShortDescription, string? Description,
    DateOnly? ReleaseDate, string? AgeRating, string? SystemRequirements,
    string? CoverImageUrl, string? HeaderImageUrl, string? TrailerUrl, bool IsAvailable, bool IsFeatured,
    LookupDto Developer, LookupDto Publisher, LookupDto Platform, IReadOnlyList<LookupDto> Genres,
    IReadOnlyList<ProductDto> Products, decimal AverageRating, int RatingsCount, int[] RatingDistribution,
    int SalesCount, bool InWishlist, bool Owned, ActivePromotionDto? Promotion);

public sealed record ActivePromotionDto(int Id, string Name, int DiscountPercent, DateTime EndsAt);

public sealed record GameQuery(
    string? Search = null,
    int? GenreId = null,
    int? PlatformId = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    bool? OnSale = null,
    bool? InStock = null,
    string? Sort = null,
    int Page = 1,
    int PageSize = 24);

public sealed record HomeDto(
    IReadOnlyList<GameCardDto> Featured,
    IReadOnlyList<GameCardDto> NewReleases,
    IReadOnlyList<GameCardDto> TopSellers,
    IReadOnlyList<GameCardDto> OnSale,
    IReadOnlyList<PromotionBannerDto> Promotions,
    StoreStatsDto Stats);

public sealed record PromotionBannerDto(int Id, string Name, string? Description, int DiscountPercent,
    DateTime StartsAt, DateTime EndsAt, string? BannerImageUrl, int ProductsCount);

public sealed record StoreStatsDto(int Games, int KeysDelivered, int Customers, decimal AverageRating);

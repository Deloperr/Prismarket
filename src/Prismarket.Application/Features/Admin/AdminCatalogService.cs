using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Prismarket.Application.Common;
using Prismarket.Application.Features.Catalog;
using Prismarket.Domain.Entities;
using Prismarket.Domain.Enums;

namespace Prismarket.Application.Features.Admin;

public sealed record ProductInput(
    int? Id,
    ProductKind Kind,
    [Required, StringLength(60)] string Edition,
    [Range(0, 100000)] decimal Price,
    [Range(0, 100)] int DiscountPercent,
    bool IsAvailable = true,
    int? LowStockThreshold = null);

public sealed record GameInput(
    [Required, StringLength(200)] string Title,
    [StringLength(300)] string? ShortDescription,
    string? Description,
    DateOnly? ReleaseDate,
    [StringLength(10)] string? AgeRating,
    string? SystemRequirements,
    [StringLength(500)] string? CoverImageUrl,
    [StringLength(500)] string? HeaderImageUrl,
    [StringLength(500)] string? TrailerUrl,
    bool IsAvailable,
    [Range(1, int.MaxValue)] int DeveloperId,
    [Range(1, int.MaxValue)] int PublisherId,
    [Range(1, int.MaxValue)] int PlatformId,
    IReadOnlyList<int> GenreIds,
    IReadOnlyList<ProductInput> Products);

public sealed record AdminProductDto(int Id, ProductKind Kind, string Edition, decimal Price, int DiscountPercent,
    decimal FinalPrice, bool IsAvailable, bool HiddenByStockMonitor, int? LowStockThreshold,
    int Available, int Reserved, int Sold);

public sealed record AdminGameDto(int Id, string Title, string Slug, string? ShortDescription, string? Description,
    DateOnly? ReleaseDate, string? AgeRating, string? SystemRequirements, string? CoverImageUrl,
    string? HeaderImageUrl, string? TrailerUrl, bool IsAvailable, bool IsFeatured, int DeveloperId,
    string Developer, int PublisherId, string Publisher, int PlatformId, string Platform, IReadOnlyList<int> GenreIds,
    IReadOnlyList<string> Genres, IReadOnlyList<AdminProductDto> Products, int SalesCount, decimal AverageRating,
    DateTime CreatedAt);

public sealed record ImportKeysRequest([Required] string Keys);
public sealed record ImportAccountsRequest([Required] string Accounts);
public sealed record ImportResultDto(int Added, int Duplicates, int Invalid);

public sealed record KeyDto(int Id, string MaskedValue, StockItemStatus Status, DateTime CreatedAt, DateTime? SoldAt);

public sealed record LookupInput([Required, StringLength(100)] string Name, string? Website, string? Description);

public interface IAdminCatalogService
{
    Task<PagedResult<AdminGameDto>> ListGamesAsync(string? search, PageRequest page, CancellationToken ct);
    Task<AdminGameDto> GetGameAsync(int id, CancellationToken ct);
    Task<AdminGameDto> CreateGameAsync(GameInput input, CancellationToken ct);
    Task<AdminGameDto> UpdateGameAsync(int id, GameInput input, CancellationToken ct);
    Task DeleteGameAsync(int id, CancellationToken ct);
    Task<AdminGameDto> UploadImageAsync(int id, string kind, Stream content, string fileName, CancellationToken ct);

    Task<ImportResultDto> ImportKeysAsync(int productId, ImportKeysRequest request, CancellationToken ct);
    Task<ImportResultDto> ImportAccountsAsync(int productId, ImportAccountsRequest request, CancellationToken ct);
    Task<ImportResultDto> GenerateDemoKeysAsync(int productId, int count, CancellationToken ct);
    Task<PagedResult<KeyDto>> ListKeysAsync(int productId, StockItemStatus? status, PageRequest page, CancellationToken ct);
    Task DeleteKeyAsync(int keyId, CancellationToken ct);

    Task<LookupDto> CreateLookupAsync(string type, LookupInput input, CancellationToken ct);
}

public sealed class AdminCatalogService(IAppDbContext db, IFileStorage files, ICacheService cache)
    : IAdminCatalogService
{
    public async Task<PagedResult<AdminGameDto>> ListGamesAsync(string? search, PageRequest page, CancellationToken ct)
    {
        var query = GamesQuery();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(g => EF.Functions.Like(g.Title.ToLower(), $"%{search.Trim().ToLowerInvariant()}%"));
        var result = await query.OrderByDescending(g => g.CreatedAt).ToPagedAsync(page, ct);
        var stock = await StockAsync(result.Items.SelectMany(g => g.Products).Select(p => p.Id).ToList(), ct);
        return new PagedResult<AdminGameDto>(result.Items.Select(g => ToDto(g, stock)).ToList(), result.Page,
            result.PageSize, result.TotalCount);
    }

    public async Task<AdminGameDto> GetGameAsync(int id, CancellationToken ct)
    {
        var game = await GamesQuery().FirstOrDefaultAsync(g => g.Id == id, ct) ?? throw NotFoundException.For("Игра", id);
        var stock = await StockAsync(game.Products.Select(p => p.Id).ToList(), ct);
        return ToDto(game, stock);
    }

    public async Task<AdminGameDto> CreateGameAsync(GameInput input, CancellationToken ct)
    {
        var game = new Game();
        await ApplyAsync(game, input, ct);
        db.Games.Add(game);
        await db.SaveChangesAsync(ct);
        await InvalidateAsync(ct);
        return await GetGameAsync(game.Id, ct);
    }

    public async Task<AdminGameDto> UpdateGameAsync(int id, GameInput input, CancellationToken ct)
    {
        var game = await db.Games.Include(g => g.Genres).Include(g => g.Products)
                       .FirstOrDefaultAsync(g => g.Id == id, ct) ?? throw NotFoundException.For("Игра", id);
        await ApplyAsync(game, input, ct);
        await db.SaveChangesAsync(ct);
        await InvalidateAsync(ct);
        return await GetGameAsync(id, ct);
    }

    public async Task DeleteGameAsync(int id, CancellationToken ct)
    {
        var game = await db.Games.FirstOrDefaultAsync(g => g.Id == id, ct) ?? throw NotFoundException.For("Игра", id);
        var hasSales = await db.OrderItems.AnyAsync(i => i.Product.GameId == id, ct);
        if (hasSales)
        {
            // Keep history consistent: games with orders are only hidden.
            game.IsAvailable = false;
        }
        else
        {
            db.Games.Remove(game);
        }
        await db.SaveChangesAsync(ct);
        await InvalidateAsync(ct);
    }

    public async Task<AdminGameDto> UploadImageAsync(int id, string kind, Stream content, string fileName,
        CancellationToken ct)
    {
        var game = await db.Games.FirstOrDefaultAsync(g => g.Id == id, ct) ?? throw NotFoundException.For("Игра", id);
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".webp"))
            throw new BusinessRuleException("Допустимые форматы: PNG, JPG, WEBP.");

        var url = await files.SaveAsync(content, $"{game.Slug}-{kind}{ext}", "games", ct);
        if (kind == "cover") game.CoverImageUrl = url; else game.HeaderImageUrl = url;
        await db.SaveChangesAsync(ct);
        await InvalidateAsync(ct);
        return await GetGameAsync(id, ct);
    }

    public async Task<ImportResultDto> ImportKeysAsync(int productId, ImportKeysRequest request, CancellationToken ct)
    {
        await EnsureProductAsync(productId, ct);
        var candidates = request.Keys
            .Split(['\n', '\r', ';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        var valid = candidates.Where(k => k.Length is >= 5 and <= 100).Distinct(StringComparer.Ordinal).ToList();
        var invalid = candidates.Count - candidates.Count(k => k.Length is >= 5 and <= 100);

        var existing = await db.ProductKeys.Where(k => valid.Contains(k.Value)).Select(k => k.Value).ToListAsync(ct);
        var toAdd = valid.Except(existing).ToList();
        db.ProductKeys.AddRange(toAdd.Select(v => new ProductKey { ProductId = productId, Value = v }));
        await db.SaveChangesAsync(ct);
        await InvalidateAsync(ct);

        return new ImportResultDto(toAdd.Count, candidates.Count - invalid - toAdd.Count, invalid);
    }

    /// <summary>Format: one account per line — login:password[:email[:extra info]]</summary>
    public async Task<ImportResultDto> ImportAccountsAsync(int productId, ImportAccountsRequest request,
        CancellationToken ct)
    {
        await EnsureProductAsync(productId, ct);
        var lines = request.Accounts.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var added = 0;
        var invalid = 0;
        foreach (var line in lines)
        {
            var parts = line.Split(':', 4, StringSplitOptions.TrimEntries);
            if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0) { invalid++; continue; }
            db.ProductAccounts.Add(new ProductAccount
            {
                ProductId = productId, Login = parts[0], Password = parts[1],
                Email = parts.Length > 2 ? parts[2] : null,
                AdditionalInfo = parts.Length > 3 ? parts[3] : null
            });
            added++;
        }
        await db.SaveChangesAsync(ct);
        await InvalidateAsync(ct);
        return new ImportResultDto(added, 0, invalid);
    }

    public async Task<ImportResultDto> GenerateDemoKeysAsync(int productId, int count, CancellationToken ct)
    {
        count = Math.Clamp(count, 1, 500);
        var keys = Enumerable.Range(0, count).Select(_ => KeyGenerator.Next()).ToList();
        return await ImportKeysAsync(productId, new ImportKeysRequest(string.Join('\n', keys)), ct);
    }

    public async Task<PagedResult<KeyDto>> ListKeysAsync(int productId, StockItemStatus? status, PageRequest page,
        CancellationToken ct)
    {
        var query = db.ProductKeys.AsNoTracking().Where(k => k.ProductId == productId);
        if (status is not null) query = query.Where(k => k.Status == status);
        var result = await query.OrderByDescending(k => k.Id)
            .Select(k => new { k.Id, k.Value, k.Status, k.CreatedAt, k.SoldAt })
            .ToPagedAsync(page, ct);
        return new PagedResult<KeyDto>(
            result.Items.Select(k => new KeyDto(k.Id, Mask(k.Value), k.Status, k.CreatedAt, k.SoldAt)).ToList(),
            result.Page, result.PageSize, result.TotalCount);
    }

    public async Task DeleteKeyAsync(int keyId, CancellationToken ct)
    {
        var deleted = await db.ProductKeys.Where(k => k.Id == keyId && k.Status == StockItemStatus.Available)
            .ExecuteDeleteAsync(ct);
        if (deleted == 0) throw new BusinessRuleException("Удалить можно только свободный ключ.");
    }

    public async Task<LookupDto> CreateLookupAsync(string type, LookupInput input, CancellationToken ct)
    {
        var name = input.Name.Trim();
        LookupDto result;
        switch (type)
        {
            case "genres":
                if (await db.Genres.AnyAsync(x => x.Name == name, ct)) throw new ConflictException("Такой жанр уже есть.");
                var genre = new Genre { Name = name, Slug = Slug.From(name), Description = input.Description };
                db.Genres.Add(genre);
                await db.SaveChangesAsync(ct);
                result = new LookupDto(genre.Id, genre.Name, genre.Slug);
                break;
            case "platforms":
                if (await db.Platforms.AnyAsync(x => x.Name == name, ct)) throw new ConflictException("Такая платформа уже есть.");
                var platform = new Platform { Name = name, Slug = Slug.From(name), Website = input.Website };
                db.Platforms.Add(platform);
                await db.SaveChangesAsync(ct);
                result = new LookupDto(platform.Id, platform.Name, platform.Slug);
                break;
            case "developers":
                if (await db.Developers.AnyAsync(x => x.Name == name, ct)) throw new ConflictException("Такой разработчик уже есть.");
                var developer = new Developer { Name = name, Website = input.Website, Description = input.Description };
                db.Developers.Add(developer);
                await db.SaveChangesAsync(ct);
                result = new LookupDto(developer.Id, developer.Name);
                break;
            case "publishers":
                if (await db.Publishers.AnyAsync(x => x.Name == name, ct)) throw new ConflictException("Такой издатель уже есть.");
                var publisher = new Publisher { Name = name, Website = input.Website };
                db.Publishers.Add(publisher);
                await db.SaveChangesAsync(ct);
                result = new LookupDto(publisher.Id, publisher.Name);
                break;
            default:
                throw new BusinessRuleException("Неизвестный справочник.");
        }
        await cache.RemoveAsync(CatalogService.LookupsCachePrefix + type, ct);
        return result;
    }

    private async Task ApplyAsync(Game game, GameInput input, CancellationToken ct)
    {
        if (!await db.Developers.AnyAsync(d => d.Id == input.DeveloperId, ct)) throw NotFoundException.For("Разработчик", input.DeveloperId);
        if (!await db.Publishers.AnyAsync(d => d.Id == input.PublisherId, ct)) throw NotFoundException.For("Издатель", input.PublisherId);
        if (!await db.Platforms.AnyAsync(d => d.Id == input.PlatformId, ct)) throw NotFoundException.For("Платформа", input.PlatformId);
        if (input.Products.Count == 0) throw new BusinessRuleException("Добавьте хотя бы одно издание (товар).");

        var slug = Slug.From(input.Title);
        if (string.IsNullOrEmpty(slug)) slug = $"game-{Guid.NewGuid():N}"[..13];
        if (await db.Games.AnyAsync(g => g.Slug == slug && g.Id != game.Id, ct)) slug = $"{slug}-{Random.Shared.Next(100, 999)}";

        game.Title = input.Title.Trim();
        game.Slug = slug;
        game.ShortDescription = input.ShortDescription;
        game.Description = input.Description;
        game.ReleaseDate = input.ReleaseDate;
        game.AgeRating = input.AgeRating;
        game.SystemRequirements = input.SystemRequirements;
        game.CoverImageUrl = input.CoverImageUrl;
        game.HeaderImageUrl = input.HeaderImageUrl;
        game.TrailerUrl = input.TrailerUrl;
        game.IsAvailable = input.IsAvailable;
        game.DeveloperId = input.DeveloperId;
        game.PublisherId = input.PublisherId;
        game.PlatformId = input.PlatformId;

        var genres = await db.Genres.Where(x => input.GenreIds.Contains(x.Id)).ToListAsync(ct);
        game.Genres.Clear();
        foreach (var genre in genres) game.Genres.Add(genre);

        foreach (var p in input.Products)
        {
            var product = p.Id is null ? null : game.Products.FirstOrDefault(x => x.Id == p.Id);
            if (product is null)
            {
                product = new Product();
                game.Products.Add(product);
            }
            product.Kind = p.Kind;
            product.Edition = p.Edition.Trim();
            product.Price = p.Price;
            product.DiscountPercent = p.DiscountPercent;
            product.IsAvailable = p.IsAvailable;
            product.LowStockThreshold = p.LowStockThreshold;
            if (p.IsAvailable) product.HiddenByStockMonitor = false;
        }

        // Editions removed in the form are hidden (they may have sales history).
        var keptIds = input.Products.Where(p => p.Id is not null).Select(p => p.Id!.Value).ToHashSet();
        foreach (var removed in game.Products.Where(p => p.Id != 0 && !keptIds.Contains(p.Id)))
            removed.IsAvailable = false;
    }

    private IQueryable<Game> GamesQuery() => db.Games.AsNoTracking()
        .Include(g => g.Developer).Include(g => g.Publisher).Include(g => g.Platform)
        .Include(g => g.Genres).Include(g => g.Products)
        .AsSplitQuery();

    private async Task<Dictionary<int, (int Available, int Reserved, int Sold)>> StockAsync(List<int> productIds,
        CancellationToken ct)
    {
        var keys = await db.ProductKeys.Where(k => productIds.Contains(k.ProductId))
            .GroupBy(k => new { k.ProductId, k.Status })
            .Select(g => new { g.Key.ProductId, g.Key.Status, Count = g.Count() }).ToListAsync(ct);
        var accounts = await db.ProductAccounts.Where(k => productIds.Contains(k.ProductId))
            .GroupBy(k => new { k.ProductId, k.Status })
            .Select(g => new { g.Key.ProductId, g.Key.Status, Count = g.Count() }).ToListAsync(ct);
        var all = keys.Concat(accounts).ToList();

        return productIds.ToDictionary(id => id, id => (
            all.Where(x => x.ProductId == id && x.Status == StockItemStatus.Available).Sum(x => x.Count),
            all.Where(x => x.ProductId == id && x.Status == StockItemStatus.Reserved).Sum(x => x.Count),
            all.Where(x => x.ProductId == id && x.Status == StockItemStatus.Sold).Sum(x => x.Count)));
    }

    private static AdminGameDto ToDto(Game g, Dictionary<int, (int Available, int Reserved, int Sold)> stock) =>
        new(g.Id, g.Title, g.Slug, g.ShortDescription, g.Description, g.ReleaseDate, g.AgeRating, g.SystemRequirements,
            g.CoverImageUrl, g.HeaderImageUrl, g.TrailerUrl, g.IsAvailable, g.IsFeatured, g.DeveloperId,
            g.Developer.Name, g.PublisherId, g.Publisher.Name, g.PlatformId, g.Platform.Name,
            g.Genres.Select(x => x.Id).ToList(), g.Genres.Select(x => x.Name).ToList(),
            g.Products.OrderBy(p => p.Price).Select(p =>
            {
                var s = stock.GetValueOrDefault(p.Id);
                return new AdminProductDto(p.Id, p.Kind, p.Edition, p.Price, p.DiscountPercent, p.FinalPrice,
                    p.IsAvailable, p.HiddenByStockMonitor, p.LowStockThreshold, s.Available, s.Reserved, s.Sold);
            }).ToList(),
            g.SalesCount, g.AverageRating, g.CreatedAt);

    private async Task EnsureProductAsync(int productId, CancellationToken ct)
    {
        if (!await db.Products.AnyAsync(p => p.Id == productId, ct)) throw NotFoundException.For("Товар", productId);
    }

    private async Task InvalidateAsync(CancellationToken ct) => await cache.RemoveAsync(CatalogService.HomeCacheKey, ct);

    private static string Mask(string value) =>
        value.Length <= 8 ? new string('•', value.Length) : $"{value[..4]}•••••{value[^4..]}";
}

public static class KeyGenerator
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>Generates a Steam-like key: XXXXX-XXXXX-XXXXX.</summary>
    public static string Next() => string.Join('-', Enumerable.Range(0, 3).Select(_ =>
        new string(Enumerable.Range(0, 5)
            .Select(_ => Alphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(Alphabet.Length)])
            .ToArray())));
}

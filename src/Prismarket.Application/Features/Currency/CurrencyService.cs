using Microsoft.EntityFrameworkCore;
using Prismarket.Application.Common;

namespace Prismarket.Application.Features.Currency;

public sealed record CurrencyRateDto(string Code, decimal RateToByn, DateTime UpdatedAt, string Source);

public interface ICurrencyService
{
    Task<IReadOnlyList<CurrencyRateDto>> GetRatesAsync(CancellationToken ct);
}

public sealed class CurrencyService(IAppDbContext db, ICacheService cache) : ICurrencyService
{
    public const string CacheKey = "currency:rates";

    public Task<IReadOnlyList<CurrencyRateDto>> GetRatesAsync(CancellationToken ct) =>
        cache.GetOrCreateAsync<IReadOnlyList<CurrencyRateDto>>(CacheKey, TimeSpan.FromMinutes(30), async token =>
        {
            var rates = await db.CurrencyRates.AsNoTracking().OrderBy(r => r.Code)
                .Select(r => new CurrencyRateDto(r.Code, r.RateToByn, r.UpdatedAt, r.Source))
                .ToListAsync(token);
            return [new CurrencyRateDto("BYN", 1m, DateTime.UtcNow, "base"), .. rates];
        }, ct);
}

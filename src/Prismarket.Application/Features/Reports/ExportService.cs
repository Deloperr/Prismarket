using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Prismarket.Application.Common;
using Prismarket.Domain.Enums;

namespace Prismarket.Application.Features.Reports;

public enum ExportFormat { Json, Csv, Xml }

public interface IExportService
{
    Task<ReportFile> ExportGamesAsync(ExportFormat format, CancellationToken ct);
    Task<ReportFile> ExportOrdersAsync(ExportFormat format, DateTime? from, DateTime? to, CancellationToken ct);
}

public sealed class ExportService(IAppDbContext db, TimeProvider clock) : IExportService
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public async Task<ReportFile> ExportGamesAsync(ExportFormat format, CancellationToken ct)
    {
        var rows = await db.Games.AsNoTracking().OrderBy(g => g.Title)
            .SelectMany(g => g.Products, (g, p) => new
            {
                GameId = g.Id, g.Title, Platform = g.Platform.Name, Developer = g.Developer.Name,
                Publisher = g.Publisher.Name, g.ReleaseDate, ProductId = p.Id, p.Edition, p.Kind, p.Price,
                p.DiscountPercent, p.IsAvailable,
                KeysAvailable = p.Keys.Count(k => k.Status == StockItemStatus.Available),
                g.SalesCount, g.AverageRating
            })
            .ToListAsync(ct);

        var table = rows.Select(r => new Dictionary<string, object?>
        {
            ["game_id"] = r.GameId, ["title"] = r.Title, ["platform"] = r.Platform, ["developer"] = r.Developer,
            ["publisher"] = r.Publisher, ["release_date"] = r.ReleaseDate, ["product_id"] = r.ProductId,
            ["edition"] = r.Edition, ["kind"] = r.Kind.ToString(), ["price"] = r.Price,
            ["discount_percent"] = r.DiscountPercent, ["is_available"] = r.IsAvailable,
            ["keys_available"] = r.KeysAvailable, ["sales"] = r.SalesCount, ["rating"] = r.AverageRating
        }).ToList();
        return Build("games", "game", table, format);
    }

    public async Task<ReportFile> ExportOrdersAsync(ExportFormat format, DateTime? from, DateTime? to,
        CancellationToken ct)
    {
        var query = db.Orders.AsNoTracking();
        if (from is not null) query = query.Where(o => o.CreatedAt >= from);
        if (to is not null) query = query.Where(o => o.CreatedAt < to);

        var rows = await query.OrderByDescending(o => o.CreatedAt)
            .Select(o => new
            {
                o.Number, o.CreatedAt, o.User.Username, o.User.Email, o.Status, o.PaymentMethod,
                Items = o.Items.Count, o.Subtotal, o.DiscountAmount, o.Total, o.PaidAt
            })
            .ToListAsync(ct);

        var table = rows.Select(o => new Dictionary<string, object?>
        {
            ["number"] = o.Number, ["created_at"] = o.CreatedAt, ["customer"] = o.Username, ["email"] = o.Email,
            ["status"] = o.Status.ToString(), ["payment_method"] = o.PaymentMethod.ToString(), ["items"] = o.Items,
            ["subtotal"] = o.Subtotal, ["discount"] = o.DiscountAmount, ["total"] = o.Total, ["paid_at"] = o.PaidAt
        }).ToList();
        return Build("orders", "order", table, format);
    }

    private ReportFile Build(string name, string itemName, List<Dictionary<string, object?>> rows, ExportFormat format)
    {
        var stamp = clock.GetUtcNow().ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
        return format switch
        {
            ExportFormat.Json => new ReportFile($"{name}-{stamp}.json", "application/json",
                JsonSerializer.SerializeToUtf8Bytes(rows, Json)),
            ExportFormat.Csv => new ReportFile($"{name}-{stamp}.csv", "text/csv", ToCsv(rows)),
            ExportFormat.Xml => new ReportFile($"{name}-{stamp}.xml", "application/xml", ToXml(name, itemName, rows)),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }

    internal static byte[] ToCsv(List<Dictionary<string, object?>> rows)
    {
        var sb = new StringBuilder();
        if (rows.Count > 0)
        {
            sb.AppendLine(string.Join(';', rows[0].Keys));
            foreach (var row in rows)
                sb.AppendLine(string.Join(';', row.Values.Select(v => Escape(Format(v)))));
        }
        // UTF-8 BOM so Excel opens Cyrillic correctly.
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    private static byte[] ToXml(string root, string item, List<Dictionary<string, object?>> rows)
    {
        var doc = new XDocument(new XElement(root,
            rows.Select(r => new XElement(item, r.Select(kv => new XElement(kv.Key, Format(kv.Value)))))));
        return Encoding.UTF8.GetBytes(doc.ToString());
    }

    private static string Format(object? value) => value switch
    {
        null => "",
        DateTime d => d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        decimal m => m.ToString("0.00", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    private static string Escape(string value) =>
        value.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}

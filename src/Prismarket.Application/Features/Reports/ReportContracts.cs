namespace Prismarket.Application.Features.Reports;

public sealed record DailySalesRow(DateOnly Day, int Orders, int Items, decimal Revenue);
public sealed record TopGameRow(int GameId, string Title, int Sold, decimal Revenue);
public sealed record PaymentMethodRow(string Method, int Orders, decimal Revenue);
public sealed record GenreRevenueRow(string Genre, decimal Revenue);
public sealed record LowStockRow(int ProductId, string GameTitle, string Edition, int Available, bool IsAvailable);
public sealed record RecentOrderRow(string Number, string Username, decimal Total, string Status, string Method, DateTime CreatedAt);

public sealed record DashboardDto(
    decimal RevenueToday,
    decimal Revenue7d,
    decimal Revenue30d,
    decimal RevenuePrev30d,
    int OrdersToday,
    int Orders30d,
    decimal AverageCheck30d,
    int NewUsers30d,
    int TotalUsers,
    int KeysInStock,
    int OpenTickets,
    int PendingReviews,
    IReadOnlyList<DailySalesRow> Daily,
    IReadOnlyList<TopGameRow> TopGames,
    IReadOnlyList<PaymentMethodRow> PaymentMethods,
    IReadOnlyList<GenreRevenueRow> Genres,
    IReadOnlyList<LowStockRow> LowStock,
    IReadOnlyList<RecentOrderRow> RecentOrders);

public sealed record SalesReportData(
    DateTime From,
    DateTime To,
    decimal Revenue,
    int Orders,
    int ItemsSold,
    decimal AverageCheck,
    decimal Cashback,
    IReadOnlyList<DailySalesRow> Daily,
    IReadOnlyList<TopGameRow> TopGames,
    IReadOnlyList<PaymentMethodRow> PaymentMethods,
    IReadOnlyList<RecentOrderRow> OrderRows);

/// <summary>Read-side queries written in raw SQL with Dapper (fast aggregates, no change tracking).</summary>
public interface IAnalyticsQueries
{
    Task<DashboardDto> GetDashboardAsync(CancellationToken ct);
    Task<SalesReportData> GetSalesReportAsync(DateTime from, DateTime to, CancellationToken ct);
}

public sealed record ReportFile(string FileName, string ContentType, byte[] Content);

public enum ReportFormat { Pdf, Excel, Word }

/// <summary>Strategy per output format (PDF via QuestPDF, Excel via ClosedXML, Word via OpenXML).</summary>
public interface IReportRenderer
{
    ReportFormat Format { get; }
    ReportFile Render(SalesReportData data);
}

public interface IReportService
{
    Task<ReportFile> BuildSalesReportAsync(DateTime from, DateTime to, ReportFormat format, CancellationToken ct);
}

public sealed class ReportService(IAnalyticsQueries analytics, IEnumerable<IReportRenderer> renderers) : IReportService
{
    public async Task<ReportFile> BuildSalesReportAsync(DateTime from, DateTime to, ReportFormat format,
        CancellationToken ct)
    {
        var renderer = renderers.FirstOrDefault(r => r.Format == format)
                       ?? throw new NotSupportedException($"Format {format} is not supported.");
        var data = await analytics.GetSalesReportAsync(from, to, ct);
        return renderer.Render(data);
    }
}

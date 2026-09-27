using Dapper;
using Npgsql;
using Prismarket.Application.Features.Reports;

namespace Prismarket.Infrastructure.Analytics;

/// <summary>
/// Read model for dashboards and reports written in plain SQL with Dapper.
/// Independent queries are executed concurrently (<see cref="Task.WhenAll(Task[])"/>), each on its own
/// pooled connection from <see cref="NpgsqlDataSource"/>.
/// </summary>
public sealed class AnalyticsQueries(NpgsqlDataSource dataSource, TimeProvider clock) : IAnalyticsQueries
{
    private const string Completed = "'Completed'";

    public async Task<DashboardDto> GetDashboardAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var today = now.Date;
        var from30 = today.AddDays(-29);
        var p = new { today = Utc(today), d7 = Utc(today.AddDays(-6)), from30 = Utc(from30), prev30 = Utc(from30.AddDays(-30)), now = Utc(now) };

        var totalsTask = QuerySingleAsync<TotalsRow>($"""
            SELECT
              COALESCE(SUM(total) FILTER (WHERE completed_at >= @today), 0)                        AS revenue_today,
              COALESCE(SUM(total) FILTER (WHERE completed_at >= @d7), 0)                           AS revenue_7d,
              COALESCE(SUM(total) FILTER (WHERE completed_at >= @from30), 0)                       AS revenue_30d,
              COALESCE(SUM(total) FILTER (WHERE completed_at >= @prev30 AND completed_at < @from30), 0) AS revenue_prev_30d,
              COUNT(*) FILTER (WHERE completed_at >= @today)::int                                  AS orders_today,
              COUNT(*) FILTER (WHERE completed_at >= @from30)::int                                 AS orders_30d,
              COALESCE(AVG(total) FILTER (WHERE completed_at >= @from30), 0)                       AS average_check_30d
            FROM orders WHERE status = {Completed}
            """, p, ct);

        var countersTask = QuerySingleAsync<CountersRow>("""
            SELECT
              (SELECT COUNT(*) FROM users WHERE created_at >= @from30)::int                AS new_users_30d,
              (SELECT COUNT(*) FROM users)::int                                            AS total_users,
              (SELECT COUNT(*) FROM product_keys WHERE status = 'Available')::int          AS keys_in_stock,
              (SELECT COUNT(*) FROM support_tickets WHERE status <> 'Closed')::int          AS open_tickets,
              (SELECT COUNT(*) FROM reviews WHERE status = 'PendingModeration')::int       AS pending_reviews
            """, p, ct);

        var dailyTask = DailyAsync(from30, today.AddDays(1), ct);
        var topTask = TopGamesAsync(from30, now, 5, ct);
        var methodsTask = PaymentMethodsAsync(from30, now, ct);

        var genresTask = QueryAsync<GenreRow>($"""
            SELECT gr.name AS genre, COALESCE(SUM(oi.final_price), 0) AS revenue
            FROM order_items oi
              JOIN orders o ON o.id = oi.order_id AND o.status = {Completed} AND o.completed_at >= @from30
              JOIN products pr ON pr.id = oi.product_id
              JOIN game_genres gg ON gg.games_id = pr.game_id
              JOIN genres gr ON gr.id = gg.genres_id
            GROUP BY gr.name ORDER BY revenue DESC LIMIT 8
            """, p, ct);

        var lowStockTask = QueryAsync<LowStockDbRow>("""
            SELECT pr.id AS product_id, g.title AS game_title, pr.edition,
                   (SELECT COUNT(*) FROM product_keys k WHERE k.product_id = pr.id AND k.status = 'Available')::int
                 + (SELECT COUNT(*) FROM product_accounts a WHERE a.product_id = pr.id AND a.status = 'Available')::int AS available,
                   pr.is_available
            FROM products pr JOIN games g ON g.id = pr.game_id
            WHERE g.is_available AND (pr.is_available OR pr.hidden_by_stock_monitor)
            ORDER BY available, g.title
            LIMIT 8
            """, p, ct);

        var recentTask = RecentOrdersAsync(null, null, 8, ct);

        await Task.WhenAll(totalsTask, countersTask, dailyTask, topTask, methodsTask, genresTask, lowStockTask, recentTask);

        var t = totalsTask.Result;
        var c = countersTask.Result;
        return new DashboardDto(t.revenue_today, t.revenue_7d, t.revenue_30d, t.revenue_prev_30d, t.orders_today,
            t.orders_30d, Math.Round(t.average_check_30d, 2), c.new_users_30d, c.total_users, c.keys_in_stock,
            c.open_tickets, c.pending_reviews, dailyTask.Result, topTask.Result, methodsTask.Result,
            genresTask.Result.Select(g => new GenreRevenueRow(g.genre, g.revenue)).ToList(),
            lowStockTask.Result.Select(r => new LowStockRow(r.product_id, r.game_title, r.edition, r.available, r.is_available)).ToList(),
            recentTask.Result);
    }

    public async Task<SalesReportData> GetSalesReportAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        var p = new { from = Utc(from), to = Utc(to) };
        var totalsTask = QuerySingleAsync<ReportTotalsRow>($"""
            SELECT COALESCE(SUM(o.total), 0) AS revenue,
                   COUNT(*)::int AS orders,
                   COALESCE((SELECT COUNT(*) FROM order_items oi JOIN orders o2 ON o2.id = oi.order_id
                             WHERE o2.status = {Completed} AND o2.completed_at >= @from AND o2.completed_at < @to), 0)::int AS items_sold,
                   COALESCE(AVG(o.total), 0) AS average_check,
                   COALESCE((SELECT SUM(amount) FROM balance_transactions
                             WHERE type = 'Cashback' AND created_at >= @from AND created_at < @to), 0) AS cashback
            FROM orders o
            WHERE o.status = {Completed} AND o.completed_at >= @from AND o.completed_at < @to
            """, p, ct);
        var dailyTask = DailyAsync(from, to, ct);
        var topTask = TopGamesAsync(from, to, 10, ct);
        var methodsTask = PaymentMethodsAsync(from, to, ct);
        var ordersTask = RecentOrdersAsync(from, to, 200, ct);

        await Task.WhenAll(totalsTask, dailyTask, topTask, methodsTask, ordersTask);
        var t = totalsTask.Result;
        return new SalesReportData(from, to, t.revenue, t.orders, t.items_sold, Math.Round(t.average_check, 2),
            t.cashback, dailyTask.Result, topTask.Result, methodsTask.Result, ordersTask.Result);
    }

    private async Task<IReadOnlyList<DailySalesRow>> DailyAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        var rows = await QueryAsync<DailyDbRow>($"""
            SELECT d::date AS day,
                   (SELECT COUNT(*) FROM orders o
                     WHERE o.status = {Completed} AND o.completed_at >= d AND o.completed_at < d + interval '1 day')::int AS orders,
                   (SELECT COUNT(*) FROM order_items oi JOIN orders o ON o.id = oi.order_id
                     WHERE o.status = {Completed} AND o.completed_at >= d AND o.completed_at < d + interval '1 day')::int AS items,
                   (SELECT COALESCE(SUM(o.total), 0) FROM orders o
                     WHERE o.status = {Completed} AND o.completed_at >= d AND o.completed_at < d + interval '1 day') AS revenue
            FROM generate_series(@from::date, (@to::timestamp - interval '1 day')::date, interval '1 day') AS d
            ORDER BY d
            """, new { from = Utc(from), to = Utc(to) }, ct);
        return rows.Select(r => new DailySalesRow(DateOnly.FromDateTime(r.day), r.orders, r.items, Math.Max(0, r.revenue)))
            .ToList();
    }

    private async Task<IReadOnlyList<TopGameRow>> TopGamesAsync(DateTime from, DateTime to, int limit, CancellationToken ct)
    {
        var rows = await QueryAsync<TopGameDbRow>($"""
            SELECT g.id AS game_id, g.title, COUNT(*)::int AS sold, SUM(oi.final_price) AS revenue
            FROM order_items oi
              JOIN orders o ON o.id = oi.order_id
              JOIN products pr ON pr.id = oi.product_id
              JOIN games g ON g.id = pr.game_id
            WHERE o.status = {Completed} AND o.completed_at >= @from AND o.completed_at < @to
            GROUP BY g.id, g.title
            ORDER BY sold DESC, revenue DESC
            LIMIT @limit
            """, new { from = Utc(from), to = Utc(to), limit }, ct);
        return rows.Select(r => new TopGameRow(r.game_id, r.title, r.sold, r.revenue)).ToList();
    }

    private async Task<IReadOnlyList<PaymentMethodRow>> PaymentMethodsAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        var rows = await QueryAsync<MethodDbRow>($"""
            SELECT payment_method AS method, COUNT(*)::int AS orders, COALESCE(SUM(total), 0) AS revenue
            FROM orders
            WHERE status = {Completed} AND completed_at >= @from AND completed_at < @to
            GROUP BY payment_method ORDER BY revenue DESC
            """, new { from = Utc(from), to = Utc(to) }, ct);
        return rows.Select(r => new PaymentMethodRow(r.method, r.orders, r.revenue)).ToList();
    }

    private async Task<IReadOnlyList<RecentOrderRow>> RecentOrdersAsync(DateTime? from, DateTime? to, int limit,
        CancellationToken ct)
    {
        var rows = await QueryAsync<RecentDbRow>("""
            SELECT o.number, u.username, o.total, o.status, o.payment_method AS method, o.created_at
            FROM orders o JOIN users u ON u.id = o.user_id
            WHERE (@from::timestamptz IS NULL OR o.created_at >= @from) AND (@to::timestamptz IS NULL OR o.created_at < @to)
            ORDER BY o.created_at DESC
            LIMIT @limit
            """, new { from = from is null ? (DateTimeOffset?)null : Utc(from.Value), to = to is null ? (DateTimeOffset?)null : Utc(to.Value), limit }, ct);
        return rows.Select(r => new RecentOrderRow(r.number, r.username, r.total, r.status, r.method, r.created_at)).ToList();
    }

    /// <summary>Dapper maps DateTimeOffset to timestamptz unambiguously (DateTime kinds are a common Npgsql pitfall).</summary>
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero);

    private async Task<T> QuerySingleAsync<T>(string sql, object param, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        return await connection.QuerySingleAsync<T>(new CommandDefinition(sql, param, cancellationToken: ct));
    }

    private async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object param, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        return (await connection.QueryAsync<T>(new CommandDefinition(sql, param, cancellationToken: ct))).AsList();
    }

    // Dapper row types (snake_case to match SQL aliases).
#pragma warning disable IDE1006
    private sealed class TotalsRow
    {
        public decimal revenue_today { get; set; }
        public decimal revenue_7d { get; set; }
        public decimal revenue_30d { get; set; }
        public decimal revenue_prev_30d { get; set; }
        public int orders_today { get; set; }
        public int orders_30d { get; set; }
        public decimal average_check_30d { get; set; }
    }

    private sealed class CountersRow
    {
        public int new_users_30d { get; set; }
        public int total_users { get; set; }
        public int keys_in_stock { get; set; }
        public int open_tickets { get; set; }
        public int pending_reviews { get; set; }
    }

    private sealed class ReportTotalsRow
    {
        public decimal revenue { get; set; }
        public int orders { get; set; }
        public int items_sold { get; set; }
        public decimal average_check { get; set; }
        public decimal cashback { get; set; }
    }

    private sealed class DailyDbRow
    {
        public DateTime day { get; set; }
        public int orders { get; set; }
        public int items { get; set; }
        public decimal revenue { get; set; }
    }

    private sealed class TopGameDbRow
    {
        public int game_id { get; set; }
        public string title { get; set; } = "";
        public int sold { get; set; }
        public decimal revenue { get; set; }
    }

    private sealed class MethodDbRow
    {
        public string method { get; set; } = "";
        public int orders { get; set; }
        public decimal revenue { get; set; }
    }

    private sealed class GenreRow
    {
        public string genre { get; set; } = "";
        public decimal revenue { get; set; }
    }

    private sealed class LowStockDbRow
    {
        public int product_id { get; set; }
        public string game_title { get; set; } = "";
        public string edition { get; set; } = "";
        public int available { get; set; }
        public bool is_available { get; set; }
    }

    private sealed class RecentDbRow
    {
        public string number { get; set; } = "";
        public string username { get; set; } = "";
        public decimal total { get; set; }
        public string status { get; set; } = "";
        public string method { get; set; } = "";
        public DateTime created_at { get; set; }
    }
#pragma warning restore IDE1006
}

using Microsoft.EntityFrameworkCore;
using Prismarket.Application.Features.Orders;
using Prismarket.Domain.Entities;

namespace Prismarket.Infrastructure.Persistence;

/// <summary>
/// Concurrency-safe stock reservation. <c>FOR UPDATE SKIP LOCKED</c> lets many customers check out
/// the same product simultaneously without blocking each other and without selling one key twice.
/// </summary>
public sealed class StockReservationService(AppDbContext db) : IStockReservationService
{
    public async Task<IReadOnlyList<ProductKey>> ReserveKeysAsync(int productId, int quantity, int orderId,
        CancellationToken ct) =>
        await db.ProductKeys.FromSql($"""
            UPDATE product_keys SET status = 'Reserved', reserved_by_order_id = {orderId}, updated_at = now()
            WHERE id IN (
                SELECT id FROM product_keys
                WHERE product_id = {productId} AND status = 'Available'
                ORDER BY id
                LIMIT {quantity}
                FOR UPDATE SKIP LOCKED)
            RETURNING *
            """).AsNoTracking().ToListAsync(ct);

    public async Task<IReadOnlyList<ProductAccount>> ReserveAccountsAsync(int productId, int quantity, int orderId,
        CancellationToken ct) =>
        await db.ProductAccounts.FromSql($"""
            UPDATE product_accounts SET status = 'Reserved', reserved_by_order_id = {orderId}, updated_at = now()
            WHERE id IN (
                SELECT id FROM product_accounts
                WHERE product_id = {productId} AND status = 'Available'
                ORDER BY id
                LIMIT {quantity}
                FOR UPDATE SKIP LOCKED)
            RETURNING *
            """).AsNoTracking().ToListAsync(ct);

    public async Task<int> ReleaseAsync(int orderId, CancellationToken ct)
    {
        var keys = await db.Database.ExecuteSqlAsync($"""
            UPDATE product_keys SET status = 'Available', reserved_by_order_id = NULL, updated_at = now()
            WHERE reserved_by_order_id = {orderId} AND status = 'Reserved'
            """, ct);
        var accounts = await db.Database.ExecuteSqlAsync($"""
            UPDATE product_accounts SET status = 'Available', reserved_by_order_id = NULL, updated_at = now()
            WHERE reserved_by_order_id = {orderId} AND status = 'Reserved'
            """, ct);
        return keys + accounts;
    }
}

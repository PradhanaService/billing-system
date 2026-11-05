using Microsoft.EntityFrameworkCore;
using WaterBilling.Data;
using WaterBilling.Data.Entities;

namespace WaterBilling.Services.Ledger;

// Read-only balance lookups. Balances come from the stored running balance of the
// latest line, never from editing a total (CLAUDE.md principle 1).
public static class LedgerQueries
{
    public static async Task<long> LastRunningBalanceAsync(AppDbContext db, long accountId, long? supplierId, long? customerId) =>
        await db.LedgerEntries
            .Where(e => e.AccountId == accountId && e.SupplierId == supplierId && e.CustomerId == customerId)
            .OrderByDescending(e => e.PostingSeq)
            .Select(e => (long?)e.RunningBalancePaise)
            .FirstOrDefaultAsync() ?? 0;

    public static async Task<long> LastVehicleRunningBalanceAsync(AppDbContext db, long accountId, long? supplierId, long? customerId, long vehicleId) =>
        await db.LedgerEntries
            .Where(e => e.AccountId == accountId && e.SupplierId == supplierId && e.CustomerId == customerId && e.VehicleId == vehicleId)
            .OrderByDescending(e => e.PostingSeq)
            .Select(e => e.VehicleRunningBalancePaise)
            .FirstOrDefaultAsync() ?? 0;

    // A party's balance (Dr positive = they owe us).
    public static Task<long> PartyBalanceAsync(AppDbContext db, PartyType type, long partyId) =>
        type == PartyType.Supplier
            ? LastRunningBalanceAsync(db, SystemAccounts.SupplierReceivables, partyId, null)
            : LastRunningBalanceAsync(db, SystemAccounts.CustomerReceivables, null, partyId);

    // Balance of a ledger without a party (Cash in Hand, Fuel Expense ...).
    public static Task<long> AccountBalanceAsync(AppDbContext db, long accountId) =>
        LastRunningBalanceAsync(db, accountId, null, null);

    // FR-LED-08 check: a supplier's balance per vehicle, and what is not tied to any vehicle.
    public static async Task<(long Total, long SumOfVehicles, long Unallocated)> SupplierSplitAsync(AppDbContext db, long supplierId)
    {
        var lines = db.LedgerEntries.Where(e => e.AccountId == SystemAccounts.SupplierReceivables && e.SupplierId == supplierId);
        var total = await lines.SumAsync(e => e.DebitPaise - e.CreditPaise);
        var vehicles = await lines.Where(e => e.VehicleId != null).SumAsync(e => e.DebitPaise - e.CreditPaise);
        return (total, vehicles, total - vehicles);
    }
}

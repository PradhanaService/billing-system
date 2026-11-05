using Microsoft.EntityFrameworkCore;
using WaterBilling.Data;

namespace WaterBilling.Services.Masters;

// "Does this record have history?" (FR-VM-08). A record with history cannot be deleted.
public static class MasterHistory
{
    // Counts every vehicle ever linked, including hidden ("deleted") ones, because they still point at the supplier.
    public static async Task<bool> SupplierHasHistoryAsync(AppDbContext db, long supplierId) =>
        await db.Vehicles.AnyAsync(v => v.SupplierId == supplierId)
        || await db.LedgerEntries.AnyAsync(e => e.SupplierId == supplierId)
        || await db.Vouchers.AnyAsync(v => v.SupplierId == supplierId);

    public static async Task<bool> CustomerHasHistoryAsync(AppDbContext db, long customerId) =>
        await db.LedgerEntries.AnyAsync(e => e.CustomerId == customerId)
        || await db.Vouchers.AnyAsync(v => v.CustomerId == customerId);

    public static async Task<bool> VehicleHasHistoryAsync(AppDbContext db, long vehicleId) =>
        await db.LedgerEntries.AnyAsync(e => e.VehicleId == vehicleId)
        || await db.Vouchers.AnyAsync(v => v.VehicleId == vehicleId);
}

using Microsoft.EntityFrameworkCore;
using WaterBilling.Data;

namespace WaterBilling.Services.Masters;

// "Does this record have history?" (FR-VM-08). A record with history cannot be deleted.
// Each later module adds its own check here (ledger lines, invoices).
public static class MasterHistory
{
    // Counts every vehicle ever linked, including hidden ("deleted") ones, because they still point at the supplier.
    public static async Task<bool> SupplierHasHistoryAsync(AppDbContext db, long supplierId) =>
        await db.Vehicles.AnyAsync(v => v.SupplierId == supplierId);

    public static Task<bool> CustomerHasHistoryAsync(AppDbContext db, long customerId) => Task.FromResult(false);

    public static Task<bool> VehicleHasHistoryAsync(AppDbContext db, long vehicleId) => Task.FromResult(false);
}

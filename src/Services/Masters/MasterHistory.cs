using WaterBilling.Data;

namespace WaterBilling.Services.Masters;

// "Does this record have history?" (FR-VM-08). A record with history cannot be deleted.
// Each later module adds its own check here (vehicles, ledger lines, invoices).
public static class MasterHistory
{
    public static Task<bool> SupplierHasHistoryAsync(AppDbContext db, long supplierId) => Task.FromResult(false);

    public static Task<bool> CustomerHasHistoryAsync(AppDbContext db, long customerId) => Task.FromResult(false);
}

using WaterBilling.Data.Entities;
using WaterBilling.Services.Common;

namespace WaterBilling.Services.Billing;

// FR-INV-03: Amount = Trip x Rate. Client change, Nov 2026: volume billing was removed, every
// invoice is billed per trip. Pure functions: easy to test, used by the screen preview too.
public static class InvoiceMath
{
    public static long Amount(int trips, long ratePaise) => checked(trips * ratePaise);

    // The "Qty" column of invoices saved earlier: trips, or kL for the old volume invoices.
    public static string Quantity(BillingBasis basis, int trips, long volumeTenths) =>
        basis == BillingBasis.Trip ? trips.ToString() : Volume.Format(volumeTenths);
}

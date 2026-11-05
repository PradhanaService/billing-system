using WaterBilling.Data.Entities;
using WaterBilling.Services.Common;

namespace WaterBilling.Services.Billing;

// FR-INV-03: Amount = Trip × Rate (Trip vehicles) or Volume × Rate (Volume vehicles),
// rounded half-up to the paisa. Pure functions: easy to test, used by the screen preview too.
public static class InvoiceMath
{
    public static long Amount(BillingBasis basis, int trips, long volumeTenths, long ratePaise) => basis switch
    {
        BillingBasis.Trip => checked(trips * ratePaise),
        // rate (₹/kL) × volume (kL) -> rupees with up to 3 decimals -> round half-up to paise
        _ => Money.RoundHalfUp(Money.ToRupees(ratePaise) * Volume.ToKilolitres(volumeTenths)),
    };

    // The "Qty" column: trips for Trip vehicles, kL for Volume vehicles.
    public static string Quantity(BillingBasis basis, int trips, long volumeTenths) =>
        basis == BillingBasis.Trip ? trips.ToString() : Volume.Format(volumeTenths);
}

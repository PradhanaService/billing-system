using WaterBilling.Services.Common;

namespace WaterBilling.Services.Ledger;

// The trading day runs 6:00 PM to 6:00 PM IST (FR-DSP-08, client question 5).
// Before 18:00 the business date is today; from 18:00 it is tomorrow.
public class BusinessCalendar(TimeProvider clock)
{
    public const int CutoffHour = 18;

    public DateOnly Today() => DateFor(clock.GetUtcNow().UtcDateTime);

    public static DateOnly DateFor(DateTime utc)
    {
        var ist = IndianTime.ToIst(utc);
        var date = DateOnly.FromDateTime(ist);
        return ist.Hour >= CutoffHour ? date.AddDays(1) : date;
    }
}

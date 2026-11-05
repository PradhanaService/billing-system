using System.Globalization;

namespace WaterBilling.Services.Common;

// Money rules (CLAUDE.md principle 4: "Money is exact").
//   - Stored as whole paise in a long. ₹1,250.50 -> 125050.
//   - decimal is used only on the way in (typed by a user) and out (shown on screen).
//   - double/float are never used for money.
public static class Money
{
    private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");

    // Typed amount -> paise. More than 2 decimal places is an input mistake, so it is refused, not rounded.
    // Example: FromRupees(1250.5m) == 125050
    public static long FromRupees(decimal rupees)
    {
        var paise = rupees * 100m;
        if (paise != decimal.Truncate(paise))
            throw new ArgumentException($"Amount {rupees} has more than 2 decimal places.", nameof(rupees));
        return checked((long)paise);
    }

    // Calculated amount -> paise, rounded half-up (FR-INV-03). 0.5 paise goes up.
    // Example: RoundHalfUp(12.345m) == 1235
    public static long RoundHalfUp(decimal rupees) =>
        checked((long)Math.Round(rupees * 100m, 0, MidpointRounding.AwayFromZero));

    // Paise -> rupees as a decimal, for calculations on screen.
    public static decimal ToRupees(long paise) => paise / 100m;

    // Indian display format (NFR-USE-02). 49478400 -> "4,94,784.00"
    public static string Format(long paise) => ToRupees(paise).ToString("#,##0.00", India);
}

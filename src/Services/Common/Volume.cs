namespace WaterBilling.Services.Common;

// Meter readings and volumes (kilolitres, one decimal place).
//   Stored as whole tenths in a long. 28451.2 kL -> 284512.
public static class Volume
{
    // Typed reading -> tenths. More than 1 decimal place is refused.
    // Example: FromKilolitres(28451.2m) == 284512
    public static long FromKilolitres(decimal kilolitres)
    {
        var tenths = kilolitres * 10m;
        if (tenths != decimal.Truncate(tenths))
            throw new ArgumentException($"Reading {kilolitres} has more than 1 decimal place.", nameof(kilolitres));
        return checked((long)tenths);
    }

    public static decimal ToKilolitres(long tenths) => tenths / 10m;

    // 284512 -> "28451.2"
    public static string Format(long tenths) =>
        ToKilolitres(tenths).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
}

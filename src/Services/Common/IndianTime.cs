using System.Globalization;

namespace WaterBilling.Services.Common;

// Times are stored in UTC and shown in IST (NFR-AUD-03).
// IST is always UTC + 5:30. India has no daylight saving, so a fixed offset is exact.
public static class IndianTime
{
    public static readonly TimeSpan Offset = new(5, 30, 0);

    public static DateTime ToIst(DateTime utc)
    {
        if (utc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Expected a UTC time.", nameof(utc));
        return DateTime.SpecifyKind(utc + Offset, DateTimeKind.Unspecified);
    }

    // Display formats (NFR-USE-02): DD/MM/YYYY and 12-hour time.
    public static string FormatDate(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    public static string FormatDateTime(DateTime utc) =>
        ToIst(utc).ToString("dd/MM/yyyy hh:mm tt", CultureInfo.InvariantCulture);
}

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

    public static string FormatTime(DateTime utc) => ToIst(utc).ToString("hh:mm tt", CultureInfo.InvariantCulture);

    // An IST date and clock time -> the UTC instant stored in the database.
    public static DateTime FromIst(DateOnly date, TimeOnly time) =>
        DateTime.SpecifyKind(date.ToDateTime(time) - Offset, DateTimeKind.Utc);

    // A typed time such as "11:05 AM", "11:05am", "9:5 pm" or "23:05" -> TimeOnly. Null when it is not a time.
    public static TimeOnly? ParseTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim().ToUpperInvariant().Replace(".", "");
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\s*(AM|PM)$", " $1");
        string[] formats = ["h:mm tt", "h:m tt", "hh:mm tt", "H:mm", "HH:mm", "h tt"];
        return TimeOnly.TryParseExact(t, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;
    }
}

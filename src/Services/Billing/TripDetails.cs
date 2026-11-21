using WaterBilling.Services.Common;

namespace WaterBilling.Services.Billing;

// Trip timing and meter readings typed on the Sales Invoice (and on the invoice edit screen).
// The date is picked from a calendar; the time is typed ("11:05 AM"). Times are stored in UTC.
// Meter readings are kilolitres with one decimal, stored as tenths; each meter is optional, but a
// meter with a start needs an end that is not lower. The trip volume is the sum of the meters used.
public sealed record TripTimes(DateTime? InUtc, DateTime? OutUtc);

public sealed record MeterReadings(long? Start1, long? End1, long? Start2, long? End2, long? Start3, long? End3)
{
    public long VolumeTenths => (End1 - Start1 ?? 0) + (End2 - Start2 ?? 0) + (End3 - Start3 ?? 0);
}

public static class TripDetails
{
    public static Result<TripTimes> Times(DateOnly? inDate, string? inTime, DateOnly? outDate, string? outTime)
    {
        if (!TryOne("In Time", inDate, inTime, out var inUtc, out var error)) return Result<TripTimes>.Fail(error!);
        if (!TryOne("Out Time", outDate, outTime, out var outUtc, out error)) return Result<TripTimes>.Fail(error!);
        if (inUtc is { } i && outUtc is { } o && o < i) return Result<TripTimes>.Fail("Out Time cannot be before In Time.");
        return Result<TripTimes>.Success(new TripTimes(inUtc, outUtc));
    }

    public static Result<MeterReadings> Meters(decimal? s1, decimal? e1, decimal? s2, decimal? e2, decimal? s3, decimal? e3)
    {
        var values = new long?[6];
        decimal?[] typed = [s1, e1, s2, e2, s3, e3];
        for (var n = 0; n < 3; n++)
        {
            var (start, end) = (typed[n * 2], typed[n * 2 + 1]);
            var name = $"Meter {n + 1}";
            if (start is null && end is null) continue;
            if (start is null || end is null) return Result<MeterReadings>.Fail($"{name}: give both the start and the end reading.");
            if (start < 0 || end < 0) return Result<MeterReadings>.Fail($"{name}: readings cannot be negative.");
            if (end < start) return Result<MeterReadings>.Fail($"{name}: the end reading cannot be lower than the start reading.");
            try
            {
                values[n * 2] = Volume.FromKilolitres(start.Value);
                values[n * 2 + 1] = Volume.FromKilolitres(end.Value);
            }
            catch (ArgumentException) { return Result<MeterReadings>.Fail($"{name}: readings have one decimal place at most (e.g. 28451.2)."); }
        }
        return Result<MeterReadings>.Success(new MeterReadings(values[0], values[1], values[2], values[3], values[4], values[5]));
    }

    private static bool TryOne(string name, DateOnly? date, string? time, out DateTime? utc, out string? error)
    {
        utc = null; error = null;
        if (string.IsNullOrWhiteSpace(time)) return true;   // not filled in: allowed
        if (IndianTime.ParseTime(time) is not { } t) { error = $"{name}: type the time like 11:05 AM."; return false; }
        if (date is not { } d) { error = $"{name}: pick the date."; return false; }
        utc = IndianTime.FromIst(d, t);
        return true;
    }
}

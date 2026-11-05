using Microsoft.EntityFrameworkCore;
using WaterBilling.Data;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Common;

namespace WaterBilling.Services.Ledger;

// Helpers for the BusinessDays table, used inside other services' transactions.
public static class BusinessDays
{
    // Returns the date if it is open, creating its row on first use.
    // A closed day refuses new postings (FR-LED-10).
    public static async Task<DateOnly> RequireOpenAsync(AppDbContext db, DateOnly date)
    {
        var day = await GetOrCreateAsync(db, date);
        if (day.Status == BusinessDayStatus.Closed)
            throw new BusinessRuleException(
                $"Business day {IndianTime.FormatDate(date)} is already closed. New entries go to the next day, which starts at 6:00 PM.");
        return date;
    }

    public static async Task<BusinessDay> GetOrCreateAsync(AppDbContext db, DateOnly date)
    {
        var day = await db.BusinessDays.FindAsync(date);
        if (day is not null) return day;

        // Opening cash = actual cash counted at the last close before this day (FR-DAY-01, FR-DAY-05).
        var lastClosed = await db.BusinessDays
            .Where(d => d.Status == BusinessDayStatus.Closed && d.Date < date)
            .OrderByDescending(d => d.Date)
            .FirstOrDefaultAsync();
        day = new BusinessDay { Date = date, OpeningCashPaise = lastClosed?.ActualCashPaise ?? 0, Version = 1 };
        db.BusinessDays.Add(day);
        return day;
    }
}

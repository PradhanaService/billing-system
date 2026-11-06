using Microsoft.EntityFrameworkCore;
using WaterBilling.Data;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Billing;
using WaterBilling.Services.Ledger;
using WaterBilling.Services.Security;

namespace WaterBilling.Services.Accounts;

// Works out the Dashboard figures: month sales (cash and credit) and trips for the chosen month,
// today's sales, receipts and payments, the total outstanding, today's opening and closing cash,
// sales for the last 6 months, and the 3 latest invoices.

public sealed record MonthSales(int Year, int Month, long SalesPaise)
{
    public string Key => $"{Year:0000}-{Month:00}";
    public string Label => new DateTime(Year, Month, 1).ToString("MMM yy", System.Globalization.CultureInfo.InvariantCulture);
}

public sealed record DashboardData(
    DateOnly Today, int Year, int Month,
    long MonthSalesPaise, long TodaySalesPaise, long TodayReceiptsPaise, long TodayPaymentsPaise,   // FR-DASH-01
    long MonthCashSalesPaise, long MonthCreditSalesPaise, int MonthTrips, long OutstandingPaise,     // FR-DASH-02
    long OpeningCashPaise, long ClosingCashPaise,
    IReadOnlyList<MonthSales> LastSixMonths,                                                         // FR-DASH-04
    IReadOnlyList<InvoiceRow> RecentInvoices);                                                       // FR-DASH-06

// Dashboard figures. Month figures follow the month selector; "today" figures always
// use today's business day (FR-DASH-08).
public class DashboardService(IDbContextFactory<AppDbContext> dbFactory, ICurrentUser currentUser, BusinessCalendar calendar)
{
    public async Task<DashboardData> GetAsync(int? year = null, int? month = null)
    {
        await currentUser.RequireAsync(Permission.DashboardView);
        await using var db = await dbFactory.CreateDbContextAsync();
        var today = calendar.Today();
        var y = year ?? today.Year;
        var m = month is >= 1 and <= 12 ? month.Value : today.Month;
        var monthStart = new DateOnly(y, m, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        var live = db.Invoices.Where(i => i.Status != InvoiceStatus.Cancelled);
        var monthInvoices = await live.Where(i => i.InvoiceDate >= monthStart && i.InvoiceDate <= monthEnd)
            .Select(i => new { i.PaymentMode, i.AmountPaise, i.Trips }).ToListAsync();
        var todaySales = await live.Where(i => i.InvoiceDate == today).SumAsync(i => i.AmountPaise);
        var todayVouchers = await db.Vouchers.Where(v => v.VoucherDate == today && v.Status == VoucherStatus.Posted)
            .GroupBy(v => v.Type).Select(g => new { g.Key, Sum = g.Sum(v => v.AmountPaise) }).ToListAsync();

        // Outstanding = what all suppliers and customers owe now (both control ledgers).
        var outstanding = await db.LedgerEntries
            .Where(e => e.AccountId == SystemAccounts.SupplierReceivables || e.AccountId == SystemAccounts.CustomerReceivables)
            .SumAsync(e => e.DebitPaise - e.CreditPaise);

        var cash = await DaySalesService.CashSummaryAsync(db, today, today);

        var sixStart = monthStart.AddMonths(-5);
        var perMonth = await live.Where(i => i.InvoiceDate >= sixStart && i.InvoiceDate <= monthEnd)
            .Select(i => new { i.InvoiceDate, i.AmountPaise }).ToListAsync();
        var months = Enumerable.Range(0, 6).Select(k => sixStart.AddMonths(k))
            .Select(d => new MonthSales(d.Year, d.Month,
                perMonth.Where(i => i.InvoiceDate.Year == d.Year && i.InvoiceDate.Month == d.Month).Sum(i => i.AmountPaise)))
            .ToList();

        var recent = await (from i in db.Invoices
                            join v in db.Vehicles on i.VehicleId equals v.Id
                            join s in db.Suppliers on i.SupplierId equals s.Id
                            join c in db.Customers on i.CustomerId equals c.Id into cj
                            from c in cj.DefaultIfEmpty()
                            orderby i.InvoiceNo descending
                            select new InvoiceRow(i.Id, i.DisplayNo, i.InvoiceDate, v.Code, v.VehicleNo, c == null ? s.Name : c.Name,
                                i.PaymentMode, i.AmountPaise, i.AllocatedPaise, i.Status)).Take(3).ToListAsync();

        return new DashboardData(today, y, m,
            monthInvoices.Sum(i => i.AmountPaise), todaySales,
            todayVouchers.Where(v => v.Key == VoucherType.Receipt).Sum(v => v.Sum),
            todayVouchers.Where(v => v.Key == VoucherType.Payment).Sum(v => v.Sum),
            monthInvoices.Where(i => i.PaymentMode == InvoicePaymentMode.Cash).Sum(i => i.AmountPaise),
            monthInvoices.Where(i => i.PaymentMode == InvoicePaymentMode.Credit).Sum(i => i.AmountPaise),
            monthInvoices.Sum(i => i.Trips), outstanding, cash.OpeningPaise, cash.ClosingPaise, months, recent);
    }
}

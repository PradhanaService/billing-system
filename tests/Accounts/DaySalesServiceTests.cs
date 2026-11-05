using WaterBilling.Data.Entities;
using WaterBilling.Services.Accounts;
using WaterBilling.Services.Billing;
using WaterBilling.Services.Ledger;
using WaterBilling.Services.Security;
using WaterBilling.Tests.Billing;

namespace WaterBilling.Tests.Accounts;

public class DaySalesServiceTests : BillingFixture
{
    private static readonly DateOnly Day1 = new(2026, 11, 5);
    private static readonly DateOnly Day2 = new(2026, 11, 6);

    public DaySalesServiceTests() => Me.User = Db.AddUser(UserRole.Admin);

    private DaySalesService Days => Svc.DaySales;

    // Cash sale 150, credit sale 150, cash receipt 100, cash fuel payment 30.
    private async Task TradeAsync()
    {
        await Svc.Invoices.CreateAsync(new InvoiceForm { VehicleId = TripVehicle, Rate = 150, PaymentMode = InvoicePaymentMode.Cash });
        await Svc.Invoices.CreateAsync(new InvoiceForm { VehicleId = TripVehicle, Rate = 150 });
        await Svc.Receipts.CreateAsync(new ReceiptForm { PartyType = PartyType.Supplier, PartyId = Supplier, Amount = 100 });
        await Svc.Payments.CreateAsync(new PaymentForm { AccountId = 10, Amount = 30 });
    }

    [Fact]
    public async Task FR_INV_16_Closing_Is_Opening_Plus_Cash_Sales_Plus_Receipts_Minus_Payments()
    {
        await TradeAsync();

        var day = await Days.GetDayAsync(Day1);

        Assert.Equal((0L, 15000L, 10000L, 3000L), (day.Cash.OpeningPaise, day.Cash.CashSalesPaise, day.Cash.CashReceiptsPaise, day.Cash.CashPaymentsPaise));
        Assert.Equal(22000, day.Cash.ClosingPaise);
        Assert.Equal((30000L, 15000L, 15000L, 2), (day.Totals.TotalSalesPaise, day.Totals.CashSalesPaise, day.Totals.CreditSalesPaise, day.Totals.Trips));
        Assert.Equal(4, day.Transactions.Count);
        using var ctx = Db.NewContext();
        Assert.Equal(day.Cash.ClosingPaise, await LedgerQueries.AccountBalanceAsync(ctx, SystemAccounts.Cash));
    }

    [Fact]
    public async Task Cancelled_Cash_Sale_Reduces_Cash_Sales()
    {
        var id = (await Svc.Invoices.CreateAsync(new InvoiceForm { VehicleId = TripVehicle, Rate = 150, PaymentMode = InvoicePaymentMode.Cash })).Value;
        await Svc.Invoices.CancelAsync(id, "mistake", (await Svc.Invoices.GetAsync(id))!.Version);

        var day = await Days.GetDayAsync(Day1);

        Assert.Equal(0, day.Cash.CashSalesPaise);
        Assert.Equal(0, day.Totals.TotalSalesPaise);
        Assert.True(Assert.Single(day.Transactions).Cancelled);
    }

    [Fact]
    public async Task FR_DAY_04_Exact_Count_Closes_Without_Reason_Or_Posting()
    {
        await TradeAsync();
        var vouchersBefore = Db.NewContext().Vouchers.Count();

        var result = await Days.CloseDayAsync(Day1, 220m, null, 0);

        Assert.True(result.Ok, result.Error);
        using var ctx = Db.NewContext();
        Assert.Equal(vouchersBefore, ctx.Vouchers.Count());
        var day = ctx.BusinessDays.Single(d => d.Date == Day1);
        Assert.Equal((BusinessDayStatus.Closed, 22000L, 22000L, 0L),
            (day.Status, day.ExpectedClosingCashPaise!.Value, day.ActualCashPaise!.Value, day.CashDifferencePaise!.Value));
    }

    [Fact]
    public async Task FR_DAY_04_P8_Cash_Short_Needs_Reason_And_Posts()
    {
        await TradeAsync();

        Assert.False((await Days.CloseDayAsync(Day1, 215m, null, 0)).Ok);
        Assert.True((await Days.CloseDayAsync(Day1, 215m, "Change given wrongly", 0)).Ok);

        using var ctx = Db.NewContext();
        Assert.Equal(21500, await LedgerQueries.AccountBalanceAsync(ctx, SystemAccounts.Cash));
        Assert.Equal(500, await LedgerQueries.AccountBalanceAsync(ctx, SystemAccounts.CashOverShort));   // Dr = loss
        Assert.Equal(-500, ctx.BusinessDays.Single(d => d.Date == Day1).CashDifferencePaise);
    }

    [Fact]
    public async Task FR_DAY_04_P9_Cash_Over_Posts_The_Other_Way()
    {
        await TradeAsync();

        Assert.True((await Days.CloseDayAsync(Day1, 225m, "Customer overpaid", 0)).Ok);

        using var ctx = Db.NewContext();
        Assert.Equal(22500, await LedgerQueries.AccountBalanceAsync(ctx, SystemAccounts.Cash));
        Assert.Equal(-500, await LedgerQueries.AccountBalanceAsync(ctx, SystemAccounts.CashOverShort));
    }

    [Fact]
    public async Task FR_DAY_05_Closing_Locks_The_Day_And_Carries_Cash_Forward()
    {
        await TradeAsync();
        await Days.CloseDayAsync(Day1, 215m, "short", 0);

        // Still before 6 PM on day 1: new postings are refused.
        var refused = await Svc.Receipts.CreateAsync(new ReceiptForm { PartyType = PartyType.Supplier, PartyId = Supplier, Amount = 1 });
        Assert.False(refused.Ok);
        Assert.Contains("already closed", refused.Error);

        Db.Clock.Advance(TimeSpan.FromHours(3));   // 18:30 IST -> business day 2
        await Svc.Receipts.CreateAsync(new ReceiptForm { PartyType = PartyType.Supplier, PartyId = Supplier, Amount = 50 });

        var day2 = await Days.GetDayAsync(Day2);
        Assert.Equal(21500, day2.OpeningCashPaise);
        Assert.Equal(21500, day2.Cash.OpeningPaise);   // ledger agrees with the carried figure
        Assert.Equal(26500, day2.Cash.ClosingPaise);
        Assert.False((await Days.CloseDayAsync(Day1, 1m, "again", 0)).Ok);
    }

    [Fact]
    public async Task FR_DAY_03_Earlier_Open_Day_Blocks_Closing()
    {
        await TradeAsync();
        Db.Clock.Advance(TimeSpan.FromDays(1));
        await Svc.Receipts.CreateAsync(new ReceiptForm { PartyType = PartyType.Supplier, PartyId = Supplier, Amount = 10 });

        var day2 = await Days.GetDayAsync(Day2);
        Assert.Contains("05/11/2026 is not closed", Assert.Single(day2.Blockers).Message);
        Assert.False((await Days.CloseDayAsync(Day2, 230m, null, day2.Version)).Ok);

        // Close in order: day 1 (late, from day 2), then day 2.
        Assert.True((await Days.CloseDayAsync(Day1, 220m, null, 0)).Ok);
        day2 = await Days.GetDayAsync(Day2);
        Assert.Equal(22000, day2.OpeningCashPaise);   // updated when day 1 closed
        Assert.True((await Days.CloseDayAsync(Day2, 230m, null, day2.Version)).Ok);
    }

    [Fact]
    public async Task Late_Close_Posts_The_Difference_On_The_Closed_Day()
    {
        await TradeAsync();
        Db.Clock.Advance(TimeSpan.FromDays(1));

        await Days.CloseDayAsync(Day1, 200m, "short", 0);

        using var ctx = Db.NewContext();
        var shortLine = ctx.LedgerEntries.Single(e => e.AccountId == SystemAccounts.CashOverShort);
        Assert.Equal((Day1, Day1), (shortLine.EntryDate, shortLine.ValueDate));
    }

    [Fact]
    public async Task Future_Day_Cannot_Be_Closed()
    {
        Assert.False((await Days.CloseDayAsync(Day2, 0, null, 0)).Ok);
    }

    [Fact]
    public async Task Billing_Clerk_Can_Close_The_Day()
    {
        Me.User = Db.AddUser(UserRole.BillingClerk);
        Assert.True((await Days.CloseDayAsync(Day1, 0, null, 0)).Ok);
    }

    [Fact]
    public async Task FR_INV_15_Period_Display_Totals_Match_The_Days()
    {
        await TradeAsync();
        await Days.CloseDayAsync(Day1, 210m, "short", 0);
        Db.Clock.Advance(TimeSpan.FromDays(1));
        await TradeAsync();

        var (cash, totals, rows) = await Days.PeriodAsync(Day1, Day2);

        Assert.Equal(8, rows.Count);
        Assert.Equal(60000, totals.TotalSalesPaise);
        Assert.Equal(-1000, cash.AdjustmentsPaise);
        using var ctx = Db.NewContext();
        Assert.Equal(cash.ClosingPaise, await LedgerQueries.AccountBalanceAsync(ctx, SystemAccounts.Cash));
    }
}

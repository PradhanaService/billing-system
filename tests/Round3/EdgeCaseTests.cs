using WaterBilling.Data.Entities;
using WaterBilling.Services.Accounts;
using WaterBilling.Services.Billing;
using WaterBilling.Services.Ledger;
using WaterBilling.Services.Masters;
using WaterBilling.Services.Security;
using WaterBilling.Tests.Billing;

namespace WaterBilling.Tests.Round3;

// Test round 3: limits, odd input and unusual paths.
public class EdgeCaseTests : BillingFixture
{
    [Fact]
    public async Task Volume_With_Two_Decimals_Is_Refused()
    {
        var r = await Svc.Invoices.CreateAsync(new InvoiceForm { VehicleId = VolumeVehicle, Rate = 12.35m, VolumeKl = 10.25m });
        Assert.False(r.Ok);
        Assert.Contains("one decimal", r.Error);
    }

    [Theory]
    [InlineData("10.005")]
    [InlineData("1000000000000")]
    public async Task Receipt_Amount_Out_Of_Range_Is_Refused(string amount) =>
        Assert.False((await Svc.Receipts.CreateAsync(new ReceiptForm { PartyType = PartyType.Supplier, PartyId = Supplier, Amount = decimal.Parse(amount) })).Ok);

    [Fact]
    public async Task Largest_Allowed_Invoice_Does_Not_Overflow()
    {
        Me.User = Db.AddUser(UserRole.Admin);
        var r = await Svc.Invoices.CreateAsync(new InvoiceForm { VehicleId = TripVehicle, Trips = 99, Rate = 99_99_99_999.99m, RateReason = "limit test" });

        Assert.True(r.Ok, r.Error);
        Assert.Equal(99L * 99_999_999_999, (await Svc.Invoices.GetAsync(r.Value))!.AmountPaise);
    }

    [Fact]
    public async Task Too_Long_Narration_Is_Refused()
    {
        var r = await Svc.Invoices.CreateAsync(new InvoiceForm { VehicleId = TripVehicle, Rate = 150, Narration = new string('x', 301) });
        Assert.False(r.Ok);
    }

    [Fact]
    public async Task Inactive_Customer_Cannot_Be_Billed()
    {
        Me.User = Db.AddUser(UserRole.Admin);
        var c = (await Svc.Customers.GetAsync(Customer))!;
        c.Status = RecordStatus.Inactive;
        await Svc.Customers.UpdateAsync(Customer, c);

        var r = await Svc.Invoices.CreateAsync(new InvoiceForm { VehicleId = TripVehicle, Rate = 150, BillToType = PartyType.Customer, CustomerId = Customer });

        Assert.False(r.Ok);
    }

    [Fact]
    public async Task Deleted_Vehicle_Cannot_Be_Billed()
    {
        Me.User = Db.AddUser(UserRole.Admin);
        var extra = (await Svc.Vehicles.CreateAsync(new VehicleForm { VehicleNo = "TN 99 Z", SupplierId = Supplier, TankCapacityLitres = 1000, Rate = 10 })).Value;
        await Svc.Vehicles.DeleteAsync(extra);

        Assert.False((await Svc.Invoices.CreateAsync(new InvoiceForm { VehicleId = extra, Rate = 10 })).Ok);
        Assert.DoesNotContain(await Svc.Vehicles.ListBillableAsync(), v => v.Id == extra);
    }

    [Fact]
    public async Task Quiet_Day_Can_Be_Closed_And_Cash_Carries()
    {
        Me.User = Db.AddUser(UserRole.Admin);
        await Svc.Invoices.CreateAsync(new InvoiceForm { VehicleId = TripVehicle, Rate = 150, PaymentMode = InvoicePaymentMode.Cash });
        await Svc.DaySales.CloseDayAsync(new DateOnly(2026, 11, 5), 150, null, 0);
        Db.Clock.Advance(TimeSpan.FromDays(1));   // nothing happens on the 6th

        Assert.True((await Svc.DaySales.CloseDayAsync(new DateOnly(2026, 11, 6), 150, null, 0)).Ok);
        Db.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(15000, (await Svc.DaySales.GetDayAsync(new DateOnly(2026, 11, 7))).Cash.OpeningPaise);
    }

    [Fact]
    public async Task Tamil_Names_Are_Unique_Ignoring_Spaces()
    {
        Me.User = Db.AddUser(UserRole.Admin);
        Assert.True((await Svc.Suppliers.CreateAsync(new SupplierForm { Name = "சங்கீதா  வாட்டர்", ShortName = "சங்கீதா", Phone = "9876543211" })).Ok);
        Assert.False((await Svc.Suppliers.CreateAsync(new SupplierForm { Name = " சங்கீதா வாட்டர் ", ShortName = "x", Phone = "9876543212" })).Ok);
    }

    [Fact]
    public async Task Statement_With_Reversed_Dates_Returns_Nothing()
    {
        Me.User = Db.AddUser(UserRole.Admin);
        Assert.Null(await Svc.Statements.GetAsync(new StatementRequest(StatementKind.Supplier, Supplier, new(2026, 12, 1), new(2026, 11, 1))));
    }

    [Fact]
    public async Task Whole_Ledger_Stays_Balanced_After_Every_Kind_Of_Event()
    {
        Me.User = Db.AddUser(UserRole.Admin);
        var inv = (await Svc.Invoices.CreateAsync(new InvoiceForm { VehicleId = TripVehicle, Rate = 150 })).Value;
        await Svc.Invoices.CreateAsync(new InvoiceForm { VehicleId = VolumeVehicle, Rate = 12.35m, VolumeKl = 3.3m, PaymentMode = InvoicePaymentMode.Cash });
        var rcp = (await Svc.Receipts.CreateAsync(new ReceiptForm { PartyType = PartyType.Supplier, PartyId = Supplier, Amount = 400 })).Value;
        await Svc.Receipts.CancelAsync(rcp, "bounced", 1);
        await Svc.Invoices.CancelAsync(inv, "wrong", 1);
        await Svc.Payments.CreateAsync(new PaymentForm { AccountId = 10, Amount = 20 });
        await Svc.Payments.CreateAsync(new PaymentForm { AccountId = SystemAccounts.CustomerReceivables, PartyType = PartyType.Customer, PartyId = Customer, Amount = 5, ConfirmAboveCash = true });
        await Svc.DaySales.CloseDayAsync(new DateOnly(2026, 11, 5), 1, "test", 0);

        using var ctx = Db.NewContext();
        foreach (var v in ctx.LedgerEntries.ToList().GroupBy(e => e.VoucherId))
            Assert.Equal(v.Sum(e => e.DebitPaise), v.Sum(e => e.CreditPaise));
        Assert.Equal(1L, await LedgerQueries.AccountBalanceAsync(ctx, SystemAccounts.Cash) / 100);
        var numbers = ctx.Vouchers.Where(v => v.Type == VoucherType.Sales).Select(v => v.Number).OrderBy(n => n).ToList();
        Assert.Equal(Enumerable.Range(1, numbers.Count).Select(i => (long)i), numbers);   // gapless
    }
}

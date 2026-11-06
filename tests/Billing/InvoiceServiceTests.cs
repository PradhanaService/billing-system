using WaterBilling.Data.Entities;
using WaterBilling.Services.Billing;
using WaterBilling.Services.Ledger;
using WaterBilling.Services.Masters;
using WaterBilling.Services.Security;

namespace WaterBilling.Tests.Billing;

public class InvoiceServiceTests : BillingFixture
{
    private InvoiceService Inv => Svc.Invoices;

    private InvoiceForm TripForm(InvoicePaymentMode mode = InvoicePaymentMode.Credit, int trips = 1) =>
        new() { SalesPersonId = Seller, VehicleId = TripVehicle, Trips = trips, Rate = 150, PaymentMode = mode };

    [Theory]
    [InlineData(1, 15000)]
    [InlineData(3, 45000)]
    public async Task FR_INV_03_Trip_Amount_Is_Trips_Times_Rate(int trips, long expectedPaise)
    {
        var id = (await Inv.CreateAsync(TripForm(trips: trips))).Value;
        Assert.Equal(expectedPaise, (await Inv.GetAsync(id))!.AmountPaise);
    }

    [Theory]
    [InlineData(BillingBasis.Volume, 1, 105, 1235, 12968)]  // 10.5 kL × 12.35 = 129.675 -> 129.68
    [InlineData(BillingBasis.Volume, 1, 3, 1235, 371)]      // 0.3 × 12.35 = 3.705 -> 3.71 (half-up)
    [InlineData(BillingBasis.Volume, 1, 1, 1, 0)]           // 0.1 × 0.01 = 0.001 -> 0.00
    [InlineData(BillingBasis.Trip, 2, 999, 15000, 30000)]   // volume ignored for trip vehicles
    public void FR_INV_03_Amount_Rounding(BillingBasis basis, int trips, long tenths, long rate, long expected) =>
        Assert.Equal(expected, InvoiceMath.Amount(basis, trips, tenths, rate));

    [Fact]
    public async Task FR_INV_03_Volume_Vehicle_Needs_Volume_And_Bills_By_It()
    {
        var noVolume = await Inv.CreateAsync(new InvoiceForm { SalesPersonId = Seller, VehicleId = VolumeVehicle, Rate = 12.35m });
        Assert.False(noVolume.Ok);

        var id = (await Inv.CreateAsync(new InvoiceForm { SalesPersonId = Seller, VehicleId = VolumeVehicle, Rate = 12.35m, VolumeKl = 10.5m })).Value;
        var inv = (await Inv.GetAsync(id))!;
        Assert.Equal(12968, inv.AmountPaise);
        Assert.Equal("10.5", inv.Quantity);
        Assert.Equal("kL", inv.Measurement);
    }

    [Fact]
    public async Task FR_INV_01_Numbers_Are_Gapless_And_Shown_Before_Save()
    {
        Assert.Equal("INV-1", await Inv.PeekNextNumberAsync());
        await Inv.CreateAsync(TripForm());
        var bad = await Inv.CreateAsync(new InvoiceForm { SalesPersonId = Seller, VehicleId = VolumeVehicle, Rate = 12.35m });   // fails
        await Inv.CreateAsync(TripForm());

        Assert.False(bad.Ok);
        var rows = await Inv.ListAsync(DateOnly.MinValue, DateOnly.MaxValue);
        Assert.Equal(["INV-2", "INV-1"], rows.Select(r => r.DisplayNo));
        Assert.Equal("INV-3", await Inv.PeekNextNumberAsync());
    }

    [Fact]
    public async Task FR_LED_01_P1_Credit_Sale_Posts_Party_And_Sales()
    {
        var id = (await Inv.CreateAsync(TripForm())).Value;

        using var ctx = Db.NewContext();
        var voucherId = ctx.Invoices.Single(i => i.Id == id).VoucherId;
        var lines = ctx.LedgerEntries.Where(e => e.VoucherId == voucherId).OrderBy(e => e.LineNo).ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal((SystemAccounts.SupplierReceivables, 15000L, (long?)Supplier, (long?)TripVehicle),
            (lines[0].AccountId, lines[0].DebitPaise, lines[0].SupplierId, lines[0].VehicleId));
        Assert.Equal((SystemAccounts.WaterSales, 15000L), (lines[1].AccountId, lines[1].CreditPaise));
        Assert.Equal(15000, await LedgerQueries.PartyBalanceAsync(ctx, PartyType.Supplier, Supplier));
        Assert.Equal(InvoiceStatus.Posted, ctx.Invoices.Single().Status);
    }

    [Fact]
    public async Task P2_Cash_Sale_Shows_On_Party_Statement_And_Nets_To_Zero()
    {
        var id = (await Inv.CreateAsync(TripForm(InvoicePaymentMode.Cash))).Value;

        using var ctx = Db.NewContext();
        var lines = ctx.LedgerEntries.Where(e => e.SupplierId == Supplier).OrderBy(e => e.PostingSeq).ToList();
        Assert.Equal(2, lines.Count);                       // the trip appears on the statement...
        Assert.Equal(0, lines[^1].RunningBalancePaise);     // ...and nets to zero
        Assert.Equal(15000, await LedgerQueries.AccountBalanceAsync(ctx, SystemAccounts.Cash));
        var inv = ctx.Invoices.Single(i => i.Id == id);
        Assert.Equal((InvoiceStatus.Paid, 15000L), (inv.Status, inv.AllocatedPaise));
        Assert.Equal(ctx.LedgerEntries.Sum(e => e.DebitPaise), ctx.LedgerEntries.Sum(e => e.CreditPaise));
    }

    [Fact]
    public async Task FR_INV_08_Bill_To_Customer_Posts_To_Customer_Tagged_With_Vehicle()
    {
        var form = TripForm();
        form.BillToType = PartyType.Customer;
        form.CustomerId = Customer;

        var id = (await Inv.CreateAsync(form)).Value;

        using var ctx = Db.NewContext();
        var partyLine = ctx.LedgerEntries.Single(e => e.AccountId == SystemAccounts.CustomerReceivables);
        Assert.Equal((Customer, TripVehicle), (partyLine.CustomerId!.Value, partyLine.VehicleId!.Value));
        Assert.Null(partyLine.SupplierId);
        Assert.Equal(0, await LedgerQueries.PartyBalanceAsync(ctx, PartyType.Supplier, Supplier));
        Assert.Equal("Meena Constructions", (await Inv.GetAsync(id))!.BillToName);
    }

    [Fact]
    public async Task Bill_To_Customer_Needs_A_Customer()
    {
        var form = TripForm();
        form.BillToType = PartyType.Customer;
        Assert.False((await Inv.CreateAsync(form)).Ok);
    }

    [Fact]
    public async Task FR_INV_04_Rate_Change_By_Any_Role_Needs_A_Reason()
    {
        var form = TripForm();
        form.Rate = 120;

        var result = await Inv.CreateAsync(form);

        Assert.False(result.Ok);
        Assert.Contains("reason", result.Error);
    }

    [Fact]
    public async Task FR_INV_04_Rate_Change_By_Admin_Needs_Reason_And_Is_Logged()
    {
        Me.User = Db.AddUser(UserRole.Admin);
        var form = TripForm();
        form.Rate = 120;
        Assert.False((await Inv.CreateAsync(form)).Ok);   // no reason

        form.RateReason = "Festival discount";
        var id = (await Inv.CreateAsync(form)).Value;

        var inv = (await Inv.GetAsync(id))!;
        Assert.Equal((12000L, RateSource.Overridden), (inv.AmountPaise, inv.RateSource));
        using var ctx = Db.NewContext();
        var log = ctx.ActivityLogs.Single(l => l.Action == "INVOICE_RATE_OVERRIDDEN");
        Assert.Equal("Festival discount", log.Reason);
    }

    [Fact]
    public async Task FR_VM_09_Inactive_Vehicle_Or_Supplier_Cannot_Be_Invoiced()
    {
        Me.User = Db.AddUser(UserRole.Admin);
        var v = (await Svc.Vehicles.GetAsync(TripVehicle))!;
        v.Status = VehicleStatus.Maintenance;
        await Svc.Vehicles.UpdateAsync(TripVehicle, v);
        Assert.False((await Inv.CreateAsync(TripForm())).Ok);

        var s = (await Svc.Suppliers.GetAsync(Supplier))!;
        s.Status = RecordStatus.Inactive;
        await Svc.Suppliers.UpdateAsync(Supplier, s);
        var result = await Inv.CreateAsync(new InvoiceForm { SalesPersonId = Seller, VehicleId = VolumeVehicle, Rate = 12.35m, VolumeKl = 1 });
        Assert.False(result.Ok);
        Assert.Contains("cannot be invoiced", result.Error);
    }

    [Fact]
    public async Task FR_INV_07_Nothing_Is_Saved_When_Posting_Fails()
    {
        using (var ctx = Db.NewContext())
        {
            ctx.BusinessDays.Add(new BusinessDay { Date = new DateOnly(2026, 11, 5), Status = BusinessDayStatus.Closed, Version = 1 });
            ctx.SaveChanges();
        }

        var result = await Inv.CreateAsync(TripForm());

        Assert.False(result.Ok);
        using var check = Db.NewContext();
        Assert.Empty(check.Invoices);
        Assert.Empty(check.Vouchers.Where(v => v.Type == VoucherType.Sales));
        Assert.Equal("INV-1", await Inv.PeekNextNumberAsync());
    }

    [Fact]
    public async Task FR_INV_12_Cancel_Reverses_Keeps_Number_And_Needs_Reason()
    {
        var id = (await Inv.CreateAsync(TripForm(InvoicePaymentMode.Cash))).Value;
        var inv = (await Inv.GetAsync(id))!;

        Assert.False((await Inv.CancelAsync(id, " ", inv.Version)).Ok);
        Assert.True((await Inv.CancelAsync(id, "Wrong vehicle", inv.Version)).Ok);

        var cancelled = (await Inv.GetAsync(id))!;
        Assert.Equal((InvoiceStatus.Cancelled, "INV-1"), (cancelled.Status, cancelled.DisplayNo));
        Assert.Equal("REV-1", cancelled.ReversalNo);
        using var ctx = Db.NewContext();
        Assert.Equal(0, await LedgerQueries.PartyBalanceAsync(ctx, PartyType.Supplier, Supplier));
        Assert.Equal(0, await LedgerQueries.AccountBalanceAsync(ctx, SystemAccounts.Cash));
        Assert.Equal(0, await LedgerQueries.AccountBalanceAsync(ctx, SystemAccounts.WaterSales));
        Assert.Contains(ctx.ActivityLogs, l => l.Action == "INVOICE_CANCELLED" && l.Reason == "Wrong vehicle");
        Assert.False((await Inv.CancelAsync(id, "again", cancelled.Version)).Ok);
    }

    [Fact]
    public async Task Every_Role_Can_Create_And_Cancel_Invoices()
    {
        foreach (var role in Enum.GetValues<UserRole>())
        {
            Me.User = Db.AddUser(role);
            var id = (await Inv.CreateAsync(TripForm())).Value;
            Assert.True((await Inv.CancelAsync(id, "test", 1)).Ok);
        }
    }

    [Fact]
    public async Task NFR_SEC_05_Not_Logged_In_Is_Refused()
    {
        Me.User = null;
        await Assert.ThrowsAsync<NotAllowedException>(() => Inv.CreateAsync(TripForm()));
    }

    [Fact]
    public async Task FR_INV_14_Previous_And_Next()
    {
        var a = (await Inv.CreateAsync(TripForm())).Value;
        var b = (await Inv.CreateAsync(TripForm())).Value;

        Assert.Equal(b, await Inv.NeighbourAsync(1, next: true));
        Assert.Equal(a, await Inv.NeighbourAsync(2, next: false));
        Assert.Null(await Inv.NeighbourAsync(2, next: true));
    }

    [Fact]
    public async Task Vehicle_With_Invoices_Cannot_Be_Deleted()
    {
        await Inv.CreateAsync(TripForm());
        Me.User = Db.AddUser(UserRole.Admin);

        Assert.False((await Svc.Vehicles.DeleteAsync(TripVehicle)).Ok);
    }

    [Theory]
    [InlineData(12505050, "Rupees One Lakh Twenty Five Thousand Fifty and Fifty Paise Only")]
    [InlineData(15000, "Rupees One Hundred Fifty Only")]
    [InlineData(1234567800, "Rupees One Crore Twenty Three Lakh Forty Five Thousand Six Hundred Seventy Eight Only")]
    public void Amount_In_Words_Uses_Indian_System(long paise, string words) =>
        Assert.Equal(words, WaterBilling.Services.Common.AmountInWords.Format(paise));
}

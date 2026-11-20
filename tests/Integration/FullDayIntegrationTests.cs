using WaterBilling.Data.Entities;
using WaterBilling.Services.Accounts;
using WaterBilling.Services.Admin;
using WaterBilling.Services.Billing;
using WaterBilling.Services.Ledger;
using WaterBilling.Services.Masters;
using WaterBilling.Services.Reports;
using WaterBilling.Services.Common;
using WaterBilling.Tests.Data;

namespace WaterBilling.Tests.Integration;

// Integration tests: one shop, one business day, every module working together through the real
// services on the real database schema (migrations, checks, triggers). Each step checks the
// figures the next module reads, so a break anywhere in the chain fails here.
//
//   Supplier Master -> Vehicle Master -> Sales Invoice (cash + credit, times, meters)
//   -> Invoice Display / Edit (corrected copy) -> Receipt (chosen invoices) -> Payment
//   -> Vehicle Trip Report -> Dashboard -> Day close -> Activity Log -> Recycle Bin
public class FullDayIntegrationTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly FakeCurrentUser _me;
    private TestServices S => new(_db, _me);
    private static readonly DateOnly Day = new(2026, 11, 5);   // the test clock: 05/11/2026 3:30 PM IST

    public FullDayIntegrationTests()
    {
        _me = new FakeCurrentUser(_db.AddUser(UserRole.Admin, "admin"));
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
    }

    private record Shop(long Kaveri, long Sangeetha, long A1, long A2, long A3, long Ravi, long Kumar);

    private async Task<Shop> OpenShopAsync()
    {
        await S.SalesPersons.AddAsync(new SalesPersonForm { Name = "Ravi" });
        await S.SalesPersons.AddAsync(new SalesPersonForm { Name = "Kumar" });
        var people = await S.SalesPersons.ListActiveAsync();
        var kaveri = (await S.Suppliers.CreateAsync(new SupplierForm { Name = "Kaveri Tankers", ShortName = "Kaveri", Phone = "9123456780", OpeningBalance = 500 })).Value;
        var sangeetha = (await S.Suppliers.CreateAsync(new SupplierForm { Name = "Sangeetha Water Supply", ShortName = "Sangeetha", Phone = "9876543210" })).Value;
        var a1 = (await S.Vehicles.CreateAsync(new VehicleForm { Code = "A1", VehicleNo = "TN 38 M 3", SupplierId = sangeetha, TankCapacityLitres = 12000, Rate = 150 })).Value;
        var a2 = (await S.Vehicles.CreateAsync(new VehicleForm { Code = "A2", VehicleNo = "TN 67 C", SupplierId = kaveri, TankCapacityLitres = 24000, Rate = 129.68m })).Value;
        var a3 = (await S.Vehicles.CreateAsync(new VehicleForm { Code = "A3", VehicleNo = "TN 45 B 2210", SupplierId = kaveri, TankCapacityLitres = 9000, Rate = 120 })).Value;
        _me.User = _db.AddUser(UserRole.SalesPerson, "sales");   // the shop works on the shared Sales login
        return new Shop(kaveri, sangeetha, a1, a2, a3, people.Single(p => p.Name == "Ravi").Id, people.Single(p => p.Name == "Kumar").Id);
    }

    private static void Balanced(AppDbContextHolder h)
    {
        Assert.Equal(h.Db.LedgerEntries.Sum(e => e.DebitPaise), h.Db.LedgerEntries.Sum(e => e.CreditPaise));
    }

    private sealed class AppDbContextHolder(WaterBilling.Data.AppDbContext db) : IDisposable
    {
        public WaterBilling.Data.AppDbContext Db { get; } = db;
        public void Dispose() => Db.Dispose();
    }

    private AppDbContextHolder Ctx() => new(_db.NewContext());

    [Fact]
    public async Task INT_01_A_Full_Business_Day_Adds_Up_In_Every_Module()
    {
        var shop = await OpenShopAsync();

        // ---- Sales Invoice: one cash trip, three credit trips (with times and meters) ----
        var cash = await S.Invoices.CreateAsync(new InvoiceForm { SalesPersonId = shop.Ravi, VehicleId = shop.A1, Rate = 150, PaymentMode = InvoicePaymentMode.Cash,
            InDate = Day, InTime = "09:10 AM", OutDate = Day, OutTime = "09:40 AM", Meter1Start = 28451.2m, Meter1End = 28463.2m });
        var c1 = await S.Invoices.CreateAsync(new InvoiceForm { SalesPersonId = shop.Ravi, VehicleId = shop.A2, Rate = 129.68m, PaymentMode = InvoicePaymentMode.Credit,
            InDate = Day, InTime = "11:30 AM", OutDate = Day, OutTime = "11:55 AM" });
        var c2 = await S.Invoices.CreateAsync(new InvoiceForm { SalesPersonId = shop.Kumar, VehicleId = shop.A3, Rate = 120, PaymentMode = InvoicePaymentMode.Credit,
            InDate = Day, InTime = "02:10 PM", OutDate = Day, OutTime = "02:35 PM" });
        var c3 = await S.Invoices.CreateAsync(new InvoiceForm { SalesPersonId = shop.Kumar, VehicleId = shop.A2, Rate = 129.68m, PaymentMode = InvoicePaymentMode.Credit });
        Assert.All(new[] { cash, c1, c2, c3 }, r => Assert.True(r.Ok, r.Error));
        Assert.Equal("5", await S.Invoices.PeekNextNumberAsync());

        // ---- Invoice Display: the list and the status pills ----
        var list = await S.InvoiceSearch.SearchAsync(new InvoiceFilter(Day, Day));
        Assert.Equal(4, list.Count);
        Assert.Equal(PayState.Paid, list.Single(r => r.Id == cash.Value).State);
        var morning = await S.InvoiceSearch.SearchAsync(new InvoiceFilter(Day, Day, TimeFrom: new TimeOnly(9, 0), TimeTo: new TimeOnly(12, 0)));
        Assert.Equal([cash.Value, c1.Value], morning.Select(r => r.Id).OrderBy(x => x));

        // ---- Edit: invoice 3 (A3) had the wrong rate (120 -> 125). Cancel + corrected copy, number 5 ----
        var old = (await S.Invoices.GetAsync(c2.Value))!;
        var edit = await S.Invoices.ReviseAsync(c2.Value, new InvoiceRevision { EditedBySalesPersonId = shop.Ravi, CapacityLitres = old.CapacityLitres, Rate = 125,
            InDate = Day, InTime = "02:10 PM", OutDate = Day, OutTime = "02:35 PM", Version = old.Version });
        Assert.True(edit.Ok, edit.Error);
        var copy = (await S.Invoices.GetAsync(edit.Value))!;
        Assert.Equal(("5", 12500L, "3"), (copy.DisplayNo, copy.AmountPaise, copy.ReplacesNo));
        Assert.Equal("5", (await S.Invoices.GetAsync(c2.Value))!.ReplacedByNo);
        Assert.Equal(InvoiceStatus.Cancelled, (await S.Invoices.GetAsync(c2.Value))!.Status);

        // ---- Receipt: Kaveri pays invoice 2 and the corrected 5 (not invoice 4), by UPI, no reference ----
        var pending = await S.Receipts.PendingInvoicesAsync(PartyType.Supplier, shop.Kaveri);
        Assert.Equal(["5", "4", "2"], pending.Select(p => p.DisplayNo));     // newest first; cancelled 3 is not pending
        var chosen = pending.Where(p => p.DisplayNo is "2" or "5").ToList();
        var receipt = await S.Receipts.CreateAsync(new ReceiptForm { PartyType = PartyType.Supplier, PartyId = shop.Kaveri, Mode = PaymentMode.Upi,
            Amount = Money.ToRupees(chosen.Sum(p => p.DuePaise)), InvoiceIds = chosen.Select(p => p.Id).ToList() });
        Assert.True(receipt.Ok, receipt.Error);
        Assert.Equal(PayState.Paid, PayStates.For(await InvoiceRowAsync(c1.Value)));
        Assert.Equal(PayState.Paid, PayStates.For(await InvoiceRowAsync(edit.Value)));
        Assert.Equal(PayState.NotPaid, PayStates.For(await InvoiceRowAsync(c3.Value)));
        var history = await S.Receipts.HistoryAsync(new ReceiptFilter(Day, Day));
        Assert.Equal(["2", "5"], history.Single().InvoicesPaid.OrderBy(x => x));

        // ---- Payment: diesel for A2 from the cash drawer, no reference ----
        var pay = await S.Payments.CreateAsync(new PaymentForm { AccountId = 10, Amount = 50, Mode = PaymentMode.Cash, VehicleId = shop.A2, PayTo = "Murugan Fuels" });
        Assert.True(pay.Ok, pay.Error);

        // ---- Ledger: balanced, supplier balances right ----
        using (var h = Ctx())
        {
            Balanced(h);
            // Kaveri: opening 500 + 129.68 + 125 + 129.68 - receipt (129.68 + 125) = 629.68
            Assert.Equal(62968, await LedgerQueries.PartyBalanceAsync(h.Db, PartyType.Supplier, shop.Kaveri));
            Assert.Equal(0, await LedgerQueries.PartyBalanceAsync(h.Db, PartyType.Supplier, shop.Sangeetha));   // cash trip nets to zero
            Assert.Equal(15000 - 5000, await LedgerQueries.AccountBalanceAsync(h.Db, SystemAccounts.Cash));
            Assert.Equal(25468, await LedgerQueries.AccountBalanceAsync(h.Db, SystemAccounts.Upi));
        }

        // ---- Vehicle Trip Report: equals the ledger, all three shapes ----
        var all = await S.TripReports.BuildAsync(new ReportFilter(Day, Day));
        Assert.Equal((4, 53436L), (all.Trips, all.BillPaise));                 // 150 + 129.68 + 125 + 129.68, cancelled 120 left out
        Assert.Equal(15000 + 25468, all.ReceivedPaise);
        using (var h = Ctx())
        {
            var sales = -h.Db.LedgerEntries.Where(e => e.AccountId == SystemAccounts.WaterSales).Sum(e => e.DebitPaise - e.CreditPaise);
            Assert.Equal(sales, all.BillPaise);
        }
        var kaveriOnly = await S.TripReports.BuildAsync(new ReportFilter(Day, Day, shop.Kaveri));
        Assert.Equal(["A2", "A3"], kaveriOnly.Suppliers.Single().Vehicles.Select(v => v.Code));
        Assert.Equal(12968, kaveriOnly.OutstandingPaise);                     // invoice 4 still unpaid
        var a2Only = await S.TripReports.BuildAsync(new ReportFilter(Day, Day, VehicleId: shop.A2));
        Assert.Equal(["2", "4"], a2Only.Lines.Select(l => l.InvoiceNo));
        var pdf = WaterBilling.Components.Pdf.ReportPdf.Generate(TripReportService.ToTable(all), new WaterBilling.Services.Common.CompanyInfo(), DateTime.UtcNow);
        Assert.True(pdf.Length > 1000);

        // ---- Dashboard ----
        var dash = await S.Dashboard.GetAsync();
        Assert.Equal((53436L, 53436L, 25468L, 5000L), (dash.MonthSalesPaise, dash.TodaySalesPaise, dash.TodayReceiptsPaise, dash.TodayPaymentsPaise));
        Assert.Equal(3, dash.RecentInvoices.Count);
        Assert.Equal("5", dash.RecentInvoices[0].DisplayNo);

        // ---- Day close: drawer has ₹100 (150 cash sale - 50 diesel) ----
        var dayView = await S.DaySales.GetDayAsync(Day);
        Assert.Equal(10000, dayView.Cash.ClosingPaise);
        Assert.True((await S.DaySales.CloseDayAsync(Day, 100m, null, dayView.Version)).Ok);
        Assert.False((await S.Invoices.CreateAsync(new InvoiceForm { SalesPersonId = shop.Ravi, VehicleId = shop.A1, Rate = 150 })).Ok);   // day is locked

        // ---- Activity Log: who did it ----
        _me.User = _db.AddUser(UserRole.Admin, "admin2");
        var log = await S.ActivityLog.SearchAsync(new ActivityFilter(Day, Day));
        Assert.Equal("Ravi", log.Single(l => l.Action == "INVOICE_EDITED").DoneByName);
        Assert.Equal(["Kumar", "Kumar", "Ravi", "Ravi"], log.Where(l => l.Action == "INVOICE_CREATED").Select(l => l.DoneByName).OrderBy(x => x));
        Assert.Contains(log, l => l.Action == "DAY_CLOSED");
    }

    [Fact]
    public async Task INT_02_Cancelling_A_Receipt_Reopens_Exactly_Its_Invoices_And_The_Report_Follows()
    {
        var shop = await OpenShopAsync();
        var first = (await S.Invoices.CreateAsync(new InvoiceForm { SalesPersonId = shop.Ravi, VehicleId = shop.A3, Rate = 120 })).Value;
        var second = (await S.Invoices.CreateAsync(new InvoiceForm { SalesPersonId = shop.Ravi, VehicleId = shop.A3, Rate = 120 })).Value;
        var receipt = (await S.Receipts.CreateAsync(new ReceiptForm { PartyType = PartyType.Supplier, PartyId = shop.Kaveri, Amount = 120, InvoiceIds = [second] })).Value;
        Assert.Equal(12000, (await S.TripReports.BuildAsync(new ReportFilter(Day, Day))).ReceivedPaise);

        var row = (await S.Receipts.HistoryAsync(new ReceiptFilter(Day, Day))).Single();
        Assert.True((await S.Receipts.CancelAsync(row.VoucherId, "Bounced UPI", row.Version)).Ok);

        Assert.Equal(PayState.NotPaid, PayStates.For(await InvoiceRowAsync(first)));
        Assert.Equal(PayState.NotPaid, PayStates.For(await InvoiceRowAsync(second)));
        Assert.Equal(0, (await S.TripReports.BuildAsync(new ReportFilter(Day, Day))).ReceivedPaise);
        Assert.Equal(["2", "1"], (await S.Receipts.PendingInvoicesAsync(PartyType.Supplier, shop.Kaveri)).Select(p => p.DisplayNo));
        using var h = Ctx();
        Balanced(h);
        Assert.Equal(receipt, h.Db.Vouchers.Single(v => v.Type == VoucherType.Receipt && v.Status == VoucherStatus.Cancelled).Id);
    }

    [Fact]
    public async Task INT_03_Editing_A_Part_Paid_Invoice_Moves_The_Payment_To_The_Corrected_Copy()
    {
        var shop = await OpenShopAsync();
        var inv = (await S.Invoices.CreateAsync(new InvoiceForm { SalesPersonId = shop.Ravi, VehicleId = shop.A1, Rate = 150 })).Value;
        await S.Receipts.CreateAsync(new ReceiptForm { PartyType = PartyType.Supplier, PartyId = shop.Sangeetha, Amount = 100 });   // part pays, oldest first
        Assert.Equal(PayState.PartPaid, PayStates.For(await InvoiceRowAsync(inv)));

        var old = (await S.Invoices.GetAsync(inv))!;
        var copyId = (await S.Invoices.ReviseAsync(inv, new InvoiceRevision { EditedBySalesPersonId = shop.Kumar, CapacityLitres = 15000, Rate = 160, Version = old.Version })).Value;

        Assert.Equal(PayState.PartPaid, PayStates.For(await InvoiceRowAsync(copyId)));
        var report = await S.TripReports.BuildAsync(new ReportFilter(Day, Day));
        Assert.Equal((16000L, 10000L, 6000L), (report.BillPaise, report.ReceivedPaise, report.OutstandingPaise));
        using var h = Ctx();
        Balanced(h);
        Assert.Equal(6000, await LedgerQueries.PartyBalanceAsync(h.Db, PartyType.Supplier, shop.Sangeetha));
        Assert.Equal(15000, h.Db.Invoices.Single(i => i.Id == copyId).CapacityLitres);
    }

    [Fact]
    public async Task INT_04_Deleted_Vehicle_Goes_To_The_Recycle_Bin_With_Who_Deleted_It()
    {
        var shop = await OpenShopAsync();
        var spare = (await S.Vehicles.CreateAsync(new VehicleForm { Code = "C78", VehicleNo = "TN 66 K 7807", SupplierId = shop.Sangeetha, TankCapacityLitres = 9000, Rate = 150 })).Value;

        Assert.False((await S.Vehicles.DeleteAsync(spare)).Ok);               // Sales login must say who
        Assert.True((await S.Vehicles.DeleteAsync(spare, shop.Kumar)).Ok);

        var bin = await S.Recovery.ListAsync();
        Assert.Contains(bin, r => r.Name == "TN 66 K 7807" && r.DeletedBy == "Kumar");
        _me.User = _db.AddUser(UserRole.Admin, "admin2");
        var log = await S.ActivityLog.SearchAsync(new ActivityFilter(Day, Day, DoneBy: "Kumar"));
        Assert.Contains(log, l => l.Action == "VEHICLE_DELETED");
    }

    private async Task<InvoiceRow> InvoiceRowAsync(long id) =>
        (await S.Invoices.ListAsync(DateOnly.MinValue, DateOnly.MaxValue)).Single(r => r.Id == id);

    public void Dispose() => _db.Dispose();
}

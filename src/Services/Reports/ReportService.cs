using Microsoft.EntityFrameworkCore;
using WaterBilling.Data;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Common;
using WaterBilling.Services.Security;

namespace WaterBilling.Services.Reports;

public enum ReportKind
{
    DailySales,
    DateWiseSales,
    SupplierWiseSales,
    VehicleOperational,
    SupplierOutstanding,
    CustomerOutstanding,
    TrialBalance,
    CashBook,
}

// Filters shared by every report (FR-RPT-06): date range, supplier, vehicle.
public sealed record ReportFilter(DateOnly From, DateOnly To, long? SupplierId = null, long? VehicleId = null);

// Builds each report as a ReportTable, so screen and PDF show the same thing.
// Sales reports read invoices; balance reports read the ledger. Tests check that the two
// agree with the ledger for the same filter (FR-RPT-07).
public class ReportService(IDbContextFactory<AppDbContext> dbFactory, ICurrentUser currentUser)
{
    public static string Title(ReportKind kind) => kind switch
    {
        ReportKind.DailySales => "Daily Sales",
        ReportKind.DateWiseSales => "Date-wise Sales",
        ReportKind.SupplierWiseSales => "Supplier-wise Sales",
        ReportKind.VehicleOperational => "Vehicle Operational Report",
        ReportKind.SupplierOutstanding => "Supplier-wise Outstanding",
        ReportKind.CustomerOutstanding => "Customer Outstanding",
        ReportKind.TrialBalance => "Trial Balance",
        _ => "Cash Book",
    };

    public async Task<ReportTable> BuildAsync(ReportKind kind, ReportFilter f)
    {
        await currentUser.RequireAsync(Permission.ReportsView);
        if (f.To < f.From) throw new ArgumentException("The To date is before the From date.");
        await using var db = await dbFactory.CreateDbContextAsync();
        var subtitle = await SubtitleAsync(db, kind, f);
        return kind switch
        {
            ReportKind.DailySales => await DailySalesAsync(db, f, subtitle),
            ReportKind.DateWiseSales => await DateWiseAsync(db, f, subtitle),
            ReportKind.SupplierWiseSales => await SupplierWiseAsync(db, f, subtitle),
            ReportKind.VehicleOperational => await VehicleOperationalAsync(db, f, subtitle),
            ReportKind.SupplierOutstanding => await OutstandingAsync(db, f, subtitle, PartyType.Supplier),
            ReportKind.CustomerOutstanding => await OutstandingAsync(db, f, subtitle, PartyType.Customer),
            ReportKind.TrialBalance => await TrialBalanceAsync(db, f, subtitle),
            _ => await CashBookAsync(db, f, subtitle),
        };
    }

    // ---------- Sales reports (from invoices, cancelled ones excluded) ----------

    private sealed record SaleRow(long Id, string No, DateOnly Date, long SupplierId, string Supplier, long VehicleId, string VehicleCode,
        string VehicleNo, string VehicleName, string BillTo, InvoicePaymentMode Mode, int Trips, long VolumeTenths, BillingBasis Basis,
        long RatePaise, long AmountPaise, long AllocatedPaise);

    private static Task<List<SaleRow>> SalesAsync(AppDbContext db, ReportFilter f) =>
        (from i in db.Invoices
         where i.Status != InvoiceStatus.Cancelled && i.InvoiceDate >= f.From && i.InvoiceDate <= f.To
             && (f.SupplierId == null || i.SupplierId == f.SupplierId) && (f.VehicleId == null || i.VehicleId == f.VehicleId)
         join v in db.Vehicles on i.VehicleId equals v.Id
         join s in db.Suppliers on i.SupplierId equals s.Id
         join c in db.Customers on i.CustomerId equals c.Id into cj
         from c in cj.DefaultIfEmpty()
         orderby i.InvoiceNo
         select new SaleRow(i.Id, i.DisplayNo, i.InvoiceDate, s.Id, s.Name, v.Id, v.Code, v.VehicleNo, v.Nickname,
             c == null ? s.Name : c.Name, i.PaymentMode, i.Trips, i.VolumeTenths, i.BillingBasis, i.RatePaise, i.AmountPaise, i.AllocatedPaise))
        .ToListAsync();

    private static async Task<ReportTable> DailySalesAsync(AppDbContext db, ReportFilter f, List<string> subtitle)
    {
        var rows = await SalesAsync(db, f);
        return new ReportTable(Title(ReportKind.DailySales), subtitle,
            [new("Date"), new("Invoice"), new("Vehicle", Width: 1.6f), new("Bill To", Width: 2), new("Mode", Width: 0.8f),
             new("Qty", true, 0.7f), new("Rate", true), new("Amount", true, 1.2f)],
            rows.Select(r => Row(IndianTime.FormatDate(r.Date), r.No, $"{r.VehicleCode} {r.VehicleNo}", r.BillTo, r.Mode.ToString(),
                r.Basis == BillingBasis.Trip ? r.Trips.ToString() : Volume.Format(r.VolumeTenths) + " kL", Money.Format(r.RatePaise), Money.Format(r.AmountPaise))).ToList(),
            [
                Row("", "", "", "Cash sales", "", "", "", Money.Format(rows.Where(r => r.Mode == InvoicePaymentMode.Cash).Sum(r => r.AmountPaise))),
                Row("", "", "", "Credit sales", "", "", "", Money.Format(rows.Where(r => r.Mode == InvoicePaymentMode.Credit).Sum(r => r.AmountPaise))),
                Row("", $"{rows.Count} invoices", "", "Total", "", rows.Sum(r => r.Trips).ToString(), "", Money.Format(rows.Sum(r => r.AmountPaise))),
            ], Landscape: true);
    }

    private static async Task<ReportTable> DateWiseAsync(AppDbContext db, ReportFilter f, List<string> subtitle)
    {
        var rows = await SalesAsync(db, f);
        var byDate = rows.GroupBy(r => r.Date).OrderBy(g => g.Key).ToList();
        return new ReportTable(Title(ReportKind.DateWiseSales), subtitle,
            [new("Date"), new("Invoices", true), new("Trips", true), new("Cash sales", true), new("Credit sales", true), new("Total", true)],
            byDate.Select(g => Row(IndianTime.FormatDate(g.Key), g.Count().ToString(), g.Sum(r => r.Trips).ToString(),
                Money.Format(g.Where(r => r.Mode == InvoicePaymentMode.Cash).Sum(r => r.AmountPaise)),
                Money.Format(g.Where(r => r.Mode == InvoicePaymentMode.Credit).Sum(r => r.AmountPaise)),
                Money.Format(g.Sum(r => r.AmountPaise)))).ToList(),
            [Row("Total", rows.Count.ToString(), rows.Sum(r => r.Trips).ToString(),
                Money.Format(rows.Where(r => r.Mode == InvoicePaymentMode.Cash).Sum(r => r.AmountPaise)),
                Money.Format(rows.Where(r => r.Mode == InvoicePaymentMode.Credit).Sum(r => r.AmountPaise)),
                Money.Format(rows.Sum(r => r.AmountPaise)))]);
    }

    private static async Task<ReportTable> SupplierWiseAsync(AppDbContext db, ReportFilter f, List<string> subtitle)
    {
        var rows = await SalesAsync(db, f);
        var bySupplier = rows.GroupBy(r => r.Supplier).OrderBy(g => g.Key).ToList();
        return new ReportTable(Title(ReportKind.SupplierWiseSales), subtitle,
            [new("Supplier", Width: 2.5f), new("Invoices", true), new("Trips", true), new("Volume (kL)", true), new("Cash sales", true), new("Credit sales", true), new("Total", true)],
            bySupplier.Select(g => Row(g.Key, g.Count().ToString(), g.Sum(r => r.Trips).ToString(), Volume.Format(g.Sum(r => r.VolumeTenths)),
                Money.Format(g.Where(r => r.Mode == InvoicePaymentMode.Cash).Sum(r => r.AmountPaise)),
                Money.Format(g.Where(r => r.Mode == InvoicePaymentMode.Credit).Sum(r => r.AmountPaise)),
                Money.Format(g.Sum(r => r.AmountPaise)))).ToList(),
            [Row("Total", rows.Count.ToString(), rows.Sum(r => r.Trips).ToString(), Volume.Format(rows.Sum(r => r.VolumeTenths)),
                Money.Format(rows.Where(r => r.Mode == InvoicePaymentMode.Cash).Sum(r => r.AmountPaise)),
                Money.Format(rows.Where(r => r.Mode == InvoicePaymentMode.Credit).Sum(r => r.AmountPaise)),
                Money.Format(rows.Sum(r => r.AmountPaise)))]);
    }

    // FR-RPT-02: vehicle, registration, total volume, unit rate, total bill, received, outstanding,
    // with a subtotal per supplier and a grand total.
    private static async Task<ReportTable> VehicleOperationalAsync(AppDbContext db, ReportFilter f, List<string> subtitle)
    {
        var rows = await SalesAsync(db, f);
        var vehicleRates = await db.Vehicles.ToDictionaryAsync(v => v.Id, v => (v.RatePaise, v.BillingBasis));
        var table = new List<IReadOnlyList<string>>();
        foreach (var supplier in rows.GroupBy(r => r.Supplier).OrderBy(g => g.Key))
        {
            foreach (var vehicle in supplier.GroupBy(r => r.VehicleId).OrderBy(g => g.First().VehicleCode.Length).ThenBy(g => g.First().VehicleCode))
            {
                var first = vehicle.First();
                var (rate, basis) = vehicleRates[vehicle.Key];
                var bill = vehicle.Sum(r => r.AmountPaise);
                var received = vehicle.Sum(r => r.AllocatedPaise);
                table.Add(Row(first.VehicleName, first.VehicleNo, Volume.Format(vehicle.Sum(r => r.VolumeTenths)),
                    $"{Money.Format(rate)}/{(basis == BillingBasis.Trip ? "trip" : "kL")}", Money.Format(bill), Money.Format(received), Money.Format(bill - received)));
            }
            table.Add(Row($"Subtotal - {supplier.Key}", "", Volume.Format(supplier.Sum(r => r.VolumeTenths)), "",
                Money.Format(supplier.Sum(r => r.AmountPaise)), Money.Format(supplier.Sum(r => r.AllocatedPaise)),
                Money.Format(supplier.Sum(r => r.AmountPaise - r.AllocatedPaise))));
        }
        return new ReportTable(Title(ReportKind.VehicleOperational), subtitle,
            [new("Vehicle Name", Width: 2.2f), new("Registration No."), new("Total Volume (kL)", true), new("Unit Rate", true, 1.1f),
             new("Total Bill", true), new("Received", true), new("Outstanding", true)],
            table,
            [Row("Grand total", "", Volume.Format(rows.Sum(r => r.VolumeTenths)), "", Money.Format(rows.Sum(r => r.AmountPaise)),
                Money.Format(rows.Sum(r => r.AllocatedPaise)), Money.Format(rows.Sum(r => r.AmountPaise - r.AllocatedPaise)))],
            Landscape: true);
    }

    // ---------- Balance reports (from the ledger) ----------

    // Opening (before From), period debits and credits, closing (to To) per party.
    private static async Task<ReportTable> OutstandingAsync(AppDbContext db, ReportFilter f, List<string> subtitle, PartyType type)
    {
        var account = SystemAccounts.ForParty(type);
        var lines = await db.LedgerEntries.Where(e => e.AccountId == account && e.EntryDate <= f.To
                && (f.VehicleId == null || e.VehicleId == f.VehicleId))
            .Select(e => new { PartyId = type == PartyType.Supplier ? e.SupplierId : e.CustomerId, e.EntryDate, e.DebitPaise, e.CreditPaise })
            .ToListAsync();
        var names = type == PartyType.Supplier
            ? await db.Suppliers.ToDictionaryAsync(s => s.Id, s => s.Name)
            : await db.Customers.ToDictionaryAsync(c => c.Id, c => c.Name);
        if (type == PartyType.Supplier && f.SupplierId is { } only) names = names.Where(n => n.Key == only).ToDictionary();

        var data = names.OrderBy(n => n.Value).Select(n =>
        {
            var mine = lines.Where(l => l.PartyId == n.Key).ToList();
            var opening = mine.Where(l => l.EntryDate < f.From).Sum(l => l.DebitPaise - l.CreditPaise);
            var inPeriod = mine.Where(l => l.EntryDate >= f.From).ToList();
            var dr = inPeriod.Sum(l => l.DebitPaise);
            var cr = inPeriod.Sum(l => l.CreditPaise);
            return (Name: n.Value, Opening: opening, Dr: dr, Cr: cr, Closing: opening + dr - cr, Any: mine.Count > 0);
        }).Where(x => x.Any).ToList();

        return new ReportTable(Title(type == PartyType.Supplier ? ReportKind.SupplierOutstanding : ReportKind.CustomerOutstanding), subtitle,
            [new(type == PartyType.Supplier ? "Supplier" : "Customer", Width: 2.5f), new("Opening", true), new("Debit (billed)", true),
             new("Credit (received)", true), new("Outstanding", true)],
            data.Select(d => Row(d.Name, Bal(d.Opening), Money.Format(d.Dr), Money.Format(d.Cr), Bal(d.Closing))).ToList(),
            [Row("Total", Bal(data.Sum(d => d.Opening)), Money.Format(data.Sum(d => d.Dr)), Money.Format(data.Sum(d => d.Cr)), Bal(data.Sum(d => d.Closing)))]);
    }

    // Every ledger's balance up to To. Total debits must equal total credits.
    private static async Task<ReportTable> TrialBalanceAsync(AppDbContext db, ReportFilter f, List<string> subtitle)
    {
        var balances = await db.LedgerEntries.Where(e => e.EntryDate <= f.To).GroupBy(e => e.AccountId)
            .Select(g => new { g.Key, Balance = g.Sum(e => e.DebitPaise - e.CreditPaise) }).ToListAsync();
        var accounts = await db.Accounts.ToDictionaryAsync(a => a.Id);
        var rows = balances.Where(b => b.Balance != 0).OrderBy(b => accounts[b.Key].Type).ThenBy(b => accounts[b.Key].Name).ToList();
        return new ReportTable(Title(ReportKind.TrialBalance), subtitle,
            [new("Ledger", Width: 2.5f), new("Type"), new("Debit", true), new("Credit", true)],
            rows.Select(b => Row(accounts[b.Key].Name, accounts[b.Key].Type.ToString(),
                b.Balance > 0 ? Money.Format(b.Balance) : "", b.Balance < 0 ? Money.Format(-b.Balance) : "")).ToList(),
            [Row("Total", "", Money.Format(rows.Where(b => b.Balance > 0).Sum(b => b.Balance)), Money.Format(-rows.Where(b => b.Balance < 0).Sum(b => b.Balance)))]);
    }

    private static async Task<ReportTable> CashBookAsync(AppDbContext db, ReportFilter f, List<string> subtitle)
    {
        var cash = db.LedgerEntries.Where(e => e.AccountId == SystemAccounts.Cash);
        var opening = await cash.Where(e => e.EntryDate < f.From).SumAsync(e => e.DebitPaise - e.CreditPaise);
        var lines = await cash.Where(e => e.EntryDate >= f.From && e.EntryDate <= f.To).OrderBy(e => e.PostingSeq).ToListAsync();
        var balance = opening;
        var rows = new List<IReadOnlyList<string>> { Row(IndianTime.FormatDate(f.From), "Opening Balance b/f", "", "", "", Bal(opening)) };
        foreach (var l in lines)
        {
            balance += l.DebitPaise - l.CreditPaise;
            rows.Add(Row(IndianTime.FormatDate(l.EntryDate), l.Particulars, l.ReferenceNo,
                l.DebitPaise > 0 ? Money.Format(l.DebitPaise) : "", l.CreditPaise > 0 ? Money.Format(l.CreditPaise) : "", Bal(balance)));
        }
        return new ReportTable(Title(ReportKind.CashBook), subtitle,
            [new("Date"), new("Particulars", Width: 3.2f), new("Voucher"), new("Receipts (Dr)", true), new("Payments (Cr)", true), new("Balance", true)],
            rows,
            [
                Row("", "Period totals", "", Money.Format(lines.Sum(l => l.DebitPaise)), Money.Format(lines.Sum(l => l.CreditPaise)), ""),
                Row(IndianTime.FormatDate(f.To), "Closing Balance c/f", "", "", "", Bal(balance)),
            ], Landscape: true);
    }

    // ---------- helpers ----------

    private static async Task<List<string>> SubtitleAsync(AppDbContext db, ReportKind kind, ReportFilter f)
    {
        var lines = new List<string>
        {
            kind == ReportKind.TrialBalance
                ? $"As on {IndianTime.FormatDate(f.To)}"
                : $"Period {IndianTime.FormatDate(f.From)} to {IndianTime.FormatDate(f.To)}",
        };
        var applies = kind is not (ReportKind.TrialBalance or ReportKind.CashBook or ReportKind.CustomerOutstanding);
        if (applies && f.SupplierId is { } s)
            lines.Add("Supplier: " + await db.Suppliers.Where(x => x.Id == s).Select(x => x.Name).SingleOrDefaultAsync());
        if (kind is not (ReportKind.TrialBalance or ReportKind.CashBook) && f.VehicleId is { } v)
            lines.Add("Vehicle: " + await db.Vehicles.Where(x => x.Id == v).Select(x => x.Code + " " + x.VehicleNo).SingleOrDefaultAsync());
        if (!applies && (f.SupplierId is not null || (f.VehicleId is not null && kind is ReportKind.TrialBalance or ReportKind.CashBook)))
            lines.Add("(Supplier/vehicle filters do not apply to this report.)");
        return lines;
    }

    private static IReadOnlyList<string> Row(params string[] cells) => cells;

    private static string Bal(long p) => $"{Money.Format(Math.Abs(p))} {(p > 0 ? "Dr" : p < 0 ? "Cr" : "")}".Trim();
}

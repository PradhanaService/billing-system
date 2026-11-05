using Microsoft.EntityFrameworkCore;
using WaterBilling.Data;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Common;
using WaterBilling.Services.Security;

namespace WaterBilling.Services.Accounts;

public enum StatementKind { Supplier, Customer, Vehicle, Account }

public sealed record StatementRequest(StatementKind Kind, long Id, DateOnly From, DateOnly To, bool SortByValueDate = false);

public sealed record StatementLine(DateOnly Date, DateOnly ValueDate, string Particulars, string ReferenceNo,
    long DebitPaise, long CreditPaise, long BalancePaise);

// FR-LED-05: Opening Balance b/f, entries, period Debit and Credit totals, Closing Balance c/f.
public sealed record Statement(string Title, string Subject, DateOnly From, DateOnly To, bool SortByValueDate,
    long OpeningPaise, IReadOnlyList<StatementLine> Lines, long TotalDebitPaise, long TotalCreditPaise, long ClosingPaise);

public sealed record StatementSubject(StatementKind Kind, long Id, string Name);

// One code path for every statement. They all read the same ledger lines, only the filter
// differs, so a supplier, its vehicles and the trial balance can never disagree (principle 2).
public class StatementService(IDbContextFactory<AppDbContext> dbFactory, ICurrentUser currentUser)
{
    public async Task<IReadOnlyList<StatementSubject>> SubjectsAsync(StatementKind kind)
    {
        await currentUser.RequireAsync(Permission.StatementsView);
        await using var db = await dbFactory.CreateDbContextAsync();
        return kind switch
        {
            StatementKind.Supplier => await db.Suppliers.OrderBy(s => s.Name).Select(s => new StatementSubject(kind, s.Id, s.Name)).ToListAsync(),
            StatementKind.Customer => await db.Customers.OrderBy(c => c.Name).Select(c => new StatementSubject(kind, c.Id, c.Name)).ToListAsync(),
            StatementKind.Vehicle => await db.Vehicles.Where(v => !v.IsDeleted).OrderBy(v => v.Code.Length).ThenBy(v => v.Code)
                .Select(v => new StatementSubject(kind, v.Id, v.Code + " - " + v.VehicleNo + " (" + v.Nickname + ")")).ToListAsync(),
            _ => await db.Accounts.OrderBy(a => a.Name).Select(a => new StatementSubject(kind, a.Id, a.Name)).ToListAsync(),
        };
    }

    public async Task<Statement?> GetAsync(StatementRequest req)
    {
        await currentUser.RequireAsync(Permission.StatementsView);
        if (req.To < req.From) return null;
        await using var db = await dbFactory.CreateDbContextAsync();

        IQueryable<LedgerEntry> scope;
        string title, subject;
        switch (req.Kind)
        {
            case StatementKind.Supplier:
                subject = await db.Suppliers.Where(s => s.Id == req.Id).Select(s => s.Name).SingleOrDefaultAsync() ?? "";
                scope = db.LedgerEntries.Where(e => e.AccountId == SystemAccounts.SupplierReceivables && e.SupplierId == req.Id);
                title = "Supplier Statement";
                break;
            case StatementKind.Customer:
                subject = await db.Customers.Where(c => c.Id == req.Id).Select(c => c.Name).SingleOrDefaultAsync() ?? "";
                scope = db.LedgerEntries.Where(e => e.AccountId == SystemAccounts.CustomerReceivables && e.CustomerId == req.Id);
                title = "Customer Statement";
                break;
            case StatementKind.Vehicle:
                // FR-LED-07: the vehicle ledger is the supplier's lines tagged with this vehicle.
                // (Trips billed to a customer appear on that customer's statement.)
                var v = await db.Vehicles.Include(x => x.Supplier).SingleOrDefaultAsync(x => x.Id == req.Id);
                subject = v is null ? "" : $"{v.Code} - {v.VehicleNo} ({v.Nickname}), supplier {v.Supplier!.Name}";
                var supplierId = v?.SupplierId;
                scope = db.LedgerEntries.Where(e => e.AccountId == SystemAccounts.SupplierReceivables && e.SupplierId == supplierId && e.VehicleId == req.Id);
                title = "Vehicle Statement";
                break;
            default:
                subject = await db.Accounts.Where(a => a.Id == req.Id).Select(a => a.Name).SingleOrDefaultAsync() ?? "";
                scope = db.LedgerEntries.Where(e => e.AccountId == req.Id);
                title = req.Id == SystemAccounts.Cash ? "Cash Book" : "Ledger Statement";
                break;
        }
        if (subject == "") return null;

        // Date order uses the posting Date; "Sort by Value Date" uses the Value Date (FR-LED-06).
        long opening;
        List<LedgerEntry> lines;
        if (req.SortByValueDate)
        {
            opening = await scope.Where(e => e.ValueDate < req.From).SumAsync(e => e.DebitPaise - e.CreditPaise);
            lines = await scope.Where(e => e.ValueDate >= req.From && e.ValueDate <= req.To)
                .OrderBy(e => e.ValueDate).ThenBy(e => e.PostingSeq).ToListAsync();
        }
        else
        {
            opening = await scope.Where(e => e.EntryDate < req.From).SumAsync(e => e.DebitPaise - e.CreditPaise);
            lines = await scope.Where(e => e.EntryDate >= req.From && e.EntryDate <= req.To)
                .OrderBy(e => e.PostingSeq).ToListAsync();
        }

        var balance = opening;
        var result = new List<StatementLine>(lines.Count);
        foreach (var e in lines)
        {
            balance += e.DebitPaise - e.CreditPaise;
            result.Add(new StatementLine(e.EntryDate, e.ValueDate, e.Particulars, e.ReferenceNo, e.DebitPaise, e.CreditPaise, balance));
        }
        return new Statement(title, subject, req.From, req.To, req.SortByValueDate, opening, result,
            lines.Sum(e => e.DebitPaise), lines.Sum(e => e.CreditPaise), balance);
    }

    // The same statement as a printable table (FR-LED-11: same columns in PDF).
    public static ReportTable ToTable(Statement s)
    {
        static string Bal(long p) => $"{Money.Format(Math.Abs(p))} {(p > 0 ? "Dr" : p < 0 ? "Cr" : "")}".Trim();
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { IndianTime.FormatDate(s.From), "", "Opening Balance b/f", "", "", "", Bal(s.OpeningPaise) },
        };
        rows.AddRange(s.Lines.Select(l => (IReadOnlyList<string>)new[]
        {
            IndianTime.FormatDate(l.Date), IndianTime.FormatDate(l.ValueDate), l.Particulars, l.ReferenceNo,
            l.DebitPaise > 0 ? Money.Format(l.DebitPaise) : "", l.CreditPaise > 0 ? Money.Format(l.CreditPaise) : "", Bal(l.BalancePaise),
        }));
        return new ReportTable(
            s.Title,
            [s.Subject, $"Period {IndianTime.FormatDate(s.From)} to {IndianTime.FormatDate(s.To)}" + (s.SortByValueDate ? " (by Value Date)" : "")],
            [new("Date", Width: 1), new("Value Date", Width: 1), new("Particulars", Width: 3.2f), new("Voucher / Ref No", Width: 1.2f),
             new("Debit (Dr)", true, 1.2f), new("Credit (Cr)", true, 1.2f), new("Balance", true, 1.4f)],
            rows,
            [
                new[] { "", "", "Period totals", "", Money.Format(s.TotalDebitPaise), Money.Format(s.TotalCreditPaise), "" },
                new[] { IndianTime.FormatDate(s.To), "", "Closing Balance c/f", "", "", "", Bal(s.ClosingPaise) },
            ],
            Landscape: true);
    }
}

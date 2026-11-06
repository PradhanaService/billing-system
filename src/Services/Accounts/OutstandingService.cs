using Microsoft.EntityFrameworkCore;
using WaterBilling.Data;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Security;

namespace WaterBilling.Services.Accounts;

// Who has to give money: the detail behind the Dashboard's "Outstanding amount".
// Balances are read from the accounts (the same entries as the Dashboard and the statements).

public enum OutstandingFilter { All, Suppliers, Customers }

// One party that does not have a zero balance.
// BalancePaise: positive (Dr) = they owe us; negative (Cr) = advance received, or we owe them.
public sealed record OutstandingRow(PartyType PartyType, long PartyId, string Name, string Phone,
    long BalancePaise, int OpenInvoices, DateOnly? OldestUnpaid);

// The whole list plus its totals. NetPaise for the "All" filter equals the dashboard's Outstanding amount.
public sealed record OutstandingSummary(IReadOnlyList<OutstandingRow> Rows, long OwedToUsPaise, long WeOwePaise, long NetPaise);

// One invoice of a party that is not fully paid yet.
public sealed record OpenInvoiceRow(long InvoiceId, string InvoiceNo, DateOnly Date, string VehicleNo,
    long AmountPaise, long PaidPaise, long DuePaise);

// "Who has to give money": the detail behind the dashboard's Outstanding amount card.
// Balances come from the ledger (the same source as the card and the statements), so the
// figures can never disagree. Unpaid invoices come from the invoices and their receipt allocations.
public class OutstandingService(IDbContextFactory<AppDbContext> dbFactory, ICurrentUser currentUser)
{
    public async Task<OutstandingSummary> GetAsync(OutstandingFilter filter = OutstandingFilter.All, string? search = null)
    {
        await currentUser.RequireAsync(Permission.StatementsView);
        await using var db = await dbFactory.CreateDbContextAsync();

        var rows = new List<OutstandingRow>();
        if (filter != OutstandingFilter.Customers) rows.AddRange(await PartyRowsAsync(db, PartyType.Supplier));
        if (filter != OutstandingFilter.Suppliers) rows.AddRange(await PartyRowsAsync(db, PartyType.Customer));

        if (!string.IsNullOrWhiteSpace(search))
            rows = rows.Where(r => r.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

        // Biggest amounts first, whichever side they are on.
        rows = rows.OrderByDescending(r => Math.Abs(r.BalancePaise)).ThenBy(r => r.Name).ToList();

        var owedToUs = rows.Where(r => r.BalancePaise > 0).Sum(r => r.BalancePaise);
        var weOwe = -rows.Where(r => r.BalancePaise < 0).Sum(r => r.BalancePaise);
        return new OutstandingSummary(rows, owedToUs, weOwe, owedToUs - weOwe);
    }

    public async Task<IReadOnlyList<OpenInvoiceRow>> OpenInvoicesAsync(PartyType partyType, long partyId)
    {
        await currentUser.RequireAsync(Permission.StatementsView);
        await using var db = await dbFactory.CreateDbContextAsync();
        return await (from i in OpenInvoices(db, partyType)
                      where (partyType == PartyType.Supplier ? i.SupplierId : i.CustomerId) == partyId
                      join v in db.Vehicles on i.VehicleId equals v.Id
                      orderby i.InvoiceNo
                      select new OpenInvoiceRow(i.Id, i.DisplayNo, i.InvoiceDate, v.VehicleNo,
                          i.AmountPaise, i.AllocatedPaise, i.AmountPaise - i.AllocatedPaise))
                     .ToListAsync();
    }

    // Balance per party from its control ledger, plus how many invoices are still unpaid.
    private static async Task<List<OutstandingRow>> PartyRowsAsync(AppDbContext db, PartyType type)
    {
        var account = SystemAccounts.ForParty(type);
        var balances = await db.LedgerEntries
            .Where(e => e.AccountId == account)
            .GroupBy(e => type == PartyType.Supplier ? e.SupplierId : e.CustomerId)
            .Select(g => new { PartyId = g.Key, Balance = g.Sum(e => e.DebitPaise - e.CreditPaise) })
            .Where(x => x.PartyId != null && x.Balance != 0)
            .ToDictionaryAsync(x => x.PartyId!.Value, x => x.Balance);
        if (balances.Count == 0) return [];

        var ids = balances.Keys.ToList();
        var parties = type == PartyType.Supplier
            ? await db.Suppliers.Where(s => ids.Contains(s.Id)).Select(s => new { s.Id, s.Name, s.Phone }).ToListAsync()
            : await db.Customers.Where(c => ids.Contains(c.Id)).Select(c => new { c.Id, c.Name, c.Phone }).ToListAsync();

        var open = await OpenInvoices(db, type)
            .GroupBy(i => type == PartyType.Supplier ? i.SupplierId : i.CustomerId!.Value)
            .Select(g => new { PartyId = g.Key, Count = g.Count(), Oldest = g.Min(i => i.InvoiceDate) })
            .ToDictionaryAsync(x => x.PartyId);

        return parties.Select(p =>
        {
            var unpaid = open.GetValueOrDefault(p.Id);
            return new OutstandingRow(type, p.Id, p.Name, p.Phone, balances[p.Id], unpaid?.Count ?? 0, unpaid?.Oldest);
        }).ToList();
    }

    // Credit invoices billed to this kind of party that still have something to pay.
    private static IQueryable<Invoice> OpenInvoices(AppDbContext db, PartyType type) =>
        db.Invoices.Where(i => i.BillToType == type
            && (i.Status == InvoiceStatus.Posted || i.Status == InvoiceStatus.PartPaid));
}

using Microsoft.EntityFrameworkCore;
using WaterBilling.Data;
using WaterBilling.Data.Entities;

namespace WaterBilling.Services.Accounts;

// Advance payments (client scenario, Nov 2026): a supplier owes ₹8,000, pays ₹10,000 and asks us to keep
// the extra ₹2,000 for future trips.
//   - The receipt pays the ticked invoices; the rest stays on the supplier's account ("advance").
//     In the ledger the whole ₹10,000 is credited to the supplier when it is received, so no entry is
//     ever needed later: the supplier's balance simply shows ₹2,000 in their favour.
//   - Each new invoice for that supplier is paid from the advance at once (oldest receipt first),
//     so it shows Paid, or Part paid when the advance runs out.
//   - Advance available = money of active receipts not yet matched to invoices, less any refunds paid
//     back to the party (Payment > refund).
public static class Advances
{
    public static async Task<long> AvailableAsync(AppDbContext db, PartyType type, long partyId)
    {
        var receipts = await ReceiptsWithMoneyLeftAsync(db, type, partyId);
        var refunds = await db.Vouchers.Where(v => v.Type == VoucherType.Payment && v.Status == VoucherStatus.Posted
                && v.PurposeAccountId == SystemAccounts.ForParty(type)
                && (type == PartyType.Supplier ? v.SupplierId == partyId : v.CustomerId == partyId))
            .SumAsync(v => (long?)v.AmountPaise) ?? 0;
        return Math.Max(0, receipts.Sum(r => r.Left) - refunds);
    }

    // Pays as much of the invoice as the party's advance allows. Changes are added to db; the caller saves.
    // Returns the amount taken from the advance.
    public static async Task<long> ApplyToAsync(AppDbContext db, Invoice invoice)
    {
        if (invoice.PaymentMode != InvoicePaymentMode.Credit || invoice.Status == InvoiceStatus.Cancelled) return 0;
        var partyId = invoice.BillToType == PartyType.Supplier ? invoice.SupplierId : invoice.CustomerId!.Value;
        var available = await AvailableAsync(db, invoice.BillToType, partyId);
        var due = invoice.AmountPaise - invoice.AllocatedPaise;
        var toTake = Math.Min(available, due);
        if (toTake <= 0) return 0;

        var taken = 0L;
        foreach (var (voucherId, left) in await ReceiptsWithMoneyLeftAsync(db, invoice.BillToType, partyId))
        {
            if (taken == toTake) break;
            var part = Math.Min(left, toTake - taken);
            db.ReceiptAllocations.Add(new ReceiptAllocation
            {
                VoucherId = voucherId, InvoiceId = invoice.Id, VehicleId = invoice.VehicleId, AmountPaise = part, FromAdvance = true,
            });
            taken += part;
        }
        invoice.AllocatedPaise += taken;
        invoice.Status = invoice.AllocatedPaise >= invoice.AmountPaise ? InvoiceStatus.Paid : InvoiceStatus.PartPaid;
        return taken;
    }

    // Active receipts of the party with money not yet matched to invoices, oldest first.
    private static async Task<List<(long VoucherId, long Left)>> ReceiptsWithMoneyLeftAsync(AppDbContext db, PartyType type, long partyId)
    {
        var receipts = await db.Vouchers.Where(v => v.Type == VoucherType.Receipt && v.Status == VoucherStatus.Posted
                && v.PartyType == type && (type == PartyType.Supplier ? v.SupplierId == partyId : v.CustomerId == partyId))
            .OrderBy(v => v.Number).Select(v => new { v.Id, v.AmountPaise }).ToListAsync();
        var ids = receipts.Select(r => r.Id).ToList();
        // Allocations added in this same unit of work (not saved yet) count too.
        var saved = await db.ReceiptAllocations.Where(a => ids.Contains(a.VoucherId) && a.IsActive)
            .GroupBy(a => a.VoucherId).Select(g => new { g.Key, Sum = g.Sum(a => a.AmountPaise) }).ToDictionaryAsync(x => x.Key, x => x.Sum);
        var pending = db.ChangeTracker.Entries<ReceiptAllocation>().Where(e => e.State == EntityState.Added && e.Entity.IsActive)
            .GroupBy(e => e.Entity.VoucherId).ToDictionary(g => g.Key, g => g.Sum(e => e.Entity.AmountPaise));
        return receipts.Select(r => (r.Id, r.AmountPaise - saved.GetValueOrDefault(r.Id) - pending.GetValueOrDefault(r.Id)))
            .Where(r => r.Item2 > 0).ToList();
    }
}

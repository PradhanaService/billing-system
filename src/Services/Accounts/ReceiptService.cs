using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using WaterBilling.Data;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Common;
using WaterBilling.Services.Ledger;
using WaterBilling.Services.Masters;
using WaterBilling.Services.Security;

namespace WaterBilling.Services.Accounts;

// Receipt Voucher form (FR-TXN-01).
public sealed class ReceiptForm
{
    public PartyType PartyType { get; set; } = PartyType.Supplier;

    [Range(1, long.MaxValue, ErrorMessage = "Choose the party.")]
    public long PartyId { get; set; }

    public PaymentMode Mode { get; set; } = PaymentMode.Cash;

    [Rupees(AllowZero = false)] public decimal Amount { get; set; }

    [StringLength(40)] public string? ReferenceNo { get; set; }
    [StringLength(300)] public string? Narration { get; set; }
}

// One invoice a receipt pays (or would pay).
public sealed record AllocationLine(long InvoiceId, string InvoiceNo, DateOnly InvoiceDate, long VehicleId, string VehicleNo,
    long OutstandingBeforePaise, long AllocatedPaise);

public sealed record ReceiptRow(long VoucherId, string DisplayNo, DateOnly Date, PartyType PartyType, string PartyName,
    PaymentMode Mode, string? ReferenceNo, long AmountPaise, long AllocatedPaise, VoucherStatus Status, string? CancelReason, int Version);

public sealed record ReceiptFilter(DateOnly From, DateOnly To, PartyType? PartyType = null, long? PartyId = null,
    PaymentMode? Mode = null, VoucherStatus? Status = null);

public class ReceiptService(IDbContextFactory<AppDbContext> dbFactory, ICurrentUser currentUser, TimeProvider clock, LedgerPoster poster)
{
    // FR-TXN-07: the party's balance now. The screen shows "after" as now minus the amount typed.
    public async Task<long> PartyBalanceAsync(PartyType type, long partyId)
    {
        await currentUser.RequireAsync(Permission.ReceiptCreate);
        await using var db = await dbFactory.CreateDbContextAsync();
        return await LedgerQueries.PartyBalanceAsync(db, type, partyId);
    }

    public async Task<string> PeekNextNumberAsync()
    {
        await currentUser.RequireAsync(Permission.ReceiptCreate);
        await using var db = await dbFactory.CreateDbContextAsync();
        var s = await db.NumberSeries.SingleAsync(x => x.Code == "RECEIPT");
        return s.Prefix + s.NextNo;
    }

    // Shows which invoices the amount would pay, before saving.
    public async Task<IReadOnlyList<AllocationLine>> PreviewAllocationAsync(PartyType type, long partyId, long amountPaise)
    {
        await currentUser.RequireAsync(Permission.ReceiptCreate);
        await using var db = await dbFactory.CreateDbContextAsync();
        return Allocate(await OpenInvoicesAsync(db, type, partyId), amountPaise);
    }

    public async Task<Result<long>> CreateAsync(ReceiptForm form)
    {
        var me = await currentUser.RequireAsync(Permission.ReceiptCreate);
        if (FormValidator.FirstError(form) is { } error) return Result<long>.Fail(error);
        if (form.Mode != PaymentMode.Cash && !me.Has(Permission.ReceiptBankUpi))
            return Result<long>.Fail("Your role can record cash receipts only.");
        if (form.Mode != PaymentMode.Cash && string.IsNullOrWhiteSpace(form.ReferenceNo))
            return Result<long>.Fail("Reference No. is required for Bank and UPI receipts.");   // FR-TXN-03

        var amount = Money.FromRupees(form.Amount);
        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var partyName = await PartyNameAsync(db, form.PartyType, form.PartyId);
        if (partyName is null) return Result<long>.Fail("Party not found.");

        // FR-TXN-09: oldest open invoice first. Anything left over stays unallocated.
        var open = await OpenInvoicesAsync(db, form.PartyType, form.PartyId);
        var allocations = Allocate(open, amount);

        long? supplier = form.PartyType == PartyType.Supplier ? form.PartyId : null;
        long? customer = form.PartyType == PartyType.Customer ? form.PartyId : null;
        var partyAccount = SystemAccounts.ForParty(form.PartyType);
        var text = $"Receipt - {form.Mode}" + (string.IsNullOrWhiteSpace(form.ReferenceNo) ? "" : $" ref {form.ReferenceNo.Trim()}");

        // P3: Dr Cash/Bank/UPI, Cr party. The credit is split by vehicle so vehicle balances
        // move too (FR-TXN-10); the unallocated rest carries no vehicle tag (FR-LED-08).
        var lines = new List<LineDraft> { LineDraft.Dr(SystemAccounts.ForMode(form.Mode), amount, $"{text} - {partyName}") };
        foreach (var byVehicle in allocations.GroupBy(a => (a.VehicleId, a.VehicleNo)))
        {
            var invoices = string.Join(", ", byVehicle.Select(a => a.InvoiceNo));
            lines.Add(LineDraft.Cr(partyAccount, byVehicle.Sum(a => a.AllocatedPaise), $"{text} against {invoices}",
                supplier, customer, byVehicle.Key.VehicleId));
        }
        var unallocated = amount - allocations.Sum(a => a.AllocatedPaise);
        if (unallocated > 0) lines.Add(LineDraft.Cr(partyAccount, unallocated, $"{text} (on account)", supplier, customer));

        var voucher = new Voucher
        {
            Type = VoucherType.Receipt,
            PartyType = form.PartyType,
            SupplierId = supplier,
            CustomerId = customer,
            Mode = form.Mode,
            ReferenceNo = PartyRules.TidyOrNull(form.ReferenceNo),
            AmountPaise = amount,
            Narration = PartyRules.TidyOrNull(form.Narration),
        };
        try
        {
            await poster.PostAsync(db, voucher, lines, me);
        }
        catch (BusinessRuleException ex)
        {
            return Result<long>.Fail(ex.Message);
        }

        var invoiceIds = allocations.Select(a => a.InvoiceId).ToList();
        var invoicesById = await db.Invoices.Where(i => invoiceIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
        foreach (var a in allocations)
        {
            db.ReceiptAllocations.Add(new ReceiptAllocation { VoucherId = voucher.Id, InvoiceId = a.InvoiceId, VehicleId = a.VehicleId, AmountPaise = a.AllocatedPaise });
            var inv = invoicesById[a.InvoiceId];
            inv.AllocatedPaise += a.AllocatedPaise;
            inv.Status = inv.AllocatedPaise >= inv.AmountPaise ? InvoiceStatus.Paid : InvoiceStatus.PartPaid;
        }

        ActivityLogger.Add(db, Now, me, ActivityCategory.Finance, "RECEIPT_CREATED", nameof(Voucher), voucher.Id,
            after: new { voucher.DisplayNo, Party = partyName, Mode = form.Mode.ToString(), voucher.AmountPaise, UnallocatedPaise = unallocated });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Result<long>.Success(voucher.Id);
    }

    // FR-TXN-11: a receipt is never edited; it is cancelled (reversal) and entered again.
    public async Task<Result> CancelAsync(long voucherId, string reason, int version)
    {
        var me = await currentUser.RequireAsync(Permission.ReceiptCancel);
        if (string.IsNullOrWhiteSpace(reason)) return Result.Fail("A reason is required to cancel a receipt.");

        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var voucher = await db.Vouchers.SingleOrDefaultAsync(v => v.Id == voucherId && v.Type == VoucherType.Receipt);
        if (voucher is null) return Result.Fail("Receipt not found.");
        if (voucher.Version != version) return Result.Fail(Messages.ChangedBySomeoneElse);

        try
        {
            var reversal = await poster.ReverseAsync(db, voucherId, reason, me);

            // Give the money back to the invoices it had paid.
            var allocations = await db.ReceiptAllocations.Where(a => a.VoucherId == voucherId && a.IsActive).ToListAsync();
            var ids = allocations.Select(a => a.InvoiceId).ToList();
            var invoices = await db.Invoices.Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
            foreach (var a in allocations)
            {
                a.IsActive = false;
                var inv = invoices[a.InvoiceId];
                inv.AllocatedPaise -= a.AmountPaise;
                if (inv.Status != InvoiceStatus.Cancelled)
                    inv.Status = inv.AllocatedPaise == 0 ? InvoiceStatus.Posted : InvoiceStatus.PartPaid;
            }

            ActivityLogger.Add(db, Now, me, ActivityCategory.Finance, "RECEIPT_CANCELLED", nameof(Voucher), voucherId,
                new { Status = "Posted" }, new { Status = "Cancelled", Reversal = reversal.DisplayNo }, reason.Trim());
            await db.SaveChangesAsync();
        }
        catch (BusinessRuleException ex)
        {
            return Result.Fail(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Fail(Messages.ChangedBySomeoneElse);
        }
        await tx.CommitAsync();
        return Result.Success();
    }

    // FR-TXN-12: Receipt History with filters.
    public async Task<IReadOnlyList<ReceiptRow>> HistoryAsync(ReceiptFilter f)
    {
        await currentUser.RequireAsync(Permission.ReceiptCreate);
        await using var db = await dbFactory.CreateDbContextAsync();
        var query =
            from v in db.Vouchers
            where v.Type == VoucherType.Receipt && v.VoucherDate >= f.From && v.VoucherDate <= f.To
            join s in db.Suppliers on v.SupplierId equals s.Id into sj
            from s in sj.DefaultIfEmpty()
            join c in db.Customers on v.CustomerId equals c.Id into cj
            from c in cj.DefaultIfEmpty()
            select new { v, PartyName = s != null ? s.Name : c!.Name };
        if (f.PartyType is { } pt) query = query.Where(x => x.v.PartyType == pt);
        if (f.PartyId is { } pid) query = query.Where(x => x.v.SupplierId == pid || x.v.CustomerId == pid);
        if (f.Mode is { } m) query = query.Where(x => x.v.Mode == m);
        if (f.Status is { } st) query = query.Where(x => x.v.Status == st);

        var rows = await query.OrderByDescending(x => x.v.Number).ToListAsync();
        var ids = rows.Select(r => r.v.Id).ToList();
        var allocated = await db.ReceiptAllocations.Where(a => ids.Contains(a.VoucherId) && a.IsActive)
            .GroupBy(a => a.VoucherId).Select(g => new { g.Key, Sum = g.Sum(a => a.AmountPaise) })
            .ToDictionaryAsync(x => x.Key, x => x.Sum);
        return rows.Select(r => new ReceiptRow(r.v.Id, r.v.DisplayNo, r.v.VoucherDate, r.v.PartyType!.Value, r.PartyName,
            r.v.Mode!.Value, r.v.ReferenceNo, r.v.AmountPaise, allocated.GetValueOrDefault(r.v.Id), r.v.Status, r.v.CancelReason, r.v.Version)).ToList();
    }

    public async Task<IReadOnlyList<AllocationLine>> AllocationsOfAsync(long voucherId)
    {
        await currentUser.RequireAsync(Permission.ReceiptCreate);
        await using var db = await dbFactory.CreateDbContextAsync();
        return await (from a in db.ReceiptAllocations
                      join i in db.Invoices on a.InvoiceId equals i.Id
                      join v in db.Vehicles on a.VehicleId equals v.Id
                      where a.VoucherId == voucherId
                      orderby i.InvoiceNo
                      select new AllocationLine(i.Id, i.DisplayNo, i.InvoiceDate, v.Id, v.VehicleNo, i.AmountPaise, a.AmountPaise))
            .ToListAsync();
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static async Task<List<(Invoice Invoice, string VehicleNo)>> OpenInvoicesAsync(AppDbContext db, PartyType type, long partyId)
    {
        var query = from i in db.Invoices
                    join v in db.Vehicles on i.VehicleId equals v.Id
                    where (i.Status == InvoiceStatus.Posted || i.Status == InvoiceStatus.PartPaid) && i.BillToType == type
                        && (type == PartyType.Supplier ? i.SupplierId == partyId : i.CustomerId == partyId)
                    orderby i.InvoiceNo
                    select new { i, v.VehicleNo };
        return (await query.ToListAsync()).Select(x => (x.i, x.VehicleNo)).ToList();
    }

    // Oldest first: fill each invoice's outstanding amount until the money runs out.
    private static List<AllocationLine> Allocate(List<(Invoice Invoice, string VehicleNo)> open, long amountPaise)
    {
        var result = new List<AllocationLine>();
        var left = amountPaise;
        foreach (var (inv, vehicleNo) in open)
        {
            if (left <= 0) break;
            var outstanding = inv.AmountPaise - inv.AllocatedPaise;
            if (outstanding <= 0) continue;
            var take = Math.Min(left, outstanding);
            result.Add(new AllocationLine(inv.Id, inv.DisplayNo, inv.InvoiceDate, inv.VehicleId, vehicleNo, outstanding, take));
            left -= take;
        }
        return result;
    }

    private static async Task<string?> PartyNameAsync(AppDbContext db, PartyType type, long id) =>
        type == PartyType.Supplier
            ? await db.Suppliers.Where(s => s.Id == id).Select(s => s.Name).SingleOrDefaultAsync()
            : await db.Customers.Where(c => c.Id == id).Select(c => c.Name).SingleOrDefaultAsync();
}

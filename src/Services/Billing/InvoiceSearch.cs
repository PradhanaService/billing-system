using Microsoft.EntityFrameworkCore;
using WaterBilling.Data;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Common;
using WaterBilling.Services.Security;

namespace WaterBilling.Services.Billing;

// Invoice Display (final design): find invoices by number, vehicle code or number, supplier, status,
// date range and In Time range; and show which receipts paid an invoice (the "Paid" pop-up).
public enum InvoiceSearchIn { All, InvoiceNumber, VehicleCode, VehicleNumber }

public sealed record InvoiceFilter(DateOnly From, DateOnly To, string? Text = null, InvoiceSearchIn SearchIn = InvoiceSearchIn.All,
    long? SupplierId = null, PayState? Status = null, TimeOnly? TimeFrom = null, TimeOnly? TimeTo = null);

public sealed record InvoiceListRow(long Id, string DisplayNo, DateOnly InvoiceDate, DateTime? InTimeUtc, string SupplierName,
    string VehicleCode, string VehicleNo, InvoicePaymentMode PaymentMode, long AmountPaise, long AllocatedPaise,
    InvoiceStatus Status, IReadOnlyList<string> ReceiptNos)
{
    public PayState State => PayStates.For(Status, PaymentMode, AmountPaise, AllocatedPaise);
}

public sealed record InvoiceReceiptInfo(long VoucherId, string ReceiptNo, DateOnly Date, string ReceivedFrom, PaymentMode? Mode,
    string? Reference, long ReceiptAmountPaise, long PaidToThisInvoicePaise, string CreatedBy)
{
    public bool FromAdvance { get; init; }   // paid later from money kept on account
}

public class InvoiceSearchService(IDbContextFactory<AppDbContext> dbFactory, ICurrentUser currentUser)
{
    public async Task<IReadOnlyList<InvoiceListRow>> SearchAsync(InvoiceFilter f)
    {
        await currentUser.RequireAsync(Permission.InvoiceView);
        await using var db = await dbFactory.CreateDbContextAsync();
        var q = from i in db.Invoices
                join v in db.Vehicles.IgnoreQueryFilters() on i.VehicleId equals v.Id
                join s in db.Suppliers.IgnoreQueryFilters() on i.SupplierId equals s.Id
                join c in db.Customers.IgnoreQueryFilters() on i.CustomerId equals c.Id into cj
                from c in cj.DefaultIfEmpty()
                where i.InvoiceDate >= f.From && i.InvoiceDate <= f.To && (f.SupplierId == null || i.SupplierId == f.SupplierId)
                select new { i, v.Code, v.VehicleNo, v.NormalizedVehicleNo, Party = c == null ? s.Name : c.Name };

        var text = (f.Text ?? "").Trim();
        if (text.Length > 0)
        {
            var digits = new string(text.Where(char.IsDigit).ToArray());
            long? no = long.TryParse(digits, out var n) ? n : null;
            var like = $"%{text}%";
            var compact = $"%{text.Replace(" ", "").ToUpperInvariant()}%";
            q = f.SearchIn switch
            {
                InvoiceSearchIn.InvoiceNumber => q.Where(x => x.i.InvoiceNo == no),
                InvoiceSearchIn.VehicleCode => q.Where(x => EF.Functions.Like(x.Code, like)),
                InvoiceSearchIn.VehicleNumber => q.Where(x => EF.Functions.Like(x.NormalizedVehicleNo, compact)),
                _ => q.Where(x => x.i.InvoiceNo == no || EF.Functions.Like(x.Code, like) || EF.Functions.Like(x.NormalizedVehicleNo, compact)),
            };
        }

        var found = await q.OrderByDescending(x => x.i.InvoiceNo).ToListAsync();
        var ids = found.Select(x => x.i.Id).ToList();
        var receipts = (await (from a in db.ReceiptAllocations
                               join v in db.Vouchers on a.VoucherId equals v.Id
                               where a.IsActive && ids.Contains(a.InvoiceId)
                               select new { a.InvoiceId, v.Number, v.DisplayNo }).ToListAsync())
            .GroupBy(r => r.InvoiceId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.OrderBy(r => r.Number).Select(r => r.Number.ToString()).ToList());

        var rows = found.Select(x => new InvoiceListRow(x.i.Id, x.i.DisplayNo, x.i.InvoiceDate,
                x.i.InTimeUtc is { } t ? DateTime.SpecifyKind(t, DateTimeKind.Utc) : null, x.Party, x.Code, x.VehicleNo,
                x.i.PaymentMode, x.i.AmountPaise, x.i.AllocatedPaise, x.i.Status, receipts.GetValueOrDefault(x.i.Id, [])))
            .Where(r => f.Status is not { } st || r.State == st);

        // Time from / Time to: the invoice's In Time (IST clock time), whatever the date.
        if (f.TimeFrom is not null || f.TimeTo is not null)
        {
            rows = rows.Where(r => r.InTimeUtc is { } t
                && TimeOnly.FromDateTime(IndianTime.ToIst(t)) is var clock
                && (f.TimeFrom is not { } from || clock >= from)
                && (f.TimeTo is not { } to || clock <= to));
        }
        return rows.ToList();
    }

    // The receipts that paid this invoice, newest first (pop-up behind a "Paid" status).
    public async Task<IReadOnlyList<InvoiceReceiptInfo>> ReceiptsForAsync(long invoiceId)
    {
        await currentUser.RequireAsync(Permission.InvoiceView);
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await (from a in db.ReceiptAllocations
                          join v in db.Vouchers on a.VoucherId equals v.Id
                          join u in db.Users on v.CreatedByUserId equals u.Id
                          join s in db.Suppliers.IgnoreQueryFilters() on v.SupplierId equals s.Id into sj
                          from s in sj.DefaultIfEmpty()
                          join c in db.Customers.IgnoreQueryFilters() on v.CustomerId equals c.Id into cj
                          from c in cj.DefaultIfEmpty()
                          where a.InvoiceId == invoiceId && a.IsActive
                          orderby v.Number descending
                          select new InvoiceReceiptInfo(v.Id, v.Number.ToString(), v.VoucherDate,
                              s != null ? s.Name : c != null ? c.Name : "", v.Mode, v.ReferenceNo, v.AmountPaise, a.AmountPaise,
                              u.Role == UserRole.Admin ? "Admin" : u.FullName) { FromAdvance = a.FromAdvance }).ToListAsync();
        return rows;
    }
}

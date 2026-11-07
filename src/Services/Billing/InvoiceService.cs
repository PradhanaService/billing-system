using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using WaterBilling.Data;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Common;
using WaterBilling.Services.Ledger;
using WaterBilling.Services.Masters;
using WaterBilling.Services.Security;

namespace WaterBilling.Services.Billing;

// Sales Invoice form (FR-INV-02 to 08).
public sealed class InvoiceForm
{
    [Range(1, long.MaxValue, ErrorMessage = "Choose a vehicle.")]
    public long VehicleId { get; set; }

    // "Created by": on the Sales login, the sales person making this invoice (several share one login).
    // Left empty on the Admin login: the Admin's invoices are always recorded as made by Admin.
    public long? SalesPersonId { get; set; }

    public PartyType BillToType { get; set; } = PartyType.Supplier;
    public long? CustomerId { get; set; }

    public InvoicePaymentMode PaymentMode { get; set; } = InvoicePaymentMode.Credit;

    [Range(1, 99, ErrorMessage = "Trip must be between 1 and 99.")]
    public int Trips { get; set; } = 1;

    [Range(typeof(decimal), "0", "99999.9", ErrorMessage = "Volume must be between 0 and 99,999.9 kL.")]
    public decimal VolumeKl { get; set; }

    [Rupees(AllowZero = false)] public decimal Rate { get; set; }

    [StringLength(200)] public string? RateReason { get; set; }
    [StringLength(300)] public string? Narration { get; set; }
}

// Everything the view screen and the PDF need.
public sealed record InvoiceDetail(
    long Id, string DisplayNo, long InvoiceNo, DateOnly InvoiceDate, InvoiceStatus Status,
    string VehicleCode, string VehicleNo, string VehicleName, string SupplierName,
    PartyType BillToType, string BillToName, string? BillToAddress, string? BillToPhone,
    InvoicePaymentMode PaymentMode, string Material, string Quantity, string Measurement,
    int Trips, long VolumeTenths, long RatePaise, RateSource RateSource, long AmountPaise, long AllocatedPaise,
    string? Narration, string CreatedBy, DateTime CreatedAtUtc,   // CreatedBy: the chosen sales person
    string? CancelReason, DateTime? CancelledAtUtc, string? ReversalNo, int Version);

public sealed record InvoiceRow(long Id, string DisplayNo, DateOnly InvoiceDate, string VehicleCode, string VehicleNo,
    string BillToName, InvoicePaymentMode PaymentMode, long AmountPaise, long AllocatedPaise, InvoiceStatus Status);

public class InvoiceService(IDbContextFactory<AppDbContext> dbFactory, ICurrentUser currentUser, TimeProvider clock,
    LedgerPoster poster, BusinessCalendar calendar)
{
    // The number the next invoice will get, shown before saving (FR-INV-01).
    public async Task<string> PeekNextNumberAsync()
    {
        await currentUser.RequireAsync(Permission.InvoiceView);
        await using var db = await dbFactory.CreateDbContextAsync();
        var series = await db.NumberSeries.SingleAsync(s => s.Code == "INVOICE");
        return series.Prefix + series.NextNo;
    }

    public DateOnly BusinessDate() => calendar.Today();

    // What "Created by" shows for an invoice made on the Admin login.
    public const string AdminName = "Admin";

    public async Task<Result<long>> CreateAsync(InvoiceForm form)
    {
        var me = await currentUser.RequireAsync(Permission.InvoiceCreate);
        if (FormValidator.FirstError(form) is { } error) return Result<long>.Fail(error);
        if (form.VolumeKl * 10 != decimal.Truncate(form.VolumeKl * 10)) return Result<long>.Fail("Volume can have only one decimal place.");

        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        // FR-VM-09: only an active vehicle of an active supplier can be invoiced.
        var vehicle = await VehicleService.Billable(db).Include(v => v.Supplier).SingleOrDefaultAsync(v => v.Id == form.VehicleId);
        if (vehicle is null) return Result<long>.Fail("This vehicle cannot be invoiced: it or its supplier is not active.");

        // Created by: Admin login = "Admin"; Sales login = a name from the Admin's Sales Persons list.
        SalesPerson? salesPerson = null;
        if (me.Role != UserRole.Admin)
        {
            salesPerson = await db.SalesPersons
                .SingleOrDefaultAsync(p => p.Id == form.SalesPersonId && p.Status == RecordStatus.Active);
            if (salesPerson is null) return Result<long>.Fail("Choose who is creating this invoice (Created by).");
        }
        var createdBy = salesPerson?.Name ?? AdminName;

        // Rate defaults from the vehicle. Changing it needs permission and a reason, and is logged (FR-INV-04).
        var ratePaise = Money.FromRupees(form.Rate);
        var overridden = ratePaise != vehicle.RatePaise;
        if (overridden)
        {
            if (!me.Has(Permission.InvoiceOverrideRate)) return Result<long>.Fail("You do not have permission to change the rate.");
            if (string.IsNullOrWhiteSpace(form.RateReason)) return Result<long>.Fail("Give a reason for changing the rate.");
        }

        var volumeTenths = Volume.FromKilolitres(form.VolumeKl);
        if (vehicle.BillingBasis == BillingBasis.Volume && volumeTenths <= 0)
            return Result<long>.Fail("Enter the volume (kL) for this vehicle, which is billed by volume.");
        var amount = InvoiceMath.Amount(vehicle.BillingBasis, form.Trips, volumeTenths, ratePaise);
        if (amount <= 0) return Result<long>.Fail("The invoice amount must be above zero.");

        // Bill To: the vehicle's supplier, or a chosen active customer (FR-INV-08).
        long? customerId = null;
        string billToName = vehicle.Supplier!.Name;
        if (form.BillToType == PartyType.Customer)
        {
            var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == form.CustomerId && c.Status == RecordStatus.Active);
            if (customer is null) return Result<long>.Fail("Choose an active customer to bill.");
            customerId = customer.Id;
            billToName = customer.Name;
        }
        long? supplierTag = form.BillToType == PartyType.Supplier ? vehicle.SupplierId : null;
        var partyAccount = SystemAccounts.ForParty(form.BillToType);

        var qty = InvoiceMath.Quantity(vehicle.BillingBasis, form.Trips, volumeTenths);
        var text = $"Water sales - {vehicle.VehicleNo} ({qty} {vehicle.UnitOfMeasure})";
        var lines = new List<LineDraft>
        {
            // P1: Dr party (tagged with vehicle), Cr Water Sales.
            LineDraft.Dr(partyAccount, amount, text, supplierTag, customerId, vehicle.Id),
            LineDraft.Cr(SystemAccounts.WaterSales, amount, text, vehicle: vehicle.Id),
        };
        if (form.PaymentMode == InvoicePaymentMode.Cash)
        {
            // P2: the cash is received at once, through the party, so the trip shows on the
            // party statement and nets to zero.
            lines.Add(LineDraft.Dr(SystemAccounts.Cash, amount, $"Cash received - {vehicle.VehicleNo}"));
            lines.Add(LineDraft.Cr(partyAccount, amount, $"Cash received - {vehicle.VehicleNo}", supplierTag, customerId, vehicle.Id));
        }

        var voucher = new Voucher
        {
            Type = VoucherType.Sales,
            PartyType = form.BillToType,
            SupplierId = supplierTag,
            CustomerId = customerId,
            VehicleId = vehicle.Id,
            Mode = form.PaymentMode == InvoicePaymentMode.Cash ? PaymentMode.Cash : null,
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

        var invoice = new Invoice
        {
            InvoiceNo = voucher.Number,
            DisplayNo = voucher.DisplayNo,
            InvoiceDate = voucher.VoucherDate,
            VehicleId = vehicle.Id,
            SupplierId = vehicle.SupplierId,
            BillToType = form.BillToType,
            CustomerId = customerId,
            PaymentMode = form.PaymentMode,
            Trips = form.Trips,
            VolumeTenths = volumeTenths,
            BillingBasis = vehicle.BillingBasis,
            RatePaise = ratePaise,
            RateSource = overridden ? RateSource.Overridden : RateSource.Master,
            AmountPaise = amount,
            AllocatedPaise = form.PaymentMode == InvoicePaymentMode.Cash ? amount : 0,
            Status = form.PaymentMode == InvoicePaymentMode.Cash ? InvoiceStatus.Paid : InvoiceStatus.Posted,
            Narration = voucher.Narration,
            VoucherId = voucher.Id,
            SalesPersonId = salesPerson?.Id,
            CreatedByUserId = me.Id,
            CreatedAtUtc = Now,
            Version = 1,
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        ActivityLogger.Add(db, Now, me, ActivityCategory.Billing, "INVOICE_CREATED", nameof(Invoice), invoice.Id,
            after: new { invoice.DisplayNo, CreatedBy = createdBy, vehicle.VehicleNo, BillTo = billToName, Mode = form.PaymentMode.ToString(), invoice.AmountPaise });
        if (overridden)
        {
            ActivityLogger.Add(db, Now, me, ActivityCategory.Billing, "INVOICE_RATE_OVERRIDDEN", nameof(Invoice), invoice.Id,
                new { RatePaise = vehicle.RatePaise }, new { RatePaise = ratePaise }, form.RateReason!.Trim());
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Result<long>.Success(invoice.Id);
    }

    // FR-INV-12: cancel = reason + reversal. The number is kept and the invoice shows Cancelled.
    public async Task<Result> CancelAsync(long id, string reason, int version)
    {
        var me = await currentUser.RequireAsync(Permission.InvoiceCancel);
        if (string.IsNullOrWhiteSpace(reason)) return Result.Fail("A reason is required to cancel an invoice.");

        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var invoice = await db.Invoices.SingleOrDefaultAsync(i => i.Id == id);
        if (invoice is null) return Result.Fail("Invoice not found.");
        if (invoice.Version != version) return Result.Fail(Messages.ChangedBySomeoneElse);
        if (invoice.Status == InvoiceStatus.Cancelled) return Result.Fail($"{invoice.DisplayNo} is already cancelled.");
        if (invoice.PaymentMode == InvoicePaymentMode.Credit && invoice.AllocatedPaise > 0)
        {
            var receipts = await ReceiptsAllocatedToAsync(db, invoice.Id);
            return Result.Fail($"Receipts are allocated to this invoice ({receipts}). Cancel those receipts first.");
        }

        try
        {
            var reversal = await poster.ReverseAsync(db, invoice.VoucherId, reason, me);
            invoice.Status = InvoiceStatus.Cancelled;
            invoice.ReversalVoucherId = reversal.Id;
            invoice.CancelReason = reason.Trim();
            invoice.CancelledByUserId = me.Id;
            invoice.CancelledAtUtc = Now;
            ActivityLogger.Add(db, Now, me, ActivityCategory.Billing, "INVOICE_CANCELLED", nameof(Invoice), invoice.Id,
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

    public async Task<InvoiceDetail?> GetAsync(long id)
    {
        await currentUser.RequireAsync(Permission.InvoiceView);
        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await (
            from i in db.Invoices
            join v in db.Vehicles on i.VehicleId equals v.Id
            join s in db.Suppliers on i.SupplierId equals s.Id
            join u in db.Users on i.CreatedByUserId equals u.Id
            join c in db.Customers on i.CustomerId equals c.Id into cj
            from c in cj.DefaultIfEmpty()
            join r in db.Vouchers on i.ReversalVoucherId equals r.Id into rj
            from r in rj.DefaultIfEmpty()
            join p in db.SalesPersons on i.SalesPersonId equals p.Id into pj
            from p in pj.DefaultIfEmpty()
            where i.Id == id
            // No sales person: made on the Admin login ("Admin"), or made before the "Created by" list (login's name).
            select new { i, v, s, CreatedBy = p != null ? p.Name : u.Role == UserRole.Admin ? AdminName : u.FullName, Customer = c, ReversalNo = r == null ? null : r.DisplayNo }
        ).SingleOrDefaultAsync();
        if (row is null) return null;

        var inv = row.i;
        var toCustomer = inv.BillToType == PartyType.Customer;
        return new InvoiceDetail(
            inv.Id, inv.DisplayNo, inv.InvoiceNo, inv.InvoiceDate, inv.Status,
            row.v.Code, row.v.VehicleNo, row.v.Nickname, row.s.Name,
            inv.BillToType, toCustomer ? row.Customer!.Name : row.s.Name,
            toCustomer ? row.Customer!.Address : row.s.Address, toCustomer ? row.Customer!.Phone : row.s.Phone,
            inv.PaymentMode, inv.Material, InvoiceMath.Quantity(inv.BillingBasis, inv.Trips, inv.VolumeTenths), inv.Measurement,
            inv.Trips, inv.VolumeTenths, inv.RatePaise, inv.RateSource, inv.AmountPaise, inv.AllocatedPaise,
            inv.Narration, row.CreatedBy, inv.CreatedAtUtc, inv.CancelReason, inv.CancelledAtUtc, row.ReversalNo, inv.Version);
    }

    // FR-INV-14: Previous / Next by invoice number.
    public async Task<long?> NeighbourAsync(long invoiceNo, bool next)
    {
        await currentUser.RequireAsync(Permission.InvoiceView);
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = next
            ? db.Invoices.Where(i => i.InvoiceNo > invoiceNo).OrderBy(i => i.InvoiceNo)
            : db.Invoices.Where(i => i.InvoiceNo < invoiceNo).OrderByDescending(i => i.InvoiceNo);
        return await query.Select(i => (long?)i.Id).FirstOrDefaultAsync();
    }

    public async Task<IReadOnlyList<InvoiceRow>> ListAsync(DateOnly fromDate, DateOnly toDate, long? supplierId = null, long? vehicleId = null, int? take = null)
    {
        await currentUser.RequireAsync(Permission.InvoiceView);
        await using var db = await dbFactory.CreateDbContextAsync();
        var query =
            from i in db.Invoices
            join v in db.Vehicles on i.VehicleId equals v.Id
            join s in db.Suppliers on i.SupplierId equals s.Id
            join c in db.Customers on i.CustomerId equals c.Id into cj
            from c in cj.DefaultIfEmpty()
            where i.InvoiceDate >= fromDate && i.InvoiceDate <= toDate
                && (supplierId == null || i.SupplierId == supplierId)
                && (vehicleId == null || i.VehicleId == vehicleId)
            orderby i.InvoiceNo descending
            select new InvoiceRow(i.Id, i.DisplayNo, i.InvoiceDate, v.Code, v.VehicleNo,
                c == null ? s.Name : c.Name, i.PaymentMode, i.AmountPaise, i.AllocatedPaise, i.Status);
        return take is { } n ? await query.Take(n).ToListAsync() : await query.ToListAsync();
    }

    private static async Task<string> ReceiptsAllocatedToAsync(AppDbContext db, long invoiceId)
    {
        var numbers = await (from a in db.ReceiptAllocations
                             join v in db.Vouchers on a.VoucherId equals v.Id
                             where a.InvoiceId == invoiceId && a.IsActive
                             select v.DisplayNo).Distinct().ToListAsync();
        return string.Join(", ", numbers);
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
}

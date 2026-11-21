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

    [Rupees(AllowZero = false)] public decimal Rate { get; set; }

    [StringLength(200)] public string? RateReason { get; set; }
    [StringLength(300)] public string? Narration { get; set; }

    // Trip Timing (final design): the date is picked, the time is typed ("11:05 AM").
    public DateOnly? InDate { get; set; }
    public string? InTime { get; set; }
    public DateOnly? OutDate { get; set; }
    public string? OutTime { get; set; }

    // Meter Readings in kL with one decimal: start and end for each of the three meters (all optional).
    public decimal? Meter1Start { get; set; }
    public decimal? Meter1End { get; set; }
    public decimal? Meter2Start { get; set; }
    public decimal? Meter2End { get; set; }
    public decimal? Meter3Start { get; set; }
    public decimal? Meter3End { get; set; }
}

// The invoice edit screen (final design): only Capacity, Rate, In Time and Out Time can change.
// Who is editing: a name from Sales Persons (Sales login; the Admin login is recorded as Admin).
public sealed class InvoiceRevision
{
    public long? EditedBySalesPersonId { get; set; }
    [Range(1, 1_000_000, ErrorMessage = "Capacity must be at least 1 litre.")] public int CapacityLitres { get; set; }
    [Rupees(AllowZero = false)] public decimal Rate { get; set; }
    public DateOnly? InDate { get; set; }
    public string? InTime { get; set; }
    public DateOnly? OutDate { get; set; }
    public string? OutTime { get; set; }
    public int Version { get; set; }
}

// Everything the view screen and the PDF need.
public sealed record InvoiceDetail(
    long Id, string DisplayNo, long InvoiceNo, DateOnly InvoiceDate, InvoiceStatus Status,
    string VehicleCode, string VehicleNo, string VehicleName, string SupplierName,
    PartyType BillToType, string BillToName, string? BillToAddress, string? BillToPhone,
    InvoicePaymentMode PaymentMode, string Material, string Quantity, string Measurement,
    int Trips, long VolumeTenths, long RatePaise, RateSource RateSource, long AmountPaise, long AllocatedPaise,
    string? Narration, string CreatedBy, DateTime CreatedAtUtc,   // CreatedBy: the chosen sales person
    string? CancelReason, DateTime? CancelledAtUtc, string? ReversalNo, int Version)
{
    // Final design: capacity at billing, trip times (UTC), meter readings, and the edit chain.
    public int CapacityLitres { get; init; }
    public DateTime? InTimeUtc { get; init; }
    public DateTime? OutTimeUtc { get; init; }
    public MeterReadings Meters { get; init; } = new(null, null, null, null, null, null);
    public string? ReplacesNo { get; init; }      // this invoice corrects that one (edit)
    public string? ReplacedByNo { get; init; }    // this invoice was corrected by that one (edit)
}

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

        var times = TripDetails.Times(form.InDate, form.InTime, form.OutDate, form.OutTime);
        if (!times.Ok) return Result<long>.Fail(times.Error!);
        var meters = TripDetails.Meters(form.Meter1Start, form.Meter1End, form.Meter2Start, form.Meter2End, form.Meter3Start, form.Meter3End);
        if (!meters.Ok) return Result<long>.Fail(meters.Error!);

        // Client change, Nov 2026: every invoice is Trip x Rate (no volume billing).
        var amount = InvoiceMath.Amount(form.Trips, ratePaise);
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
        Voucher voucher;
        try
        {
            voucher = await PostSaleAsync(db, vehicle, form.BillToType, customerId, form.PaymentMode, form.Trips, amount,
                PartyRules.TidyOrNull(form.Narration), me);
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
            VolumeTenths = meters.Value!.VolumeTenths,   // information only: billing is per trip
            CapacityLitres = vehicle.TankCapacityLitres,
            InTimeUtc = times.Value!.InUtc,
            OutTimeUtc = times.Value.OutUtc,
            Meter1StartTenths = meters.Value.Start1, Meter1EndTenths = meters.Value.End1,
            Meter2StartTenths = meters.Value.Start2, Meter2EndTenths = meters.Value.End2,
            Meter3StartTenths = meters.Value.Start3, Meter3EndTenths = meters.Value.End3,
            BillingBasis = BillingBasis.Trip,
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
            after: new { invoice.DisplayNo, CreatedBy = createdBy, vehicle.VehicleNo, BillTo = billToName, Mode = form.PaymentMode.ToString(), invoice.AmountPaise }, doneBy: createdBy);
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

    // Edit an invoice without changing the ledger in place (CLAUDE.md principle 1, FR-INV-11):
    // the old invoice is cancelled with a reversal and a corrected copy is posted with a new number.
    // Receipts set against the old invoice move to the copy. Everything happens in one transaction.
    // Returns the id of the corrected invoice.
    public async Task<Result<long>> ReviseAsync(long id, InvoiceRevision r)
    {
        var me = await currentUser.RequireAsync(Permission.InvoiceCancel);
        if (FormValidator.FirstError(r) is { } error) return Result<long>.Fail(error);
        var times = TripDetails.Times(r.InDate, r.InTime, r.OutDate, r.OutTime);
        if (!times.Ok) return Result<long>.Fail(times.Error!);

        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var old = await db.Invoices.SingleOrDefaultAsync(i => i.Id == id);
        if (old is null) return Result<long>.Fail("Invoice not found.");
        if (old.Version != r.Version) return Result<long>.Fail(Messages.ChangedBySomeoneElse);
        if (old.Status == InvoiceStatus.Cancelled) return Result<long>.Fail($"{old.DisplayNo} is cancelled and cannot be edited.");

        // Who is editing: Admin login = "Admin"; Sales login = an active name from Sales Persons.
        string editor = AdminName;
        if (me.Role != UserRole.Admin)
        {
            var person = await db.SalesPersons.SingleOrDefaultAsync(p => p.Id == r.EditedBySalesPersonId && p.Status == RecordStatus.Active);
            if (person is null) return Result<long>.Fail("Choose which salesperson is editing.");
            editor = person.Name;
        }

        var ratePaise = Money.FromRupees(r.Rate);
        var inUtc = times.Value!.InUtc;
        var outUtc = times.Value.OutUtc;
        static DateTime? Utc(DateTime? t) => t is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;
        if (ratePaise == old.RatePaise && r.CapacityLitres == old.CapacityLitres && inUtc == Utc(old.InTimeUtc) && outUtc == Utc(old.OutTimeUtc))
            return Result<long>.Fail("Nothing was changed.");

        var amount = InvoiceMath.Amount(old.Trips, ratePaise);
        if (old.PaymentMode == InvoicePaymentMode.Credit && old.AllocatedPaise > amount)
            return Result<long>.Fail($"₹{Money.Format(old.AllocatedPaise)} has already been received against {old.DisplayNo}; the new amount cannot be lower than that.");

        var vehicle = await db.Vehicles.IgnoreQueryFilters().SingleAsync(v => v.Id == old.VehicleId);
        var reason = $"Edited by {editor}: corrected copy issued";
        Voucher reversal, voucher;
        try
        {
            reversal = await poster.ReverseAsync(db, old.VoucherId, reason, me);
            voucher = await PostSaleAsync(db, vehicle, old.BillToType, old.CustomerId, old.PaymentMode, old.Trips, amount,
                $"Corrected copy of {old.DisplayNo}", me);
        }
        catch (BusinessRuleException ex)
        {
            return Result<long>.Fail(ex.Message);
        }

        var copy = new Invoice
        {
            InvoiceNo = voucher.Number,
            DisplayNo = voucher.DisplayNo,
            InvoiceDate = voucher.VoucherDate,
            VehicleId = old.VehicleId,
            SupplierId = old.SupplierId,
            BillToType = old.BillToType,
            CustomerId = old.CustomerId,
            PaymentMode = old.PaymentMode,
            Material = old.Material,
            Trips = old.Trips,
            VolumeTenths = old.VolumeTenths,
            BillingBasis = BillingBasis.Trip,
            RatePaise = ratePaise,
            RateSource = ratePaise == vehicle.RatePaise ? RateSource.Master : RateSource.Overridden,
            AmountPaise = amount,
            AllocatedPaise = old.PaymentMode == InvoicePaymentMode.Cash ? amount : old.AllocatedPaise,
            Narration = voucher.Narration,
            VoucherId = voucher.Id,
            SalesPersonId = old.SalesPersonId,           // "Created by" stays the person who made the invoice
            CreatedByUserId = me.Id,
            CreatedAtUtc = Now,
            Version = 1,
            CapacityLitres = r.CapacityLitres,
            InTimeUtc = inUtc,
            OutTimeUtc = outUtc,
            Meter1StartTenths = old.Meter1StartTenths, Meter1EndTenths = old.Meter1EndTenths,
            Meter2StartTenths = old.Meter2StartTenths, Meter2EndTenths = old.Meter2EndTenths,
            Meter3StartTenths = old.Meter3StartTenths, Meter3EndTenths = old.Meter3EndTenths,
            ReplacesInvoiceId = old.Id,
        };
        copy.Status = copy.PaymentMode == InvoicePaymentMode.Cash || copy.AllocatedPaise >= amount ? InvoiceStatus.Paid
            : copy.AllocatedPaise > 0 ? InvoiceStatus.PartPaid : InvoiceStatus.Posted;
        db.Invoices.Add(copy);
        await db.SaveChangesAsync();

        // Receipts that paid the old invoice now pay the corrected copy.
        foreach (var a in await db.ReceiptAllocations.Where(a => a.InvoiceId == old.Id && a.IsActive).ToListAsync())
            a.InvoiceId = copy.Id;

        var before = new { old.DisplayNo, old.CapacityLitres, old.RatePaise, old.AmountPaise, InTime = Show(Utc(old.InTimeUtc)), OutTime = Show(Utc(old.OutTimeUtc)) };
        old.AllocatedPaise = 0;
        old.Status = InvoiceStatus.Cancelled;
        old.ReversalVoucherId = reversal.Id;
        old.CancelReason = reason;
        old.CancelledByUserId = me.Id;
        old.CancelledAtUtc = Now;
        old.ReplacedByInvoiceId = copy.Id;
        old.Version++;

        ActivityLogger.Add(db, Now, me, ActivityCategory.Billing, "INVOICE_EDITED", nameof(Invoice), copy.Id, before,
            new { copy.DisplayNo, copy.CapacityLitres, copy.RatePaise, copy.AmountPaise, InTime = Show(inUtc), OutTime = Show(outUtc), EditedBy = editor },
            reason, doneBy: editor);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<long>.Fail(Messages.ChangedBySomeoneElse);
        }
        await tx.CommitAsync();
        return Result<long>.Success(copy.Id);
    }

    private static string? Show(DateTime? utc) => utc is { } t ? IndianTime.FormatDateTime(t) : null;

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
            join p in db.SalesPersons.IgnoreQueryFilters() on i.SalesPersonId equals p.Id into pj   // removed names still show
            from p in pj.DefaultIfEmpty()
            join o in db.Invoices on i.ReplacesInvoiceId equals o.Id into oj
            from o in oj.DefaultIfEmpty()
            join n in db.Invoices on i.ReplacedByInvoiceId equals n.Id into nj
            from n in nj.DefaultIfEmpty()
            where i.Id == id
            // No sales person: made on the Admin login ("Admin"), or made before the "Created by" list (login's name).
            select new { i, v, s, CreatedBy = p != null ? p.Name : u.Role == UserRole.Admin ? AdminName : u.FullName, Customer = c, ReversalNo = r == null ? null : r.DisplayNo,
                     ReplacesNo = o == null ? null : o.DisplayNo, ReplacedByNo = n == null ? null : n.DisplayNo }
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
            inv.Narration, row.CreatedBy, inv.CreatedAtUtc, inv.CancelReason, inv.CancelledAtUtc, row.ReversalNo, inv.Version)
        {
            CapacityLitres = inv.CapacityLitres,
            InTimeUtc = inv.InTimeUtc is { } t1 ? DateTime.SpecifyKind(t1, DateTimeKind.Utc) : null,
            OutTimeUtc = inv.OutTimeUtc is { } t2 ? DateTime.SpecifyKind(t2, DateTimeKind.Utc) : null,
            Meters = new MeterReadings(inv.Meter1StartTenths, inv.Meter1EndTenths, inv.Meter2StartTenths, inv.Meter2EndTenths,
                                       inv.Meter3StartTenths, inv.Meter3EndTenths),
            ReplacesNo = row.ReplacesNo,
            ReplacedByNo = row.ReplacedByNo,
        };
    }

    // Search box on the Sales Invoice toolbar: "7231" (or "INV-7231" for old invoices) -> that invoice.
    public async Task<long?> FindByNumberAsync(string? text)
    {
        await currentUser.RequireAsync(Permission.InvoiceView);
        var digits = new string((text ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length == 0 || !long.TryParse(digits, out var no)) return null;
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Invoices.Where(i => i.InvoiceNo == no).Select(i => (long?)i.Id).FirstOrDefaultAsync();
    }

    // The newest invoice, where Previous starts from on a blank Sales Invoice.
    public async Task<long?> LatestAsync()
    {
        await currentUser.RequireAsync(Permission.InvoiceView);
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Invoices.OrderByDescending(i => i.InvoiceNo).Select(i => (long?)i.Id).FirstOrDefaultAsync();
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

    // Posts one sale: P1 (Dr party tagged with the vehicle, Cr Water Sales), plus P2 for a cash sale
    // (the cash comes in at once, through the party, so the trip shows on the party statement and nets to zero).
    // Used by a new invoice and by the corrected copy an edit makes. Throws BusinessRuleException.
    private async Task<Voucher> PostSaleAsync(AppDbContext db, Vehicle vehicle, PartyType billToType, long? customerId,
        InvoicePaymentMode mode, int trips, long amount, string? narration, UserInfo me)
    {
        long? supplierTag = billToType == PartyType.Supplier ? vehicle.SupplierId : null;
        var partyAccount = SystemAccounts.ForParty(billToType);
        var text = $"Water sales - {vehicle.VehicleNo} ({trips} {(trips == 1 ? "trip" : "trips")})";
        var lines = new List<LineDraft>
        {
            LineDraft.Dr(partyAccount, amount, text, supplierTag, customerId, vehicle.Id),
            LineDraft.Cr(SystemAccounts.WaterSales, amount, text, vehicle: vehicle.Id),
        };
        if (mode == InvoicePaymentMode.Cash)
        {
            lines.Add(LineDraft.Dr(SystemAccounts.Cash, amount, $"Cash received - {vehicle.VehicleNo}"));
            lines.Add(LineDraft.Cr(partyAccount, amount, $"Cash received - {vehicle.VehicleNo}", supplierTag, customerId, vehicle.Id));
        }
        var voucher = new Voucher
        {
            Type = VoucherType.Sales,
            PartyType = billToType,
            SupplierId = supplierTag,
            CustomerId = customerId,
            VehicleId = vehicle.Id,
            Mode = mode == InvoicePaymentMode.Cash ? PaymentMode.Cash : null,
            AmountPaise = amount,
            Narration = narration,
        };
        return await poster.PostAsync(db, voucher, lines, me);
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
}

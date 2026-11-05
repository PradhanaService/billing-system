using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using WaterBilling.Data;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Common;
using WaterBilling.Services.Ledger;
using WaterBilling.Services.Masters;
using WaterBilling.Services.Security;

namespace WaterBilling.Services.Accounts;

// Payment Voucher form (FR-TXN-02).
public sealed class PaymentForm
{
    [Range(1, long.MaxValue, ErrorMessage = "Choose the ledger.")]
    public long AccountId { get; set; }

    // Only for a refund to a party (Ledger = Trade Receivables).
    public PartyType PartyType { get; set; } = PartyType.Supplier;
    public long PartyId { get; set; }

    [StringLength(120)] public string? PayTo { get; set; }
    public long? VehicleId { get; set; }
    public PaymentMode Mode { get; set; } = PaymentMode.Cash;

    [Rupees(AllowZero = false)] public decimal Amount { get; set; }

    [StringLength(40)] public string? ReferenceNo { get; set; }
    [StringLength(300)] public string? Narration { get; set; }

    // FR-TXN-08: the user ticked "pay more than the cash in hand".
    public bool ConfirmAboveCash { get; set; }
}

public sealed record PaymentLedger(long Id, string Name, PartyType? RefundTo)
{
    public bool IsPartyRefund => RefundTo is not null;
}

public sealed record PaymentRow(long VoucherId, string DisplayNo, DateOnly Date, string Ledger, string? PayTo, string? VehicleNo,
    PaymentMode Mode, string? ReferenceNo, long AmountPaise, VoucherStatus Status, string? CancelReason, int Version);

public class PaymentService(IDbContextFactory<AppDbContext> dbFactory, ICurrentUser currentUser, TimeProvider clock, LedgerPoster poster)
{
    // Ledgers a payment may go to: active expense ledgers, plus refunds to suppliers or customers.
    public async Task<IReadOnlyList<PaymentLedger>> LedgersAsync()
    {
        await currentUser.RequireAsync(Permission.PaymentCreate);
        await using var db = await dbFactory.CreateDbContextAsync();
        // The accounts table is small, so it is filtered in memory (IsPayable is C#, not SQL).
        var accounts = (await db.Accounts.Where(a => a.Status == RecordStatus.Active).ToListAsync()).Where(IsPayable);
        return accounts.Select(a => new PaymentLedger(a.Id,
                a.Id == SystemAccounts.SupplierReceivables ? "Refund / payout to supplier"
                : a.Id == SystemAccounts.CustomerReceivables ? "Refund / payout to customer" : a.Name,
                IsParty(a.Id) ? PartyTypeOf(a.Id) : null))
            .OrderBy(l => l.IsPartyRefund).ThenBy(l => l.Name).ToList();
    }

    public async Task<long> CashInHandAsync()
    {
        await currentUser.RequireAsync(Permission.PaymentCreate);
        await using var db = await dbFactory.CreateDbContextAsync();
        return await LedgerQueries.AccountBalanceAsync(db, SystemAccounts.Cash);
    }

    // FR-TXN-07: balance of the chosen ledger (or party) now.
    public async Task<long> LedgerBalanceAsync(long accountId, PartyType partyType, long partyId)
    {
        await currentUser.RequireAsync(Permission.PaymentCreate);
        await using var db = await dbFactory.CreateDbContextAsync();
        return IsParty(accountId)
            ? await LedgerQueries.PartyBalanceAsync(db, PartyTypeOf(accountId), partyId)
            : await LedgerQueries.AccountBalanceAsync(db, accountId);
    }

    public async Task<string> PeekNextNumberAsync()
    {
        await currentUser.RequireAsync(Permission.PaymentCreate);
        await using var db = await dbFactory.CreateDbContextAsync();
        var s = await db.NumberSeries.SingleAsync(x => x.Code == "PAYMENT");
        return s.Prefix + s.NextNo;
    }

    public async Task<Result<long>> CreateAsync(PaymentForm form)
    {
        var me = await currentUser.RequireAsync(Permission.PaymentCreate);
        if (FormValidator.FirstError(form) is { } error) return Result<long>.Fail(error);
        if (form.Mode != PaymentMode.Cash && string.IsNullOrWhiteSpace(form.ReferenceNo))
            return Result<long>.Fail("Reference No. is required for Bank and UPI payments.");

        var amount = Money.FromRupees(form.Amount);
        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == form.AccountId && a.Status == RecordStatus.Active);
        if (account is null || !IsPayable(account)) return Result<long>.Fail("Choose a valid ledger.");

        string? vehicleNo = null;
        if (form.VehicleId is { } vid and > 0)
        {
            vehicleNo = await db.Vehicles.Where(v => v.Id == vid && !v.IsDeleted).Select(v => v.VehicleNo).SingleOrDefaultAsync();
            if (vehicleNo is null) return Result<long>.Fail("Vehicle not found.");
        }
        long? vehicleId = vehicleNo is null ? null : form.VehicleId;

        // FR-TXN-08: warn before paying out more cash than the drawer holds.
        if (form.Mode == PaymentMode.Cash)
        {
            var cash = await LedgerQueries.AccountBalanceAsync(db, SystemAccounts.Cash);
            if (amount > cash && !form.ConfirmAboveCash)
                return Result<long>.Fail($"Cash in hand is only ₹{Money.Format(cash)}. Tick the confirmation to pay more than that.");
        }

        var moneyAccount = SystemAccounts.ForMode(form.Mode);
        var voucher = new Voucher
        {
            Type = VoucherType.Payment,
            PurposeAccountId = account.Id,
            VehicleId = vehicleId,
            Mode = form.Mode,
            ReferenceNo = PartyRules.TidyOrNull(form.ReferenceNo),
            PayTo = PartyRules.TidyOrNull(form.PayTo),
            AmountPaise = amount,
            Narration = PartyRules.TidyOrNull(form.Narration),
        };

        List<LineDraft> lines;
        string ledgerName;
        if (IsParty(account.Id))
        {
            // P5: refund or payout to a party. Dr party, Cr Cash/Bank/UPI.
            var partyType = PartyTypeOf(account.Id);
            var partyName = partyType == PartyType.Supplier
                ? await db.Suppliers.Where(s => s.Id == form.PartyId).Select(s => s.Name).SingleOrDefaultAsync()
                : await db.Customers.Where(c => c.Id == form.PartyId).Select(c => c.Name).SingleOrDefaultAsync();
            if (partyName is null) return Result<long>.Fail("Choose who is paid.");
            long? supplier = partyType == PartyType.Supplier ? form.PartyId : null;
            long? customer = partyType == PartyType.Customer ? form.PartyId : null;
            voucher.PartyType = partyType;
            voucher.SupplierId = supplier;
            voucher.CustomerId = customer;
            voucher.PayTo ??= partyName;
            ledgerName = $"Refund to {partyName}";
            lines =
            [
                LineDraft.Dr(account.Id, amount, $"Payment {form.Mode} - refund to {partyName}", supplier, customer, vehicleId),
                LineDraft.Cr(moneyAccount, amount, $"Payment - refund to {partyName}"),
            ];
        }
        else
        {
            // P4: operational expense. Dr expense (tagged with vehicle if given), Cr Cash/Bank/UPI.
            ledgerName = account.Name;
            var text = $"{account.Name}" + (voucher.PayTo is null ? "" : $" - {voucher.PayTo}") + (vehicleNo is null ? "" : $" ({vehicleNo})");
            lines =
            [
                LineDraft.Dr(account.Id, amount, text, vehicle: vehicleId),
                LineDraft.Cr(moneyAccount, amount, $"Payment - {text}"),
            ];
        }

        try
        {
            await poster.PostAsync(db, voucher, lines, me);
        }
        catch (BusinessRuleException ex)
        {
            return Result<long>.Fail(ex.Message);
        }

        ActivityLogger.Add(db, Now, me, ActivityCategory.Finance, "PAYMENT_CREATED", nameof(Voucher), voucher.Id,
            after: new { voucher.DisplayNo, Ledger = ledgerName, voucher.PayTo, Mode = form.Mode.ToString(), voucher.AmountPaise });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Result<long>.Success(voucher.Id);
    }

    public async Task<Result> CancelAsync(long voucherId, string reason, int version)
    {
        var me = await currentUser.RequireAsync(Permission.PaymentCancel);
        if (string.IsNullOrWhiteSpace(reason)) return Result.Fail("A reason is required to cancel a payment.");

        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var voucher = await db.Vouchers.SingleOrDefaultAsync(v => v.Id == voucherId && v.Type == VoucherType.Payment);
        if (voucher is null) return Result.Fail("Payment not found.");
        if (voucher.Version != version) return Result.Fail(Messages.ChangedBySomeoneElse);
        try
        {
            var reversal = await poster.ReverseAsync(db, voucherId, reason, me);
            ActivityLogger.Add(db, Now, me, ActivityCategory.Finance, "PAYMENT_CANCELLED", nameof(Voucher), voucherId,
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

    // FR-TXN-12: Payment History.
    public async Task<IReadOnlyList<PaymentRow>> HistoryAsync(DateOnly fromDate, DateOnly toDate, PaymentMode? mode = null, VoucherStatus? status = null)
    {
        await currentUser.RequireAsync(Permission.PaymentCreate);
        await using var db = await dbFactory.CreateDbContextAsync();
        var query =
            from v in db.Vouchers
            where v.Type == VoucherType.Payment && v.VoucherDate >= fromDate && v.VoucherDate <= toDate
                && (mode == null || v.Mode == mode) && (status == null || v.Status == status)
            join a in db.Accounts on v.PurposeAccountId equals a.Id
            join veh in db.Vehicles on v.VehicleId equals veh.Id into vj
            from veh in vj.DefaultIfEmpty()
            orderby v.Number descending
            select new PaymentRow(v.Id, v.DisplayNo, v.VoucherDate,
                a.SystemCode == "AR_SUPPLIERS" || a.SystemCode == "AR_CUSTOMERS" ? "Refund" : a.Name,
                v.PayTo, veh == null ? null : veh.VehicleNo, v.Mode!.Value, v.ReferenceNo, v.AmountPaise, v.Status, v.CancelReason, v.Version);
        return await query.ToListAsync();
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static bool IsParty(long accountId) =>
        accountId is SystemAccounts.SupplierReceivables or SystemAccounts.CustomerReceivables;

    private static PartyType PartyTypeOf(long accountId) =>
        accountId == SystemAccounts.SupplierReceivables ? PartyType.Supplier : PartyType.Customer;

    // Expense ledgers made by users, or the two party ledgers (refunds). Never cash, bank,
    // sales, equity or the cash over/short ledger (that one is only for day closing).
    private static bool IsPayable(Account a) =>
        (a.Type == AccountType.Expense && !a.IsSystem) || IsParty(a.Id);
}

using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using WaterBilling.Data;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Common;
using WaterBilling.Services.Ledger;
using WaterBilling.Services.Recovery;
using WaterBilling.Services.Security;

namespace WaterBilling.Services.Masters;

// Customer Master rules: unique name (ignoring capitals and extra spaces), phone required,
// opening balance posted once on creation, delete only without history, every change recorded.

public sealed record CustomerRow(long Id, string Name, string Phone, string? Address, RecordStatus Status);

public sealed class CustomerForm
{
    [Required(ErrorMessage = "Customer Name is required."), StringLength(120)]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Phone No. is required."), Phone] public string Phone { get; set; } = "";

    [StringLength(500)] public string? Address { get; set; }

    [Rupees] public decimal OpeningBalance { get; set; }
    public BalanceSide OpeningBalanceSide { get; set; } = BalanceSide.Dr;

    public RecordStatus Status { get; set; } = RecordStatus.Active;
    public int Version { get; set; }
}

// Customer Master (FR-VM-11). Same rules as suppliers, fewer fields.
public class CustomerService(IDbContextFactory<AppDbContext> dbFactory, ICurrentUser currentUser, TimeProvider clock, LedgerPoster poster)
{
    public async Task<IReadOnlyList<CustomerRow>> SearchAsync(string? text, StatusFilter filter)
    {
        await currentUser.RequireAsync(Permission.MastersView);
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = db.Customers.AsQueryable();
        if (filter != StatusFilter.All)
        {
            var status = filter == StatusFilter.Active ? RecordStatus.Active : RecordStatus.Inactive;
            query = query.Where(c => c.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(text))
        {
            var t = $"%{text.Trim()}%";
            query = query.Where(c => EF.Functions.Like(c.Name, t) || EF.Functions.Like(c.Phone, t));
        }
        return await query.OrderBy(c => c.Name)
            .Select(c => new CustomerRow(c.Id, c.Name, c.Phone, c.Address, c.Status)).ToListAsync();
    }

    public async Task<IReadOnlyList<(long Id, string Name)>> ListActiveAsync()
    {
        await currentUser.RequireAsync(Permission.MastersView);
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.Customers.Where(c => c.Status == RecordStatus.Active).OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name }).ToListAsync();
        return rows.Select(r => (r.Id, r.Name)).ToList();
    }

    public async Task<CustomerForm?> GetAsync(long id)
    {
        await currentUser.RequireAsync(Permission.MastersView);
        await using var db = await dbFactory.CreateDbContextAsync();
        var c = await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        return c is null ? null : new CustomerForm
        {
            Name = c.Name, Phone = c.Phone, Address = c.Address, OpeningBalance = Money.ToRupees(c.OpeningBalancePaise),
            OpeningBalanceSide = c.OpeningBalanceSide, Status = c.Status, Version = c.Version,
        };
    }

    public async Task<Result<long>> CreateAsync(CustomerForm form)
    {
        var me = await currentUser.RequireAsync(Permission.MastersEdit);
        if (FormValidator.FirstError(form) is { } error) return Result<long>.Fail(error);

        await using var db = await dbFactory.CreateDbContextAsync();
        var customer = new Customer { OpeningBalancePaise = Money.FromRupees(form.OpeningBalance), OpeningBalanceSide = form.OpeningBalanceSide };
        Apply(customer, form);
        if (await NameTakenAsync(db, customer.Name, null)) return Result<long>.Fail("A customer with this name already exists.");

        await using var tx = await db.Database.BeginTransactionAsync();
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        ActivityLogger.Add(db, Now, me, ActivityCategory.Master, "CUSTOMER_CREATED", nameof(Customer), customer.Id, after: Snapshot(customer));
        await db.SaveChangesAsync();
        try
        {
            // P10: the opening balance goes to the ledger in the same transaction (FR-VM-12).
            await OpeningBalances.PostAsync(db, poster, PartyType.Customer, customer.Id, customer.Name,
                customer.OpeningBalancePaise, customer.OpeningBalanceSide, me);
        }
        catch (BusinessRuleException ex)
        {
            return Result<long>.Fail(ex.Message);   // leaving without commit rolls everything back
        }
        await tx.CommitAsync();
        return Result<long>.Success(customer.Id);
    }

    public async Task<Result> UpdateAsync(long id, CustomerForm form)
    {
        var me = await currentUser.RequireAsync(Permission.MastersEdit);
        if (FormValidator.FirstError(form) is { } error) return Result.Fail(error);

        await using var db = await dbFactory.CreateDbContextAsync();
        var customer = await db.Customers.FindAsync(id);
        if (customer is null) return Result.Fail("Customer not found.");
        if (customer.Version != form.Version) return Result.Fail(Messages.ChangedBySomeoneElse);
        if (await NameTakenAsync(db, PartyRules.Tidy(form.Name), id)) return Result.Fail("A customer with this name already exists.");

        var before = Snapshot(customer);
        Apply(customer, form);
        ActivityLogger.Add(db, Now, me, ActivityCategory.Master, "CUSTOMER_UPDATED", nameof(Customer), id, before, Snapshot(customer));
        try { await db.SaveChangesAsync(); return Result.Success(); }
        catch (DbUpdateConcurrencyException) { return Result.Fail(Messages.ChangedBySomeoneElse); }
    }

    // Only a customer with no transactions.
    // Soft delete: the record is hidden, not removed. It can be restored from Recovery for 30 days.
    // deletedBy: the sales person chosen in "Deleted by" (Sales login); ignored on the Admin login.
    public async Task<Result> DeleteAsync(long id, long? deletedBy = null)
    {
        var me = await currentUser.RequireAsync(Permission.MastersEdit);
        await using var db = await dbFactory.CreateDbContextAsync();
        var who = await RecoveryService.DeletedByAsync(db, me, deletedBy);
        if (!who.Ok) return Result.Fail(who.Error!);
        var customer = await db.Customers.FindAsync(id);
        if (customer is null) return Result.Fail("Customer not found.");
        if (await MasterHistory.CustomerHasHistoryAsync(db, id))
            return Result.Fail("This customer has transactions, so it cannot be deleted. Set it to Inactive instead.");

        RecoveryService.MarkDeleted(customer, me, Now, deletedBy);
        ActivityLogger.Add(db, Now, me, ActivityCategory.Master, "CUSTOMER_DELETED", nameof(Customer), id, before: Snapshot(customer), reason: $"Deleted by {who.Value}");
        await db.SaveChangesAsync();
        return Result.Success();
    }


    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static void Apply(Customer c, CustomerForm f)
    {
        c.Name = PartyRules.Tidy(f.Name);
        c.Phone = PartyRules.CleanPhone(f.Phone);
        c.Address = PartyRules.TidyOrNull(f.Address);
        c.Status = f.Status;
    }

    private static Task<bool> NameTakenAsync(AppDbContext db, string name, long? exceptId)
    {
        var key = Supplier.NormalizeName(name);
        return db.Customers.AnyAsync(c => c.NormalizedName == key && c.Id != exceptId);
    }

    private static object Snapshot(Customer c) => new
    {
        c.Name, c.Phone, c.Address, c.OpeningBalancePaise,
        OpeningBalanceSide = c.OpeningBalanceSide.ToString(), Status = c.Status.ToString(),
    };
}

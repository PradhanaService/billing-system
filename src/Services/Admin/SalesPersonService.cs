using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using WaterBilling.Data;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Common;
using WaterBilling.Services.Masters;
using WaterBilling.Services.Security;

namespace WaterBilling.Services.Admin;

// The "Created by" names on the Sales Invoice. The Admin adds and removes them; the Sales login
// only reads the list. A name already used on invoices is hidden, not deleted, so old invoices keep it.

public sealed record SalesPersonRow(long Id, string Name, int InvoiceCount);

public sealed class SalesPersonForm
{
    [Required(ErrorMessage = "Enter the sales person's name."), StringLength(80)]
    public string Name { get; set; } = "";
}

// The names shown in the invoice "Created by" list. Admin adds and removes them.
public class SalesPersonService(IDbContextFactory<AppDbContext> dbFactory, ICurrentUser currentUser, TimeProvider clock)
{
    // For the Sales Invoice dropdown: active names only.
    public async Task<IReadOnlyList<(long Id, string Name)>> ListActiveAsync()
    {
        await currentUser.RequireAsync(Permission.InvoiceCreate);
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.SalesPersons.Where(p => p.Status == RecordStatus.Active).OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name }).ToListAsync();
        return rows.Select(r => (r.Id, r.Name)).ToList();
    }

    // For the Admin page: the names in the list, with how many invoices each has made.
    public async Task<IReadOnlyList<SalesPersonRow>> ListAsync()
    {
        await currentUser.RequireAsync(Permission.SalesPersonsManage);
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.SalesPersons.Where(p => p.Status == RecordStatus.Active).OrderBy(p => p.Name)
            .Select(p => new SalesPersonRow(p.Id, p.Name, db.Invoices.Count(i => i.SalesPersonId == p.Id)))
            .ToListAsync();
    }

    public async Task<Result> AddAsync(SalesPersonForm form)
    {
        var me = await currentUser.RequireAsync(Permission.SalesPersonsManage);
        if (FormValidator.FirstError(form) is { } error) return Result.Fail(error);
        var name = PartyRules.Tidy(form.Name);
        var key = Supplier.NormalizeName(name);

        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var existing = await db.SalesPersons.SingleOrDefaultAsync(p => p.NormalizedName == key);
        if (existing is { Status: RecordStatus.Active }) return Result.Fail($"{existing.Name} is already in the list.");

        if (existing is not null)
        {
            // Removed earlier but kept for old invoices: put the same person back on the list.
            existing.Status = RecordStatus.Active;
            existing.Name = name;
        }
        else
        {
            existing = new SalesPerson { Name = name, NormalizedName = key };
            db.SalesPersons.Add(existing);
        }
        await db.SaveChangesAsync();
        ActivityLogger.Add(db, Now, me, ActivityCategory.Admin, "SALES_PERSON_ADDED", nameof(SalesPerson), existing.Id,
            after: new { existing.Name });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Result.Success();
    }

    // Takes the name off the dropdown. If invoices already name this person it is kept (Inactive)
    // so those invoices still show who made them; otherwise it is deleted.
    public async Task<Result> RemoveAsync(long id)
    {
        var me = await currentUser.RequireAsync(Permission.SalesPersonsManage);
        await using var db = await dbFactory.CreateDbContextAsync();
        var person = await db.SalesPersons.SingleOrDefaultAsync(p => p.Id == id && p.Status == RecordStatus.Active);
        if (person is null) return Result.Fail("Sales person not found.");

        if (await db.Invoices.AnyAsync(i => i.SalesPersonId == id)) person.Status = RecordStatus.Inactive;
        else db.SalesPersons.Remove(person);

        ActivityLogger.Add(db, Now, me, ActivityCategory.Admin, "SALES_PERSON_REMOVED", nameof(SalesPerson), id,
            before: new { person.Name });
        await db.SaveChangesAsync();
        return Result.Success();
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
}

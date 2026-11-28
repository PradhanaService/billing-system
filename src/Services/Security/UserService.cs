using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using WaterBilling.Data;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Common;

namespace WaterBilling.Services.Security;

// Users & Roles rules (Admin only): one active login per role (one Admin, one shared Sales login),
// temporary password changed at first sign-in, logins deactivated but never deleted, the last Admin
// cannot be removed. Changing a role or deactivating signs that user out straight away.

public sealed record LoginCard(UserRole Role, long? Id, string? UserName, DateTime? PasswordChangedAtUtc, DateTime? LastLoginAtUtc);

public sealed record UserRow(long Id, string UserName, string FullName, UserRole Role,
    RecordStatus Status, DateTime? LastLoginAtUtc, int Version);

// Form used to create or edit a user. The attributes give instant feedback on screen;
// the service checks the same rules again.
public sealed class UserForm
{
    [Required, StringLength(50, MinimumLength = 3), RegularExpression(@"^[A-Za-z0-9._-]+$",
        ErrorMessage = "Use letters, numbers, dot, dash or underscore only.")]
    public string UserName { get; set; } = "";

    [Required, StringLength(100)]
    public string FullName { get; set; } = "";

    [StringLength(15)]
    public string? Phone { get; set; }

    public UserRole Role { get; set; } = UserRole.SalesPerson;

    // Only used when creating, or when the Admin resets a password.
    public string? TemporaryPassword { get; set; }
}

// Users & Roles screen (FR-ADM-01). Admin only.
public class UserService(IDbContextFactory<AppDbContext> dbFactory, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<IReadOnlyList<UserRow>> ListAsync()
    {
        await currentUser.RequireAsync(Permission.UsersManage);
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Users.OrderBy(u => u.UserName)
            .Select(u => new UserRow(u.Id, u.UserName, u.FullName, u.Role, u.Status, u.LastLoginAtUtc, u.Version))
            .ToListAsync();
    }

    public async Task<Result<long>> CreateAsync(UserForm form)
    {
        var me = await currentUser.RequireAsync(Permission.UsersManage);
        if (Validate(form) is { } error) return Result<long>.Fail(error);
        var userName = form.UserName.Trim();
        if (PasswordRules.Check(form.TemporaryPassword ?? "", userName) is { } pwError) return Result<long>.Fail(pwError);

        await using var db = await dbFactory.CreateDbContextAsync();
        var key = userName.ToUpperInvariant();
        if (await db.Users.AnyAsync(u => u.NormalizedUserName == key))
            return Result<long>.Fail("A user with this name already exists.");
        if (await RoleTakenAsync(db, form.Role, exceptUserId: null) is { } taken) return Result<long>.Fail(taken);

        var user = new User
        {
            UserName = userName,
            NormalizedUserName = key,
            FullName = form.FullName.Trim(),
            Phone = string.IsNullOrWhiteSpace(form.Phone) ? null : form.Phone.Trim(),
            Role = form.Role,
            MustChangePassword = true,
            PasswordChangedAtUtc = Now,
        };
        user.PasswordHash = AuthService.HashPassword(user, form.TemporaryPassword!);

        // One transaction: the user and its log row are saved together or not at all.
        await using var tx = await db.Database.BeginTransactionAsync();
        db.Users.Add(user);
        await db.SaveChangesAsync();                  // gives the user its Id
        ActivityLogger.Add(db, Now, me, ActivityCategory.Admin, "USER_CREATED", "User", user.Id,
            after: new { user.UserName, user.FullName, Role = user.Role.ToString() });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Result<long>.Success(user.Id);
    }

    public async Task<Result> UpdateAsync(long id, UserForm form, int version)
    {
        var me = await currentUser.RequireAsync(Permission.UsersManage);
        if (Validate(form) is { } error) return Result.Fail(error);

        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.FindAsync(id);
        if (user is null) return Result.Fail("User not found.");
        if (user.Version != version) return Result.Fail("This user was changed by someone else. Reload and try again.");

        if (user.Role == UserRole.Admin && form.Role != UserRole.Admin && await IsLastActiveAdmin(db, user.Id))
            return Result.Fail("You cannot remove the last active Admin.");
        if (user.Role != form.Role && user.Status == RecordStatus.Active
            && await RoleTakenAsync(db, form.Role, user.Id) is { } taken)
            return Result.Fail(taken);

        var before = new { user.FullName, user.Phone, Role = user.Role.ToString() };
        user.FullName = form.FullName.Trim();
        user.Phone = string.IsNullOrWhiteSpace(form.Phone) ? null : form.Phone.Trim();
        if (user.Role != form.Role)
        {
            user.Role = form.Role;
            user.SecurityStamp = Guid.NewGuid().ToString("N");   // signs the user out so the new role applies
        }
        ActivityLogger.Add(db, Now, me, ActivityCategory.Admin, "USER_UPDATED", "User", user.Id,
            before, new { user.FullName, user.Phone, Role = user.Role.ToString() });
        return await SaveAsync(db);
    }

    public async Task<Result> SetStatusAsync(long id, RecordStatus status, int version)
    {
        var me = await currentUser.RequireAsync(Permission.UsersManage);
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.FindAsync(id);
        if (user is null) return Result.Fail("User not found.");
        if (user.Version != version) return Result.Fail("This user was changed by someone else. Reload and try again.");
        if (status == RecordStatus.Inactive)
        {
            if (user.Id == me.Id) return Result.Fail("You cannot deactivate your own account.");
            if (user.Role == UserRole.Admin && await IsLastActiveAdmin(db, user.Id))
                return Result.Fail("You cannot deactivate the last active Admin.");
        }
        else if (await RoleTakenAsync(db, user.Role, user.Id) is { } taken)
        {
            return Result.Fail(taken);
        }

        var before = user.Status;
        user.Status = status;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        ActivityLogger.Add(db, Now, me, ActivityCategory.Admin,
            status == RecordStatus.Active ? "USER_ACTIVATED" : "USER_DEACTIVATED", "User", user.Id,
            new { Status = before.ToString() }, new { Status = status.ToString() });
        return await SaveAsync(db);
    }

    public async Task<Result> ResetPasswordAsync(long id, string temporaryPassword, int version)
    {
        var me = await currentUser.RequireAsync(Permission.UsersManage);
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.FindAsync(id);
        if (user is null) return Result.Fail("User not found.");
        if (user.Version != version) return Result.Fail("This user was changed by someone else. Reload and try again.");
        if (PasswordRules.Check(temporaryPassword ?? "", user.UserName) is { } pwError) return Result.Fail(pwError);

        user.PasswordHash = AuthService.HashPassword(user, temporaryPassword!);
        user.MustChangePassword = true;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.PasswordChangedAtUtc = Now;
        ActivityLogger.Add(db, Now, me, ActivityCategory.Admin, "PASSWORD_RESET", "User", user.Id);
        return await SaveAsync(db);
    }

    // ---------- Password Management (final design, Nov 2026) ----------
    // Only two logins exist: Admin and the shared Sales login. The Admin sets either password here.
    // The new password works from the next sign-in: the user's open sessions are signed out.
    // If the Sales login has not been made yet, setting its password creates it (user name "sales").

    public const string SalesUserName = "sales";

    public async Task<IReadOnlyList<LoginCard>> LoginsAsync()
    {
        await currentUser.RequireAsync(Permission.UsersManage);
        await using var db = await dbFactory.CreateDbContextAsync();
        var users = await db.Users.Where(u => u.Status == RecordStatus.Active).ToListAsync();
        return [.. new[] { UserRole.Admin, UserRole.SalesPerson }.Select(role =>
        {
            var u = users.Where(x => x.Role == role).OrderBy(x => x.Id).FirstOrDefault();
            return new LoginCard(role, u?.Id, u?.UserName, u?.PasswordChangedAtUtc, u?.LastLoginAtUtc);
        })];
    }

    public async Task<Result> SetPasswordAsync(UserRole role, string? newPassword, string? confirm)
    {
        var me = await currentUser.RequireAsync(Permission.UsersManage);
        if (string.IsNullOrEmpty(newPassword)) return Result.Fail("Enter the new password.");
        if (newPassword != confirm) return Result.Fail("The two passwords do not match.");

        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.Where(u => u.Role == role && u.Status == RecordStatus.Active).OrderBy(u => u.Id).FirstOrDefaultAsync();
        var created = user is null;
        if (user is null)
        {
            if (role != UserRole.SalesPerson) return Result.Fail("There is no active Admin login.");
            if (await db.Users.AnyAsync(u => u.NormalizedUserName == SalesUserName.ToUpperInvariant()))
                return Result.Fail("A login named \"sales\" already exists but is inactive.");
            user = new User { UserName = SalesUserName, NormalizedUserName = SalesUserName.ToUpperInvariant(), FullName = "Sales", Role = role };
            db.Users.Add(user);
        }
        if (PasswordRules.Check(newPassword, user.UserName) is { } problem) return Result.Fail(problem);

        user.PasswordHash = AuthService.HashPassword(user, newPassword);
        user.MustChangePassword = false;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.PasswordChangedAtUtc = Now;
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.SaveChangesAsync();
        ActivityLogger.Add(db, Now, me, ActivityCategory.Admin, created ? "USER_CREATED" : "PASSWORD_SET", "User", user.Id,
            after: new { user.UserName, Role = role.ToString() });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Result.Success();
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // Client rule: one login per role (one Admin login, one shared Sales login).
    // Returns the reason when another active user already has this role.
    private static async Task<string?> RoleTakenAsync(AppDbContext db, UserRole role, long? exceptUserId)
    {
        var holder = await db.Users
            .Where(u => u.Role == role && u.Status == RecordStatus.Active && u.Id != exceptUserId)
            .Select(u => u.UserName).FirstOrDefaultAsync();
        return holder is null ? null
            : $"There is already an active {RolePermissions.DisplayName(role)} login ({holder}). Only one login per role is allowed: deactivate it first.";
    }

    private static Task<bool> IsLastActiveAdmin(AppDbContext db, long userId) =>
        db.Users.AllAsync(u => u.Id == userId || u.Role != UserRole.Admin || u.Status != RecordStatus.Active);

    private static string? Validate(UserForm form) => FormValidator.FirstError(form);

    private static async Task<Result> SaveAsync(AppDbContext db)
    {
        try
        {
            await db.SaveChangesAsync();
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Fail("This record was changed by someone else. Reload and try again.");
        }
    }
}

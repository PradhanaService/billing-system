using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WaterBilling.Data;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Common;

namespace WaterBilling.Services.Security;

// Login, logout and password change.
public class AuthService(IDbContextFactory<AppDbContext> dbFactory, TimeProvider clock)
{
    private static readonly PasswordHasher<User> Hasher = new();

    // A real hash of a random password. Checked when the user name does not exist,
    // so a wrong name takes as long as a wrong password and names cannot be guessed by timing.
    private static readonly string DummyHash = Hasher.HashPassword(new User(), Guid.NewGuid().ToString());

    public static string HashPassword(User user, string password) => Hasher.HashPassword(user, password);

    public async Task<Result<ClaimsPrincipal>> LoginAsync(string userName, string password)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var now = clock.GetUtcNow().UtcDateTime;
        var key = (userName ?? "").Trim().ToUpperInvariant();
        var user = await db.Users.SingleOrDefaultAsync(u => u.NormalizedUserName == key);

        bool ok;
        if (user is not null)
        {
            ok = Hasher.VerifyHashedPassword(user, user.PasswordHash, password ?? "") != PasswordVerificationResult.Failed;
        }
        else
        {
            Hasher.VerifyHashedPassword(new User(), DummyHash, password ?? "");   // same work, result ignored
            ok = false;
        }

        if (!ok || user is null || user.Status != RecordStatus.Active)
        {
            var reason = user is null ? "unknown user" : user.Status != RecordStatus.Active ? "inactive user" : "wrong password";
            ActivityLogger.Add(db, now, user is null ? null : ToInfo(user), ActivityCategory.Auth, "LOGIN_FAILED",
                "User", user?.Id, reason: reason, userNameIfUnknown: userName);
            await db.SaveChangesAsync();
            // Same message in every case, so the screen does not reveal which part was wrong.
            return Result<ClaimsPrincipal>.Fail("Wrong user name or password, or the account is inactive.");
        }

        user.LastLoginAtUtc = now;
        ActivityLogger.Add(db, now, ToInfo(user), ActivityCategory.Auth, "LOGIN", "User", user.Id);
        await db.SaveChangesAsync();
        return Result<ClaimsPrincipal>.Success(UserInfo.ToPrincipal(user));
    }

    public async Task LogLogoutAsync(UserInfo user)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        ActivityLogger.Add(db, clock.GetUtcNow().UtcDateTime, user, ActivityCategory.Auth, "LOGOUT", "User", user.Id);
        await db.SaveChangesAsync();
    }

    // On success returns the new login cookie contents (new security stamp, no "must change" flag).
    public async Task<Result<ClaimsPrincipal>> ChangePasswordAsync(long userId, string currentPassword, string newPassword)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId && u.Status == RecordStatus.Active);
        if (user is null) return Result<ClaimsPrincipal>.Fail("Account not found or inactive.");

        if (Hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword ?? "") == PasswordVerificationResult.Failed)
            return Result<ClaimsPrincipal>.Fail("Current password is wrong.");
        if (PasswordRules.Check(newPassword ?? "", user.UserName) is { } problem)
            return Result<ClaimsPrincipal>.Fail(problem);
        if (newPassword == currentPassword)
            return Result<ClaimsPrincipal>.Fail("New password must be different from the current one.");

        user.PasswordHash = Hasher.HashPassword(user, newPassword!);
        user.MustChangePassword = false;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        ActivityLogger.Add(db, clock.GetUtcNow().UtcDateTime, ToInfo(user), ActivityCategory.Auth, "PASSWORD_CHANGED", "User", user.Id);
        await db.SaveChangesAsync();
        return Result<ClaimsPrincipal>.Success(UserInfo.ToPrincipal(user));
    }

    // Used on every request and every minute in a live screen:
    // is this login still valid (user active, password/role unchanged)?
    public async Task<bool> IsStillValidAsync(ClaimsPrincipal principal)
    {
        var info = UserInfo.FromPrincipal(principal);
        var stamp = principal.FindFirstValue(UserInfo.StampClaim);
        if (info is null || stamp is null) return false;
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Users.AnyAsync(u => u.Id == info.Id && u.Status == RecordStatus.Active && u.SecurityStamp == stamp);
    }

    // First start: create the Admin account so someone can log in.
    public async Task EnsureAdminExistsAsync(string initialPassword)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Users.AnyAsync()) return;
        var admin = new User
        {
            UserName = "admin",
            NormalizedUserName = "ADMIN",
            FullName = "Administrator",
            Role = UserRole.Admin,
            MustChangePassword = true,
        };
        admin.PasswordHash = Hasher.HashPassword(admin, initialPassword);
        db.Users.Add(admin);
        ActivityLogger.Add(db, clock.GetUtcNow().UtcDateTime, null, ActivityCategory.Admin, "ADMIN_SEEDED", "User", null,
            userNameIfUnknown: "system");
        await db.SaveChangesAsync();
    }

    private static UserInfo ToInfo(User u) => new(u.Id, u.UserName, u.Role);
}

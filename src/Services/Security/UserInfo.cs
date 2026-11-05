using System.Security.Claims;
using WaterBilling.Data.Entities;

namespace WaterBilling.Services.Security;

// The logged-in person, as services see them.
public sealed record UserInfo(long Id, string UserName, UserRole Role)
{
    public bool Has(Permission permission) => RolePermissions.Has(Role, permission);

    // Claim names stored in the login cookie.
    public const string StampClaim = "stamp";
    public const string MustChangeClaim = "must_change";

    public static ClaimsPrincipal ToPrincipal(User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(StampClaim, user.SecurityStamp),
        };
        if (user.MustChangePassword) claims.Add(new(MustChangeClaim, "true"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies"));
    }

    // Reads the cookie claims back. Returns null if not logged in or the claims are damaged.
    public static UserInfo? FromPrincipal(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true) return null;
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var name = principal.FindFirstValue(ClaimTypes.Name);
        var role = principal.FindFirstValue(ClaimTypes.Role);
        if (!long.TryParse(id, out var userId) || name is null || !Enum.TryParse<UserRole>(role, out var userRole))
            return null;
        return new UserInfo(userId, name, userRole);
    }
}

// Thrown when someone tries an action their role does not allow.
// The screens hide those actions, so reaching this means a bug or tampering.
public sealed class NotAllowedException(Permission permission)
    : Exception($"You do not have permission for this action ({permission}).");

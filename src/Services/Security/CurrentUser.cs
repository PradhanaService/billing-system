using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace WaterBilling.Services.Security;

// Services ask this "who is doing this?". Blazor screens get the answer from the
// login state of the live connection. PDF download endpoints (normal HTTP requests)
// hand in the request's user with UseHttpUser.
public interface ICurrentUser
{
    Task<UserInfo?> GetAsync();
}

public sealed class CurrentUser(AuthenticationStateProvider authState) : ICurrentUser
{
    private ClaimsPrincipal? _httpUser;

    public void UseHttpUser(ClaimsPrincipal user) => _httpUser = user;

    public async Task<UserInfo?> GetAsync()
    {
        if (_httpUser is not null) return UserInfo.FromPrincipal(_httpUser);
        var state = await authState.GetAuthenticationStateAsync();
        return UserInfo.FromPrincipal(state.User);
    }
}

public static class CurrentUserExtensions
{
    // The one-line permission check at the top of every service method.
    public static async Task<UserInfo> RequireAsync(this ICurrentUser currentUser, Permission permission)
    {
        var user = await currentUser.GetAsync() ?? throw new NotAllowedException(permission);
        if (!user.Has(permission)) throw new NotAllowedException(permission);
        return user;
    }
}

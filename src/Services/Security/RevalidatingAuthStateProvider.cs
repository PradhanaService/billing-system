using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace WaterBilling.Services.Security;

// A Blazor screen stays connected for a long time. Every minute this checks that the
// user is still active and their password/role has not changed. If not, the screen
// is logged out straight away.
public sealed class RevalidatingAuthStateProvider(ILoggerFactory loggerFactory, IServiceScopeFactory scopeFactory)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(1);

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
        return await auth.IsStillValidAsync(state.User);
    }
}

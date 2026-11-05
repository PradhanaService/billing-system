using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using WaterBilling.Services.Common;
using WaterBilling.Services.Security;

namespace WaterBilling.Components;

// Base class for screens: knows the logged-in user (to show or hide buttons)
// and shows the result of the last action.
// Showing a button is only convenience; services check permissions again.
public abstract class UserContextBase : ComponentBase
{
    [CascadingParameter] private Task<AuthenticationState>? AuthState { get; set; }

    protected UserInfo? Me { get; private set; }
    protected string? MessageText { get; set; }
    protected bool MessageOk { get; set; }
    protected bool Busy { get; set; }   // stops a double click saving twice (NFR-CON-06)

    protected bool Can(Permission p) => Me?.Has(p) == true;

    protected override async Task OnInitializedAsync()
    {
        if (AuthState is not null) Me = UserInfo.FromPrincipal((await AuthState).User);
        await OnUserReadyAsync();
    }

    protected virtual Task OnUserReadyAsync() => Task.CompletedTask;

    protected void Show(Result result, string okText)
    {
        MessageOk = result.Ok;
        MessageText = result.Ok ? okText : result.Error;
    }

    protected void ShowError(string text) { MessageOk = false; MessageText = text; }
    protected void ShowOk(string text) { MessageOk = true; MessageText = text; }
}

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using WaterBilling.Data;
using WaterBilling.Services.Common;
using WaterBilling.Services.Security;

namespace WaterBilling.Components;

// Base class for screens: knows the logged-in user (to show or hide buttons),
// shows the result of the last action, and can keep its figures live.
// Showing a button is only convenience; services check permissions again.
public abstract class UserContextBase : ComponentBase, IDisposable
{
    [CascadingParameter] private Task<AuthenticationState>? AuthState { get; set; }
    [Inject] private DataChangeNotifier DataChanges { get; set; } = default!;

    private Func<Task>? _reload;

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

    // Call once (e.g. in OnUserReadyAsync): whenever anyone saves data, in any window,
    // run `reload` and redraw, so figures on an open screen never go stale.
    protected void RefreshWhenDataChanges(Func<Task> reload)
    {
        _reload = reload;
        DataChanges.Changed += OnDataChanged;
    }

    // Called from whichever connection saved the data, so switch to this screen's own thread first.
    private void OnDataChanged() => _ = InvokeAsync(async () =>
    {
        if (_reload is null) return;
        try
        {
            await _reload();
            StateHasChanged();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or NotAllowedException)
        {
            // The window was closed or the user signed out meanwhile: nothing to refresh.
        }
    });

    public void Dispose()
    {
        DataChanges.Changed -= OnDataChanged;
        _reload = null;
        GC.SuppressFinalize(this);
    }
}

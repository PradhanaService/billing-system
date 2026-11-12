using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace WaterBilling.Desktop;

// The Water Billing window. All screens come from the Water Billing service that the
// installer sets up on this computer (it starts with Windows). This window only shows them:
// no address bar, no browser tabs. If the service is still starting it waits for it.
public sealed class MainForm : Form
{
    // The service listens only on this computer (see src/appsettings.Production.json).
    internal static readonly Uri AppAddress = new("http://localhost:5080/");

    // Kept visible (Windows only starts a visible control); the "Starting" panel covers it until ready.
    private readonly WebView2 _view = new() { Dock = DockStyle.Fill };
    private readonly TableLayoutPanel _waiting = new() { Dock = DockStyle.Fill, RowCount = 2, BackColor = Color.White };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Segoe UI", 14f), Text = "Starting Water Billing…",
    };
    private readonly Button _retry = new() { Text = "Try again", AutoSize = true, Visible = false, Anchor = AnchorStyles.None };

    public MainForm()
    {
        Text = "Water Billing";
        Icon = AppIcon.Load();
        MinimumSize = new Size(1000, 650);
        WindowState = FormWindowState.Maximized;
        BackColor = Color.White;

        _waiting.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _waiting.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        _waiting.Controls.Add(_status, 0, 0);
        _waiting.Controls.Add(_retry, 0, 1);
        _retry.Click += async (_, _) => await ConnectAsync();
        Controls.Add(_view);
        Controls.Add(_waiting);
        _waiting.BringToFront();

        Shown += async (_, _) => await ConnectAsync();
    }

    // Wait for the service (up to a minute, e.g. just after Windows starts), then show the app.
    private async Task ConnectAsync()
    {
        _retry.Visible = false;
        _status.Text = "Starting Water Billing…";
        if (!await ServiceIsReadyAsync(TimeSpan.FromSeconds(60)))
        {
            _status.Text = "Water Billing could not start.\nRestart the computer and open it again.\nIf it still does not open, contact support.";
            _retry.Visible = true;
            return;
        }

        if (_view.CoreWebView2 is null)
        {
            // The window's own storage (sign-in cookie, cache) lives in the user's AppData,
            // because Program Files cannot be written to.
            var dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WaterBilling", "Window");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: dataFolder);
            await _view.EnsureCoreWebView2Async(environment);
            AppWindow.Configure(_view.CoreWebView2!, this);
            _view.CoreWebView2!.DocumentTitleChanged += (_, _) => Text = TitleFor(_view.CoreWebView2.DocumentTitle);
        }

        _view.Source = AppAddress;
        _waiting.Visible = false;
    }

    private static async Task<bool> ServiceIsReadyAsync(TimeSpan limit)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var until = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < until)
        {
            try
            {
                using var answer = await http.GetAsync(new Uri(AppAddress, "account/login"));
                if (answer.IsSuccessStatusCode) return true;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(1000);
        }
        return false;
    }

    internal static string TitleFor(string page) => string.IsNullOrWhiteSpace(page) ? "Water Billing" : $"{page} - Water Billing";
}

// Settings shared by the main window and the PDF windows.
internal static class AppWindow
{
    public static void Configure(CoreWebView2 web, Form owner)
    {
        web.Settings.AreDevToolsEnabled = false;          // no developer tools for the shop user
        web.Settings.IsStatusBarEnabled = false;          // no link addresses shown at the bottom
        web.Settings.AreBrowserAcceleratorKeysEnabled = false;   // no browser shortcuts (Ctrl+P works through the app's Print buttons)
        web.Settings.IsZoomControlEnabled = true;

        // Only the Water Billing screens open inside the app; a PDF always gets its own window.
        web.NavigationStarting += (_, e) =>
        {
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var to)) return;
            if (to.Host != MainForm.AppAddress.Host) { e.Cancel = true; return; }
            if (owner is MainForm && to.AbsolutePath.StartsWith("/pdf/", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                new PdfForm(to, web.Environment).Show(owner);
            }
        };

        // "Print PDF" links open in a separate app window (signed in, same session) instead of a browser.
        web.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var to) && to.Host == MainForm.AppAddress.Host)
                new PdfForm(to, web.Environment).Show(owner);
        };
    }
}

// A window for one PDF (invoice, report, statement). It has Print and Save in its own toolbar.
internal sealed class PdfForm : Form
{
    public PdfForm(Uri address, CoreWebView2Environment environment)
    {
        Text = "Water Billing - PDF";
        Icon = AppIcon.Load();
        Size = new Size(1000, 800);
        StartPosition = FormStartPosition.CenterParent;
        var view = new WebView2 { Dock = DockStyle.Fill };
        Controls.Add(view);
        Shown += async (_, _) =>
        {
            await view.EnsureCoreWebView2Async(environment);
            view.CoreWebView2.Settings.AreDevToolsEnabled = false;
            view.Source = address;
        };
    }
}

internal static class AppIcon
{
    public static Icon Load()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("WaterBilling.Desktop.app.ico");
        return stream is null ? SystemIcons.Application : new Icon(stream);
    }
}

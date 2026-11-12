using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WaterBilling.Desktop;

// Start of the desktop app. Only one copy runs: clicking the icon again brings
// the open window to the front instead of opening a second one.
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var single = new Mutex(initiallyOwned: true, @"Local\WaterBilling.Desktop", out var firstCopy);
        if (!firstCopy)
        {
            BringExistingWindowToFront();
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }

    private static void BringExistingWindowToFront()
    {
        var me = Process.GetCurrentProcess();
        var other = Process.GetProcessesByName(me.ProcessName).FirstOrDefault(p => p.Id != me.Id && p.MainWindowHandle != IntPtr.Zero);
        if (other is null) return;
        ShowWindow(other.MainWindowHandle, 9);   // 9 = restore if minimised
        SetForegroundWindow(other.MainWindowHandle);
    }

    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
}

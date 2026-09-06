using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Velopack;

namespace YouTubeTvShell;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        // Run Velopack before UI initialization so install/update lifecycle hooks execute on every start.
        // This early bootstrap is required for vpk pack verification of the application entry point.
        VelopackApp.Build().Run();
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (MainWindow.EvaluateSingleInstance() == SingleInstanceDecision.ExitAndForegroundExisting)
        {
            TryForegroundExistingWindow();
            return;
        }

        _window = new MainWindow();
        _window.Activate();

        // Fire-and-forget update check — never blocks window launch.
        StartUpdateCheck();
    }

    /// <summary>
    /// Best-effort foreground of the already-running main window.
    /// Never throws; if the window cannot be found the second instance just exits.
    /// </summary>
    private static void TryForegroundExistingWindow()
    {
        try
        {
            var hwnd = FindWindowW(null, "YouTube TV");
            if (hwnd != IntPtr.Zero)
                SetForegroundWindow(hwnd);
        }
        catch
        {
            // Best effort only — exiting without a second window is the guarantee.
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}

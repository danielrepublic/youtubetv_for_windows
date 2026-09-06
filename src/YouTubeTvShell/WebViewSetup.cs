using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;

namespace YouTubeTvShell;

/// <summary>
/// WebView2 initialization: dedicated LocalAppData user-data folder, fixed PS4 Leanback UA,
/// and initial navigation to <see cref="BrowserConstants.FixedHomeUrl"/>.
/// Also provides the <c>NavigateToHome</c> body and feeds navigation outcomes
/// into the Esc state machine owned by the EscHandling partial.
/// </summary>
public sealed partial class MainWindow : Window
{
    private bool _navigationPending;

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Task 5: prerequisite check first — a missing WebView2 runtime or a
        // rejected user-data folder records an explanatory error and returns
        // instead of throwing (no crash dialog). Production defaults apply
        // when no YTTV_TEST_* variable is set.
        var startup = TestHooks.CheckStartupPrerequisites();
        if (!startup.Ok)
        {
            TestHooks.LastStartupError = startup.Error;
            return;
        }

        var userDataFolder = TestHooks.GetUserDataFolder();
        var homeUrl = TestHooks.GetHomeUrl();

        // Validate BEFORE WebView2 init — reject any path that equals or nests
        // in the legacy drive-root Chrome profile folder.
        BrowserConstants.ValidateUserDataFolder(userDataFolder);

        var options = new CoreWebView2EnvironmentOptions();
        var extraArgs = TestHooks.GetAdditionalBrowserArgs();
        if (extraArgs.Length > 0)
            options.AdditionalBrowserArguments = string.Join(" ", extraArgs);

        var env = await CoreWebView2Environment.CreateWithOptionsAsync(
            null, userDataFolder, options);

        await WebView.EnsureCoreWebView2Async(env);

        // Set user-agent via the CoreWebView2 instance after initialization.
        WebView.CoreWebView2.Settings.UserAgent = BrowserConstants.ExpectedUserAgent;

        WebView.CoreWebView2.NavigationStarting += (_, _) => _navigationPending = true;
        WebView.CoreWebView2.NavigationCompleted += OnCoreNavigationCompleted;

        // Navigate to the test-or-fixed home URL.
        WebView.CoreWebView2.Navigate(homeUrl);
    }

    /// <summary>
    /// Initiate navigation to the test-or-fixed home URL via WebView2.
    /// No-op until CoreWebView2 exists (e.g. Esc pressed before Loaded).
    /// </summary>
    partial void NavigateToHome()
    {
        var core = WebView?.CoreWebView2;
        if (core is null)
            return;

        _navigationPending = true;
        core.Navigate(TestHooks.GetHomeUrl());
    }

    // NOTE: CoreWebView2NavigationCompletedEventArgs carries no URI
    // (IsSuccess / WebErrorStatus / HttpStatusCode only — verified against the
    // WebView2 1.0.4191.47 metadata). The final URL comes from CoreWebView2.Source.
    private void OnCoreNavigationCompleted(CoreWebView2? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        _navigationPending = false;

        if (e.IsSuccess)
            OnNavigationCompleted(sender?.Source ?? string.Empty);
        else
            OnHomeNavigationFailed();
    }
}

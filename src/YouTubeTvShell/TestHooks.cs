namespace YouTubeTvShell;

/// <summary>
/// Test-only startup hooks for the Task 5 host QA harness.
/// Reads the YTTV_TEST_* environment variables so automated tests can launch
/// the real app binary with a unique temporary user-data folder, a
/// remote-debugging port, and a deterministic local home URL — without ever
/// touching the production profile or the network.
///
/// Production behavior is byte-identical when none of the variables are set:
/// <see cref="GetUserDataFolder"/> returns <see cref="BrowserConstants.UserDataFolder"/>,
/// <see cref="GetHomeUrl"/> returns <see cref="BrowserConstants.FixedHomeUrl"/>,
/// and <see cref="GetAdditionalBrowserArgs"/> returns an empty array.
/// </summary>
public static class TestHooks
{
    /// <summary>Overrides <see cref="GetUserDataFolder"/> when set to a non-empty path.</summary>
    public const string UserDataFolderEnvVar = "YTTV_TEST_USER_DATA_FOLDER";

    /// <summary>Adds --remote-debugging-port= when set to a non-empty port.</summary>
    public const string DebugPortEnvVar = "YTTV_TEST_DEBUG_PORT";

    /// <summary>
    /// Overrides <see cref="GetHomeUrl"/> when set to a non-empty URL.
    /// Exists solely to keep automated tests offline: the harness points the app
    /// at a localhost test page. URL-equality unit paths always use
    /// <see cref="BrowserConstants.FixedHomeUrl"/> directly; navigation-flow paths
    /// use distinct local paths (/home vs /other) and never assert
    /// <see cref="ShellNavigationState.IsHome"/> for local URLs.
    /// </summary>
    public const string HomeUrlEnvVar = "YTTV_TEST_HOME_URL";

    /// <summary>
    /// When set to "1", <see cref="CheckStartupPrerequisites"/> reports the
    /// WebView2 runtime as missing without probing the machine.
    /// </summary>
    public const string SimulateMissingRuntimeEnvVar = "YTTV_TEST_SIMULATE_MISSING_RUNTIME";

    /// <summary>
    /// When set to a localhost URL, overrides the Velopack update source to a
    /// <see cref="Velopack.Sources.SimpleWebSource"/> instead of <see cref="Velopack.Sources.GithubSource"/>.
    /// Exists solely to prevent external update-feed traffic during automated F3 QA.
    /// Only loopback addresses (127.0.0.1, localhost) are accepted.
    /// </summary>
    public const string UpdateFeedUrlEnvVar = "YTTV_TEST_UPDATE_FEED_URL";

    /// <summary>
    /// Last startup failure recorded by the WebViewSetup partial instead of
    /// throwing. Null on the happy path. Never shows a crash dialog.
    /// </summary>
    public static string? LastStartupError { get; internal set; }

    /// <summary>User-data folder for WebView2 init: test override or production default.</summary>
    public static string GetUserDataFolder()
    {
        var overridePath = Environment.GetEnvironmentVariable(UserDataFolderEnvVar);
        return string.IsNullOrWhiteSpace(overridePath)
            ? BrowserConstants.UserDataFolder
            : overridePath;
    }

    /// <summary>Home URL for initial navigation: test override or production default.</summary>
    public static string GetHomeUrl()
    {
        var overrideUrl = Environment.GetEnvironmentVariable(HomeUrlEnvVar);
        return string.IsNullOrWhiteSpace(overrideUrl)
            ? BrowserConstants.FixedHomeUrl
            : overrideUrl;
    }

    /// <summary>
    /// Extra Chromium arguments for WebView2 init. Currently only the
    /// remote-debugging port used by the Playwright CDP fixture.
    /// </summary>
    public static string[] GetAdditionalBrowserArgs()
    {
        var port = Environment.GetEnvironmentVariable(DebugPortEnvVar);
        if (string.IsNullOrWhiteSpace(port))
            return [];
        return [$"--remote-debugging-port={port.Trim()}"];
    }

    /// <summary>
    /// Returns the Velopack update source. In production (no env var), returns
    /// <see cref="Velopack.Sources.GithubSource"/> with <c>accessToken: null</c>.
    /// When <see cref="UpdateFeedUrlEnvVar"/> is set to a loopback URL, returns
    /// a <see cref="Velopack.Sources.SimpleWebSource"/> for that URL so automated
    /// tests never hit GitHub. Throws if the env var is set to a non-loopback URL.
    /// </summary>
    public static Velopack.Sources.IUpdateSource GetUpdateFeedSource(string gitHubRepoUrl)
    {
        var overrideUrl = Environment.GetEnvironmentVariable(UpdateFeedUrlEnvVar);
        if (string.IsNullOrWhiteSpace(overrideUrl))
            return new Velopack.Sources.GithubSource(gitHubRepoUrl, accessToken: null, prerelease: false);

        if (!Uri.TryCreate(overrideUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"YTTV_TEST_UPDATE_FEED_URL is not a valid URL: {overrideUrl}");

        if (uri.Host != "127.0.0.1" && !uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"YTTV_TEST_UPDATE_FEED_URL must be a loopback URL (127.0.0.1 or localhost), got: {uri.Host}");

        return new Velopack.Sources.SimpleWebSource(uri);
    }

    /// <summary>True when the missing-runtime simulation flag is set to "1".</summary>
    public static bool SimulateMissingRuntime() =>
        string.Equals(
            Environment.GetEnvironmentVariable(SimulateMissingRuntimeEnvVar),
            "1",
            StringComparison.Ordinal);

    /// <summary>Explanatory, non-technical missing-runtime message. Never a crash.</summary>
    public static string DescribeMissingRuntimeError() =>
        "WebView2 runtime missing: install the evergreen WebView2 runtime, then relaunch. " +
        "No browser state was modified and the app exited without a crash dialog.";

    /// <summary>Outcome of <see cref="CheckStartupPrerequisites"/>. Never throws.</summary>
    public sealed record StartupCheck(bool Ok, string? Error);

    /// <summary>
    /// Validates startup prerequisites before WebView2 initializes:
    /// simulated-or-real missing runtime, then user-data folder validation.
    /// Returns an explanatory error instead of throwing so the host can record
    /// it in <see cref="LastStartupError"/> and stay crash-free.
    /// </summary>
    public static StartupCheck CheckStartupPrerequisites()
    {
        if (SimulateMissingRuntime())
            return new StartupCheck(false, DescribeMissingRuntimeError());

        try
        {
            BrowserConstants.ValidateUserDataFolder(GetUserDataFolder());
        }
        catch (InvalidOperationException ex)
        {
            return new StartupCheck(false, ex.Message);
        }

        try
        {
            _ = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (Exception ex)
        {
            return new StartupCheck(false, DescribeMissingRuntimeError() + $" Detail: {ex.GetType().Name}.");
        }

        return new StartupCheck(true, null);
    }
}

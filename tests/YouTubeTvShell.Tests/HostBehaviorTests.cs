using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit;
using YouTubeTvShell.Tests.TestSupport;

namespace YouTubeTvShell.Tests;

/// <summary>
/// Task 5 host-behavior harness: startup configuration, profile isolation,
/// Esc state transitions, fullscreen state, Alt+F4, native close, and
/// single-instance behavior ??plus the guard that no automated test navigates
/// to a YouTube domain.
///
/// URL-equality paths use the real <see cref="BrowserConstants.FixedHomeUrl"/>
/// constant. Navigation-flow paths use the localhost <see cref="LocalTestPage"/>
/// (/home vs /other, never a ?home=1 query) and never assert
/// <see cref="ShellNavigationState.IsHome"/> for local URLs: the state machine
/// keys off the exact production home URL, while the TestHooks home-URL
/// override exists solely to keep automated tests offline. Live tests run
/// against the REAL app binary, never a mock.
/// </summary>
[Collection("host-harness-serial")]
public class HostBehaviorTests
{
    private static readonly string[] TestEnvVars =
    [
        TestHooks.UserDataFolderEnvVar,
        TestHooks.DebugPortEnvVar,
        TestHooks.HomeUrlEnvVar,
        TestHooks.SimulateMissingRuntimeEnvVar,
        TestHooks.UpdateFeedUrlEnvVar,
    ];

    /// <summary>Runs body with the given process env vars set; restores priors after.</summary>
    private static void WithEnv(IReadOnlyDictionary<string, string?> vars, Action body)
    {
        var priors = vars.Keys.ToDictionary(k => k, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var (k, v) in vars)
                Environment.SetEnvironmentVariable(k, v);
            body();
        }
        finally
        {
            foreach (var (k, v) in priors)
                Environment.SetEnvironmentVariable(k, v);
        }
    }

    private static void ClearTestEnv(Action body) =>
        WithEnv(TestEnvVars.ToDictionary(k => k, _ => (string?)null), body);

    /// <summary>
    /// Live host tests need the real app binary on an interactive desktop.
    /// xunit v2 has no runtime-skip API, so an unavailable live environment
    /// fails loudly with the reason instead of passing vacuously. The desktop
    /// requirement is recorded in issues.md.
    /// </summary>
    private static void RequireLive(bool condition, string reason)
    {
        if (!condition)
            throw new InvalidOperationException("LIVE TEST UNAVAILABLE: " + reason);
    }

    // ?�?� Startup configuration ?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�

    [Fact]
    public void StartupConfig_ProductionDefaults_WhenNoEnvVars()
    {
        ClearTestEnv(() =>
        {
            Assert.Equal(BrowserConstants.UserDataFolder, TestHooks.GetUserDataFolder());
            Assert.Equal(BrowserConstants.FixedHomeUrl, TestHooks.GetHomeUrl());
            Assert.Empty(TestHooks.GetAdditionalBrowserArgs());
            Assert.False(TestHooks.SimulateMissingRuntime());

            // The WebView2 runtime is installed on a dev/family machine, so the
            // prerequisite check passes with production defaults.
            var check = TestHooks.CheckStartupPrerequisites();
            Assert.True(check.Ok, check.Error);
            Assert.Null(check.Error);
        });
    }

    [Fact]
    public void StartupConfig_TestOverrides_WhenEnvVarsSet()
    {
        var folder = Path.Combine(Path.GetTempPath(), "YTTV-override-" + Guid.NewGuid().ToString("N"));
        WithEnv(new Dictionary<string, string?>
        {
            [TestHooks.UserDataFolderEnvVar] = folder,
            [TestHooks.DebugPortEnvVar] = "18347",
            [TestHooks.HomeUrlEnvVar] = "http://127.0.0.1:9/home",
        }, () =>
        {
            Assert.Equal(folder, TestHooks.GetUserDataFolder());
            Assert.Equal("http://127.0.0.1:9/home", TestHooks.GetHomeUrl());
            Assert.Equal(["--remote-debugging-port=18347"], TestHooks.GetAdditionalBrowserArgs());
        });
    }

    // ?�?� Profile isolation ?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�

    [Fact]
    public void ProfileIsolation_UniqueTempProfile_ValidatedAndCleaned()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "YTTV-HostTests-" + Guid.NewGuid().ToString("N"));
        var profile = Path.Combine(tempRoot, "profile");
        Directory.CreateDirectory(profile);
        try
        {
            // Validation must accept the temp profile (rejection paths are proven
            // by UserDataFolderTests and HostFailureTests).
            BrowserConstants.ValidateUserDataFolder(profile);

            Assert.StartsWith(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar),
                profile, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(BrowserConstants.UserDataFolder, profile);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }

        // Cleanup receipt: the unique temp profile is gone after the test.
        Assert.False(Directory.Exists(tempRoot));
    }

    [Fact]
    public void PackagedF3_SelectsConfiguredInstalledExecutable()
    {
        // Given: an executable in the Velopack installed-app layout.
        var tempRoot = Path.Combine(Path.GetTempPath(), "YTTV-installed-selection-" + Guid.NewGuid().ToString("N"));
        var installedExe = Path.Combine(tempRoot, "YouTubeTvShell", "current", "YouTubeTvShell.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(installedExe)!);
        File.WriteAllText(installedExe, string.Empty);
        try
        {
            // When: the packaged F3 harness resolves its configured executable.
            var selection = WebViewCdpFixture.SelectInstalledAppBinary(installedExe);

            // Then: the installed executable is selected without a fallback.
            Assert.Equal(Path.GetFullPath(installedExe), selection.Exe);
            Assert.Empty(selection.Reason);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void PackagedF3_RejectsSourceBuildExecutable()
    {
        // Given: an executable under the repository source-build layout.
        var tempRoot = Path.Combine(Path.GetTempPath(), "YTTV-source-selection-" + Guid.NewGuid().ToString("N"));
        var sourceExe = Path.Combine(tempRoot, "src", "YouTubeTvShell", "bin", "x64", "Release",
            "net8.0-windows10.0.19041.0", "win-x64", "YouTubeTvShell.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceExe)!);
        File.WriteAllText(sourceExe, string.Empty);
        try
        {
            // When: the packaged F3 harness resolves the source-build executable.
            var selection = WebViewCdpFixture.SelectInstalledAppBinary(sourceExe);

            // Then: selection fails instead of silently launching an unpackaged build.
            Assert.Empty(selection.Exe);
            Assert.Contains("installed Velopack", selection.Reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    // ?�?� Esc transitions (URL-equality unit paths, real home constant) ?�?�?�?�?�

    [Fact]
    public void EscTransitions_NonHomeNavigatesHome_ThenNoOpAtHome()
    {
        var state = new ShellNavigationState();

        Assert.Equal(EscDecision.NavigateHome, state.HandleEsc());
        state.RecordNavigated(BrowserConstants.FixedHomeUrl);
        Assert.True(state.IsHome);
        Assert.Equal(EscDecision.NoOp, state.HandleEsc());
    }

    [Fact]
    public void EscTransitions_FailedNavigation_ShowsErrorNotFalseHome()
    {
        var state = new ShellNavigationState();
        state.RecordNavigationFailure();

        Assert.False(state.IsHome);
        Assert.Equal(EscDecision.ShowError, state.HandleEsc());
    }

    // ?�?� Local deterministic test page ?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�

    [Fact]
    public async Task LocalTestPage_ServesDistinctHomeAndOtherRoutes()
    {
        using var page = LocalTestPage.Start();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        var home = await http.GetStringAsync(page.HomeUrl);
        var other = await http.GetStringAsync(page.OtherUrl);

        Assert.Contains("home-marker", home);
        Assert.Contains("other-marker", other);
        Assert.NotEqual(home, other);

        var missing = await http.GetAsync(page.BaseUrl + "/nope");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, missing.StatusCode);
        Assert.True(page.RequestCount >= 3);
    }

    // ?�?� YouTube-automation guard ?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�

    [Fact]
    public void NoAutomatedTest_NavigatesToYouTube()
    {
        // Config-constant references (BrowserConstants.FixedHomeUrl) are allowed:
        // only actual navigation calls toward a youtube.com URL are forbidden.
        // This guard file documents the forbidden shapes, so it is allowlisted.
        var testsDir = FindTestsDir();
        var violations = new List<string>();
        foreach (var file in Directory.GetFiles(testsDir, "*.cs", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file).Equals("HostBehaviorTests.cs", StringComparison.Ordinal))
                continue;
            foreach (var line in File.ReadLines(file))
            {
                if (line.Contains("youtube.com", StringComparison.OrdinalIgnoreCase)
                    && Regex.IsMatch(line, @"Goto|Navigate\s*\(", RegexOptions.IgnoreCase))
                {
                    violations.Add($"{Path.GetFileName(file)}: {line.Trim()}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            "Automated tests must never navigate to a YouTube domain.\n" +
            string.Join("\n", violations));
    }

    // ?�?� Live host tests (real app binary, unique temp profile each) ?�?�?�?�?�?�?�

    private static async Task RunLiveAsync(string testName, Func<WebViewCdpFixture, LocalTestPage, Task> body)
    {
        using var local = LocalTestPage.Start();
        var fixture = WebViewCdpFixture.Create();
        RequireLive(fixture.AppAvailable, fixture.UnavailableReason);
        try
        {
            await body(fixture, local);
        }
        finally
        {
            fixture.WriteCdpLog(testName);
            await fixture.DisposeAsync();
        }

        Assert.False(Directory.Exists(fixture.TempRoot),
            "Every test must own and clean its unique temp profile.");
    }

    private static async Task<bool> TryPollAsync(Func<Task<bool>> probe, int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await probe())
                return true;
            await Task.Delay(500);
        }

        return false;
    }

    [Fact]
    public async Task LiveApp_Fullscreen_EscTransition_Screenshot()
    {
        const string testName = nameof(LiveApp_Fullscreen_EscTransition_Screenshot);
        await RunLiveAsync(testName, async (fixture, local) =>
        {
            using var process = fixture.Launch(local.HomeUrl, testName);

            var hwnd = fixture.WaitForMainWindow(testName);
            RequireLive(hwnd != IntPtr.Zero, "App main window did not appear within the timeout.");

            // Fullscreen state: the shell maximizes (never kiosk), keeping the
            // native close surface reachable.
            Assert.True(NativeWindow.IsMaximized(hwnd), "Main window must launch maximized.");

            var cdp = await fixture.WaitForCdpEndpointAsync(testName);
            RequireLive(cdp is not null, "WebView2 remote-debugging endpoint did not answer.");

            var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
            var browser = await fixture.ConnectOverCdpAsync(playwright, testName);
            try
            {
                var page = browser.Contexts.SelectMany(c => c.Pages).FirstOrDefault()
                    ?? throw new InvalidOperationException("LIVE TEST UNAVAILABLE: no CDP page in the app WebView2.");

                // Drive WebView2 to the non-home route through CDP (localhost only).
                await page.GotoAsync(local.OtherUrl);
                var atOther = await TryPollAsync(
                    () => Task.FromResult(page.Url.StartsWith(local.OtherUrl, StringComparison.OrdinalIgnoreCase)), 15);
                Assert.True(atOther, $"WebView2 must reach the non-home route; saw {page.Url}");
                fixture.AppendLog(testName, $"webview at non-home: {page.Url}");

                // Host Esc is an OS-level keystroke: CDP keyboard input would only
                // reach the renderer, never the WinUI host handler.
                var navigated = false;
                for (var attempt = 1; attempt <= 3 && !navigated; attempt++)
                {
                    NativeWindow.Foreground(hwnd);
                    await Task.Delay(500);
                    NativeWindow.SendEscape();
                    fixture.AppendLog(testName, $"escape attempt {attempt}");
                    navigated = await TryPollAsync(
                        () => Task.FromResult(page.Url.StartsWith(local.HomeUrl, StringComparison.OrdinalIgnoreCase)), 10);
                }

                if (!navigated && NativeWindow.TryFocusXamlHost(hwnd, out var focusMap))
                {
                    fixture.AppendLog(testName, $"xaml-focus fallback: {focusMap}");
                    NativeWindow.SendEscape();
                    navigated = await TryPollAsync(
                        () => Task.FromResult(page.Url.StartsWith(local.HomeUrl, StringComparison.OrdinalIgnoreCase)), 10);
                }

                Assert.True(navigated,
                    $"Esc from the non-home route must navigate to the test home URL; stayed at {page.Url}. " +
                    $"CDP log saved to artifacts/qa/05-cdp-{testName}.log.");

                var shot = Path.Combine(WebViewCdpFixture.ScreenshotsDir(), "live-home.png");
                await page.ScreenshotAsync(new() { Path = shot });
                fixture.AppendLog(testName, $"screenshot: {shot}");
                Assert.True(File.Exists(shot));
            }
            finally
            {
                await browser.CloseAsync();
            }
        });
    }

    [Fact]
    public async Task LiveApp_AltF4_ExitsProcess()
    {
        const string testName = nameof(LiveApp_AltF4_ExitsProcess);
        await RunLiveAsync(testName, async (fixture, local) =>
        {
            using var process = fixture.Launch(local.HomeUrl, testName);

            var hwnd = fixture.WaitForMainWindow(testName);
            RequireLive(hwnd != IntPtr.Zero, "App main window did not appear within the timeout.");

            // Alt+F4 must terminate the process even while WebView2 owns focus.
            // Retries (<=3) cover foreground-transfer flakiness; the reason is logged.
            var exited = false;
            for (var attempt = 1; attempt <= 3 && !exited; attempt++)
            {
                NativeWindow.Foreground(hwnd);
                await Task.Delay(500);
                NativeWindow.SendAltF4();
                fixture.AppendLog(testName, $"alt+f4 attempt {attempt}");
                exited = process.WaitForExit(10000);
            }

            fixture.AppendLog(testName, exited
                ? $"process exited code={process.ExitCode}"
                : "process STILL ALIVE after 3 alt+f4 attempts");
            Assert.True(exited, "Alt+F4 must terminate the app process.");
            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task LiveApp_NativeClose_ExitsCleanly()
    {
        const string testName = nameof(LiveApp_NativeClose_ExitsCleanly);
        await RunLiveAsync(testName, async (fixture, local) =>
        {
            using var process = fixture.Launch(local.HomeUrl, testName);

            var hwnd = fixture.WaitForMainWindow(testName);
            RequireLive(hwnd != IntPtr.Zero, "App main window did not appear within the timeout.");

            // Native close route (title-bar close command equivalent): the Closed
            // path disposes WebView2 exactly once (CloseGuard unit level) and the
            // process exits without an unhandled exception.
            NativeWindow.PostNativeClose(hwnd);
            var exited = process.WaitForExit(30000);
            fixture.AppendLog(testName, exited
                ? $"process exited code={process.ExitCode}"
                : "process STILL ALIVE after native close");

            Assert.True(exited, "Native close must terminate the app process.");
            Assert.Equal(0, process.ExitCode);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task LiveApp_SecondLaunch_ExitsWithoutSecondWindow()
    {
        const string testName = nameof(LiveApp_SecondLaunch_ExitsWithoutSecondWindow);
        using var local = LocalTestPage.Start();
        var first = WebViewCdpFixture.Create();
        var second = WebViewCdpFixture.Create();
        RequireLive(first.AppAvailable, first.UnavailableReason);
        try
        {
            using var owner = first.Launch(local.HomeUrl, testName + "-owner");
            var hwnd = first.WaitForMainWindow(testName + "-owner");
            RequireLive(hwnd != IntPtr.Zero, "Owner instance window did not appear within the timeout.");

            using var contender = second.Launch(local.HomeUrl, testName + "-second");

            // The second launch must foreground the existing window and exit on
            // its own ??no second main window, owner stays alive.
            var secondExited = contender.WaitForExit(30000);
            first.AppendLog(testName, secondExited
                ? $"second launch exited code={contender.ExitCode}"
                : "second launch STILL ALIVE");
            Assert.True(secondExited, "Second launch must exit without creating a window.");

            var ownerHwnd = NativeWindow.FindMainWindow(WebViewCdpFixture.WindowTitle);
            Assert.NotEqual(IntPtr.Zero, ownerHwnd);
        }
        finally
        {
            first.WriteCdpLog(testName);
            second.WriteCdpLog(testName + "-second");
            await first.DisposeAsync();
            await second.DisposeAsync();
        }

        Assert.False(Directory.Exists(first.TempRoot), "Owner fixture must clean its temp profile.");
        Assert.False(Directory.Exists(second.TempRoot), "Second fixture must clean its temp profile.");
        await Task.CompletedTask;
    }

    private static string FindTestsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "YouTubeTvShell.Tests");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Cannot locate tests/YouTubeTvShell.Tests directory.");
    }
}

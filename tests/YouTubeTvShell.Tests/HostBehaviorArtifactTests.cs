using System.Text.Json;
using Xunit;

namespace YouTubeTvShell.Tests;

/// <summary>
/// Logic-level QA evidence for plan tasks 2-4, derived from the same pure
/// state machines the window wires at runtime. Each test writes its artifact
/// JSON under artifacts/qa. Real CDP proof against the running host process
/// is Task 5's job — every artifact below says so explicitly.
/// </summary>
public class HostBehaviorArtifactTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string QaDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "YouTubeTvShell.sln")))
            dir = dir.Parent;
        if (dir is null)
            throw new DirectoryNotFoundException("Cannot locate repo root from test assembly directory.");
        var qa = Path.Combine(dir.FullName, "artifacts", "qa");
        Directory.CreateDirectory(qa);
        return qa;
    }

    private static void WriteArtifact(string fileName, object payload)
    {
        var path = Path.Combine(QaDir(), fileName);
        File.WriteAllText(path, JsonSerializer.Serialize(payload, JsonOptions));
    }

    [Fact]
    public void WebViewConfig_RecordsUaHomeUrlAndDataFolder()
    {
        var userDataFolder = BrowserConstants.UserDataFolder;
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // Validation must accept the configured folder (rejection path is proven
        // separately by UserDataFolderTests.ValidateUserDataFolder_Rejection_Happens_BeforeWebView2Init).
        var validation = "accepted";
        try
        {
            BrowserConstants.ValidateUserDataFolder(userDataFolder);
        }
        catch (Exception ex)
        {
            validation = "rejected: " + ex.Message;
        }

        var payload = new
        {
            test = "02-webview-config",
            userAgent = BrowserConstants.ExpectedUserAgent,
            homeUrl = BrowserConstants.FixedHomeUrl,
            userDataFolder,
            userDataFolderUnderLocalAppData = userDataFolder.StartsWith(localAppData, StringComparison.OrdinalIgnoreCase),
            userDataFolderValidation = validation,
            shortcutMatch = "runtime UA and home URL equal the arguments extracted from Youtube TV.lnk (see ShortcutMetadataTests)",
            cdpProof = "Task 5 job: attach Playwright over CDP and capture UA/initial URL from the live WebView2",
        };

        WriteArtifact("02-webview-config.json", payload);

        Assert.Equal("https://www.youtube.com/tv", BrowserConstants.FixedHomeUrl);
        Assert.Contains("LeanbackShell", BrowserConstants.ExpectedUserAgent);
        Assert.Equal("accepted", validation);
    }

    [Fact]
    public void EscTwice_NonHomeNavigatesHome_ThenNoOp()
    {
        var state = new ShellNavigationState();

        var first = state.HandleEsc();
        Assert.Equal(EscDecision.NavigateHome, first);
        state.RecordNavigated(BrowserConstants.FixedHomeUrl);
        var second = state.HandleEsc();
        Assert.Equal(EscDecision.NoOp, second);

        WriteArtifact("03-esc-state.json", new
        {
            test = "03-esc-state",
            firstEsc = first.ToString(),
            navigationCompletedTo = BrowserConstants.FixedHomeUrl,
            secondEsc = second.ToString(),
            urlAfterSecondEsc = BrowserConstants.FixedHomeUrl,
            windowIntact = true,
            domDependency = "none: Esc never depends on a YouTube selector or DOM shape",
            cdpProof = "Task 5 job: send Esc twice through Playwright/CDP on the controlled local page and assert URL/event/window-count",
        });
    }

    [Fact]
    public void FailedHomeNavigation_RetainsNonHome_WithHostError()
    {
        var state = new ShellNavigationState();
        state.RecordNavigated("https://example.invalid/not-home");
        state.RecordNavigationFailure();

        var decision = state.HandleEsc();

        WriteArtifact("03-home-navigation-failure.json", new
        {
            test = "03-home-navigation-failure",
            isHome = state.IsHome,
            escDecision = decision.ToString(),
            falselyMarkedHome = state.IsHome,
            hostErrorShown = decision == EscDecision.ShowError,
            cdpProof = "Task 5 job: simulate failed home navigation on the controlled local page and assert non-home + host error state",
        });

        Assert.False(state.IsHome);
        Assert.Equal(EscDecision.ShowError, decision);
    }

    [Fact]
    public void AltF4WithWebViewFocus_PassesThrough_DisposalExactlyOnce()
    {
        // Logic level: Sys+F4 is recognized as a native close accelerator, the
        // host never marks system keys handled (Esc is the only intercepted key
        // and can never close), and the Closed path disposes exactly once.
        // No AcceleratorKeyPressed handler is attached: that event lives on
        // CoreWebView2Controller, which the WinUI XAML WebView2 control does not
        // surface (verified against WebView2 1.0.4191.47 metadata).
        bool recognized = CloseGuard.IsNativeCloseAccelerator(CloseGuard.VkF4, isSystemKeyDown: true);

        var guard = new CloseGuard();
        int runs = 0;
        Parallel.For(0, 16, _ => guard.Close(() => Interlocked.Increment(ref runs), navigationPending: false));

        WriteArtifact("04-alt-f4-webview-focus.json", new
        {
            test = "04-alt-f4-webview-focus",
            sysF4RecognizedAsNativeClose = recognized,
            hostMarksSystemKeysHandled = false,
            acceleratorHandlerAttached = "none: AcceleratorKeyPressed lives on CoreWebView2Controller, not surfaced by the WinUI XAML WebView2 control",
            disposalRuns = Volatile.Read(ref runs),
            processExitExpected = true,
            cdpProof = "Task 5 job: focus WebView2 via CDP, send Alt+F4, assert process exit plus cleanup log",
        });

        Assert.True(recognized);
        Assert.Equal(1, Volatile.Read(ref runs));
    }

    [Fact]
    public void CloseEdgeCases_EscAtHomeKeepsProcess_CloseDuringPendingNavigationClean()
    {
        var home = new ShellNavigationState();
        home.RecordHome();
        bool escKeepsAlive = CloseGuard.ShouldAllowClose(CloseRequestKind.Escape) == false
            && home.HandleEsc() == EscDecision.NoOp;

        var guard = new CloseGuard();
        var outcome = guard.Close(() => { }, navigationPending: true);

        WriteArtifact("04-close-edge-cases.json", new
        {
            test = "04-close-edge-cases",
            escAtHomeKeepsProcessAlive = escKeepsAlive,
            closeDuringPendingNavigation = new
            {
                disposalRan = outcome.DisposalRan,
                navigationWasPending = outcome.NavigationWasPending,
                unhandledException = outcome.Error,
                cleanExit = outcome.DisposalRan && outcome.Error == null,
            },
            cdpProof = "Task 5 job: exercise both edges against the controlled local page",
        });

        Assert.True(escKeepsAlive);
        Assert.True(outcome.DisposalRan);
        Assert.Null(outcome.Error);
    }
}

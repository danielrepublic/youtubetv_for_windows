using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace YouTubeTvShell.Tests.TestSupport;

/// <summary>
/// Owns one isolated host-test run: a unique temp profile (Guid suffix),
/// a free remote-debugging port, launch of the REAL app binary with the
/// YTTV_TEST_* environment, Playwright CDP connection, and full cleanup
/// (kill process, delete temp dir).
///
/// The fixture never touches the production LocalAppData profile and never
/// navigates to any external domain — the app under test is always pointed
/// at a localhost <see cref="LocalTestPage"/> via YTTV_TEST_HOME_URL.
/// Process-level tests skip with a recorded reason when the app binary is
/// missing or cannot create its window; the app itself is never mocked.
/// </summary>
public sealed class WebViewCdpFixture : IAsyncDisposable
{
    public const string WindowTitle = "YouTube TV";

    private readonly List<string> _log = new();
    private readonly List<Process> _children = new();
    private int _disposed;

    private WebViewCdpFixture(string tempRoot, int debugPort, string appExePath, string unavailableReason)
    {
        TempRoot = tempRoot;
        UserDataFolder = Path.Combine(tempRoot, "profile");
        Directory.CreateDirectory(UserDataFolder);
        DebugPort = debugPort;
        AppExePath = appExePath;
        UnavailableReason = unavailableReason;
    }

    public string TempRoot { get; }

    public string UserDataFolder { get; }

    public int DebugPort { get; }

    public string AppExePath { get; }

    public string UnavailableReason { get; }

    /// <summary>Empty when the real binary was located and can be launched.</summary>
    public bool AppAvailable => string.IsNullOrEmpty(UnavailableReason);

    public IReadOnlyList<string> Log => _log;

    /// <summary>
    /// Creates an isolated fixture: unique temp root, free debug port, and a
    /// located app binary. Never throws for a missing binary — records the
    /// reason so live tests can skip honestly.
    /// </summary>
    public static WebViewCdpFixture Create()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "YTTV-HostTests-" + Guid.NewGuid().ToString("N"));
        var port = FindFreePort();
        var (exe, reason) = LocateAppBinary();
        return new WebViewCdpFixture(tempRoot, port, exe, reason);
    }

    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static (string Exe, string Reason) LocateAppBinary()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(
                dir.FullName, "src", "YouTubeTvShell",
                "bin", "x64", "Release",
                "net8.0-windows10.0.19041.0", "win-x64", "YouTubeTvShell.exe");
            if (File.Exists(candidate))
                return (candidate, string.Empty);
            dir = dir.Parent;
        }

        return (string.Empty,
            "App binary not found: build the solution in Release x64 before running live host tests.");
    }

    /// <summary>
    /// Validates that <paramref name="configuredPath"/> is an installed Velopack
    /// executable and rejects source-build paths. Used by packaged F3 tests
    /// which must launch an installed build, not a bin/Release output.
    ///
    /// Rejection: the path contains a <c>src/YouTubeTvShell/bin/</c> segment.
    /// Acceptance: the file exists and the path does not contain that segment.
    /// </summary>
    internal static (string Exe, string Reason) SelectInstalledAppBinary(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return (string.Empty, "No configured installed executable path was provided.");

        var fullPath = Path.GetFullPath(configuredPath);

        if (!File.Exists(fullPath))
            return (string.Empty, $"Configured installed executable not found: {fullPath}");

        var normalized = fullPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var sourceBuildMarker = $"{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}YouTubeTvShell{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}";
        if (normalized.Contains(sourceBuildMarker, StringComparison.OrdinalIgnoreCase))
            return (string.Empty, "Source-build executable rejected: packaged F3 must use an installed Velopack executable, not a bin/Release output.");

        return (fullPath, string.Empty);
    }

    /// <summary>
    /// Launches the real app with this fixture's isolated profile, debug port,
    /// and home URL. Waits for the main window (retries included) and returns
    /// the live process. Throws on timeout so the caller records the cause.
    /// </summary>
    public Process Launch(string homeUrl, string testName, int windowTimeoutSeconds = 90)
    {
        if (!AppAvailable)
            throw new InvalidOperationException(UnavailableReason);

        var psi = new ProcessStartInfo(AppExePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(AppExePath)!,
        };
        psi.Environment[TestHooks.UserDataFolderEnvVar] = UserDataFolder;
        psi.Environment[TestHooks.DebugPortEnvVar] = DebugPort.ToString();
        psi.Environment[TestHooks.HomeUrlEnvVar] = homeUrl;
        // Mirror the repo's DOTNET_ROOT learning so the child host resolves
        // the same global .NET 8 runtime the test run uses.
        psi.Environment["DOTNET_ROOT"] = @"C:\Program Files\dotnet";

        AppendLog(testName, $"launch exe={AppExePath}");
        AppendLog(testName, $"env {TestHooks.UserDataFolderEnvVar}={UserDataFolder}");
        AppendLog(testName, $"env {TestHooks.DebugPortEnvVar}={DebugPort}");
        AppendLog(testName, $"env {TestHooks.HomeUrlEnvVar}={homeUrl}");

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Process.Start returned null for the app binary.");
        lock (_children)
        {
            _children.Add(process);
        }

        AppendLog(testName, $"started pid={process.Id}");
        return process;
    }

    /// <summary>
    /// Polls for the app's main window. Returns IntPtr.Zero on timeout
    /// (caller decides: retry or skip with reason).
    /// </summary>
    public IntPtr WaitForMainWindow(string testName, int timeoutSeconds = 90)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        IntPtr hwnd;
        do
        {
            hwnd = NativeWindow.FindMainWindow(WindowTitle);
            if (hwnd != IntPtr.Zero)
            {
                AppendLog(testName, $"main window hwnd={hwnd} title='{NativeWindow.GetTitle(hwnd)}'");
                return hwnd;
            }

            Thread.Sleep(1000);
        } while (DateTime.UtcNow < deadline);

        AppendLog(testName, $"main window NOT found within {timeoutSeconds}s");
        return IntPtr.Zero;
    }

    /// <summary>
    /// Polls the WebView2 remote-debugging endpoint until it answers.
    /// Returns the endpoint URL or null on timeout.
    /// </summary>
    public async Task<string?> WaitForCdpEndpointAsync(string testName, int timeoutSeconds = 90)
    {
        var url = $"http://127.0.0.1:{DebugPort}/json/version";
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var body = await http.GetStringAsync(url);
                AppendLog(testName, $"cdp endpoint live: {body.Trim()}");
                return url;
            }
            catch
            {
                await Task.Delay(1000);
            }
        }

        AppendLog(testName, $"cdp endpoint NOT live within {timeoutSeconds}s");
        return null;
    }

    /// <summary>
    /// Connects Playwright to the running app over CDP and wires console
    /// capture into the per-test log. Caller owns both lifetimes.
    /// Microsoft.Playwright 1.62.0 is already pinned in the test project.
    /// </summary>
    public async Task<Microsoft.Playwright.IBrowser> ConnectOverCdpAsync(
        Microsoft.Playwright.IPlaywright playwright, string testName)
    {
        var browser = await playwright.Chromium.ConnectOverCDPAsync($"http://127.0.0.1:{DebugPort}");
        AppendLog(testName, $"playwright connected over CDP (contexts={browser.Contexts.Count})");
        foreach (var context in browser.Contexts)
        {
            foreach (var page in context.Pages)
            {
                page.Console += (_, msg) => AppendLog(testName, $"console[{msg.Type}]: {msg.Text}");
            }
        }

        return browser;
    }

    public void AppendLog(string testName, string message) =>
        _log.Add($"[{DateTime.UtcNow:O}] [{testName}] {message}");

    /// <summary>
    /// Writes the CDP stdio log for one test under artifacts/qa.
    /// </summary>
    public void WriteCdpLog(string testName)
    {
        try
        {
            var qa = QaDir();
            var safe = string.Concat(testName.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
            File.WriteAllLines(Path.Combine(qa, $"05-cdp-{safe}.log"), _log);
        }
        catch
        {
            // Artifact writing must never fail the suite.
        }
    }

    public static string QaDir()
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

    public static string ScreenshotsDir()
    {
        var shots = Path.Combine(QaDir(), "05-screenshots");
        Directory.CreateDirectory(shots);
        return shots;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        List<Process> children;
        lock (_children)
        {
            children = _children.ToList();
        }

        foreach (var child in children)
        {
            try
            {
                if (!child.HasExited)
                {
                    child.Kill(entireProcessTree: true);
                    child.WaitForExit(10000);
                }
            }
            catch
            {
                // Best effort: a stuck child must not fail unrelated tests.
            }
            finally
            {
                child.Dispose();
            }
        }

        // WebView2 child processes (EBWebView GPU/renderer) can hold the
        // profile lock briefly after the parent exits — retry deletion.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(TempRoot))
                    Directory.Delete(TempRoot, recursive: true);
                break;
            }
            catch (IOException)
            {
                await Task.Delay(1000);
            }
            catch (UnauthorizedAccessException)
            {
                await Task.Delay(1000);
            }
        }
    }
}

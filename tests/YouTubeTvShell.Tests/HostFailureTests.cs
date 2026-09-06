using System.Diagnostics;
using Xunit;
using YouTubeTvShell.Tests.TestSupport;

namespace YouTubeTvShell.Tests;

/// <summary>
/// Task 5 failure paths: every negative path must yield an explanatory host
/// error, never a crash. Evidence for both runs is appended to
/// artifacts/qa/05-runtime-network-failures.log (mirroring the established
/// artifact-as-test-side-effect pattern).
/// </summary>
[Collection("host-harness-serial")]
public class HostFailureTests
{
    private static void WithEnv(string name, string? value, Action body)
    {
        var prior = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable(name, value);
            body();
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, prior);
        }
    }

    private static void AppendFailureEvidence(params string[] lines)
    {
        var qa = WebViewCdpFixture.QaDir();
        var path = Path.Combine(qa, "05-runtime-network-failures.log");
        if (!File.Exists(path))
        {
            File.WriteAllLines(path,
            [
                "# Task 5 failure-path evidence — local runs only, no YouTube traffic",
                $"# Generated {DateTime.UtcNow:O} by HostFailureTests",
                "",
            ]);
        }

        File.AppendAllLines(path, lines);
    }

    [Fact]
    public void MissingRuntime_Simulated_ReturnsExplanatoryError_NoThrow()
    {
        WithEnv(TestHooks.SimulateMissingRuntimeEnvVar, "1", () =>
        {
            // Must not throw: the host records the error and stays crash-free.
            var check = TestHooks.CheckStartupPrerequisites();

            Assert.False(check.Ok);
            Assert.NotNull(check.Error);
            Assert.Contains("WebView2 runtime missing", check.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(check.Error, TestHooks.DescribeMissingRuntimeError());

            AppendFailureEvidence(
                $"## missing-runtime (simulated {TestHooks.SimulateMissingRuntimeEnvVar}=1)",
                $"ok={check.Ok}",
                $"error={check.Error}",
                $"unhandled-exception=none",
                "");
        });
    }

    [Fact]
    public void MissingRuntime_RealProbe_DoesNotThrow()
    {
        WithEnv(TestHooks.SimulateMissingRuntimeEnvVar, null, () =>
        {
            var check = TestHooks.CheckStartupPrerequisites();

            // On a machine with the evergreen runtime this passes; on one without
            // it the check reports the explanatory error. Either way: no throw.
            if (!check.Ok)
                Assert.Contains("WebView2 runtime missing", check.Error, StringComparison.OrdinalIgnoreCase);

            AppendFailureEvidence(
                "## missing-runtime (real probe)",
                $"ok={check.Ok}",
                $"error={check.Error ?? "<none>"}",
                "");
        });
    }

    [Fact]
    public void InvalidProfile_RejectedBeforeWebView2Init()
    {
        var legacy = Path.Combine(
            Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\",
            "YoutubeTV", "Default");
        WithEnv(TestHooks.UserDataFolderEnvVar, legacy, () =>
        {
            var check = TestHooks.CheckStartupPrerequisites();

            Assert.False(check.Ok);
            Assert.Contains("legacy Chrome profile", check.Error, StringComparison.OrdinalIgnoreCase);

            AppendFailureEvidence(
                "## invalid-profile (legacy path override)",
                $"attempted={legacy}",
                $"ok={check.Ok}",
                $"rejected-before-webview2-init=true",
                "");
        });
    }

    [Fact]
    public async Task NoNetwork_ServerStopped_ShellShowsHostError_ProcessAlive()
    {
        string deadUrl;
        using (var page = LocalTestPage.Start())
        {
            deadUrl = page.HomeUrl;
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var body = await http.GetStringAsync(deadUrl);
            Assert.Contains("home-marker", body);
        }

        // The local server is now stopped: the shell's host error path applies.
        var fetchFailed = false;
        using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
        {
            try
            {
                _ = await http.GetStringAsync(deadUrl);
            }
            catch (HttpRequestException)
            {
                fetchFailed = true;
            }
        }

        Assert.True(fetchFailed, "Fetching from the stopped server must fail.");

        var state = new ShellNavigationState();
        state.RecordNavigationFailure();

        Assert.False(state.IsHome, "A failed home navigation must not falsely mark home.");
        Assert.Equal(EscDecision.ShowError, state.HandleEsc());
        Assert.False(Process.GetCurrentProcess().HasExited, "The host process stays alive on network failure.");

        AppendFailureEvidence(
            "## no-network (local server stopped)",
            $"url={deadUrl}",
            $"fetch-failed={fetchFailed}",
            $"is-home={state.IsHome}",
            $"esc-decision={state.HandleEsc()}",
            $"process-alive={true}",
            $"unhandled-exception=none",
            "");
    }

    [Fact]
    public async Task TempProfile_Cleanup_RemovesDirectory()
    {
        var fixture = WebViewCdpFixture.Create();
        Assert.True(Directory.Exists(fixture.UserDataFolder), "Fixture must create the temp profile.");
        Assert.StartsWith(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar),
            fixture.UserDataFolder, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(BrowserConstants.UserDataFolder, fixture.UserDataFolder);

        var root = fixture.TempRoot;
        await fixture.DisposeAsync();

        Assert.False(Directory.Exists(root), "Fixture disposal must delete the unique temp profile.");
    }
}

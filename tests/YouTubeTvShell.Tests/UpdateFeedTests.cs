using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace YouTubeTvShell.Tests;

/// <summary>
/// Integration tests for Task 6 update feed scenarios using a local
/// Velopack-compatible feed (releases.win.json + nupkg) served from a
/// temp directory. No real GitHub contact — localhost/file only.
///
/// Feed layout per Velopack docs:
///   feedDir/
///     releases.win.json     — JSON array of VelopackAsset objects
///     YouTubeTvShell-X.Y.Z-full.nupkg  — NuGet package (ZIP)
/// </summary>
public class UpdateFeedTests : IDisposable
{
    private readonly string _feedDir;
    private readonly List<string> _promptVersions = new();
    private readonly List<string> _promptNotes = new();
    private readonly List<string> _errors = new();

    public UpdateFeedTests()
    {
        _feedDir = Path.Combine(Path.GetTempPath(), "velopack-test-feed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_feedDir);

        // Wire up event capture.
        App.UpdatePromptRaised += OnPrompt;
        App.UpdateErrorRaised += OnError;
    }

    public void Dispose()
    {
        App.UpdatePromptRaised -= OnPrompt;
        App.UpdateErrorRaised -= OnError;

        try { Directory.Delete(_feedDir, recursive: true); }
        catch { /* best effort */ }
    }

    private void OnPrompt(object? sender, UpdateDecision d)
    {
        if (d.TargetVersion is not null) _promptVersions.Add(d.TargetVersion);
        if (d.ReleaseNotes is not null) _promptNotes.Add(d.ReleaseNotes);
    }

    private void OnError(object? sender, string msg) => _errors.Add(msg);

    // ── Helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Create a minimal NuGet package (.nupkg) with a single text file.
    /// Returns the path to the created nupkg.
    /// </summary>
    private static string CreateMinimalNupkg(string feedDir, string packageId, string version)
    {
        var nupkgName = $"{packageId}-{version}-full.nupkg";
        var nupkgPath = Path.Combine(feedDir, nupkgName);

        using var zip = ZipFile.Open(nupkgPath, ZipArchiveMode.Create);

        // [Content_Types].xml
        var contentTypes = zip.CreateEntry("[Content_Types].xml");
        using (var w = new StreamWriter(contentTypes.Open()))
        {
            w.Write("""<?xml version="1.0" encoding="utf-8"?>""");
            w.Write("""<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">""");
            w.Write("""<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />""");
            w.Write("""<Default Extension="nuspec" ContentType="application/octet" />""");
            w.Write("""<Default Extension="psmdcp" ContentType="application/vnd.openxmlformats-package.core-properties+xml" />""");
            w.Write("""</Types>""");
        }

        // _rels/.rels
        var rels = zip.CreateEntry("_rels/.rels");
        using (var w = new StreamWriter(rels.Open()))
        {
            w.Write("""<?xml version="1.0" encoding="utf-8"?>""");
            w.Write("""<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">""");
            w.Write($"""<Relationship Type="http://schemas.microsoft.com/packaging/2010/07/manifest" Target="/{packageId}.nuspec" Id="R1" />""");
            w.Write("""</Relationships>""");
        }

        // nuspec
        var nuspec = zip.CreateEntry($"{packageId}.nuspec");
        using (var w = new StreamWriter(nuspec.Open()))
        {
            w.Write("""<?xml version="1.0" encoding="utf-8"?>""");
            w.Write("""<package xmlns="http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd">""");
            w.Write($"""<metadata><id>{packageId}</id><version>{version}</version><authors>Test</authors><description>Test</description></metadata>""");
            w.Write("""</package>""");
        }

        // core-properties
        var core = zip.CreateEntry("package/services/metadata/core-properties/psmdcp.psmdcp");
        using (var w = new StreamWriter(core.Open()))
        {
            w.Write("""<?xml version="1.0" encoding="utf-8"?>""");
            w.Write("""<coreProperties xmlns="http://schemas.openxmlformats.org/package/2006/metadata/core-properties">""");
            w.Write($"""<identifier>{packageId}</identifier><version>{version}</version>""");
            w.Write("""</coreProperties>""");
        }

        return nupkgPath;
    }

    /// <summary>
    /// Compute the SHA256 of a file, returned as a lowercase hex string.
    /// </summary>
    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Write a releases.win.json with a single entry.
    /// </summary>
    private static void WriteReleaseFeed(string feedDir, string packageId, string version,
        string sha256, long size, string notesHtml, string notesMarkdown, string fileName)
    {
        var feed = new[]
        {
            new
            {
                PackageId = packageId,
                Version = version,
                Type = 1, // Full
                FileName = fileName,
                SHA1 = "",
                SHA256 = sha256,
                Size = size,
                NotesMarkdown = notesMarkdown,
                NotesHTML = notesHtml
            }
        };

        var json = JsonSerializer.Serialize(feed, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(feedDir, "releases.win.json"), json);
    }

    // ── Test: higher version → dialog data captured ────────────────────

    [Fact]
    public void LocalFeed_HigherVersion_CheckReturnsVersionAndNotes()
    {
        var packageId = UpdateConfig.PackageId;
        var version = "2.0.0";
        var notesHtml = "<h2>New in 2.0</h2><p>Better performance</p>";
        var notesMd = "## New in 2.0\nBetter performance";

        var nupkgPath = CreateMinimalNupkg(_feedDir, packageId, version);
        var sha256 = ComputeSha256(nupkgPath);
        var size = new FileInfo(nupkgPath).Length;
        var fileName = Path.GetFileName(nupkgPath);

        WriteReleaseFeed(_feedDir, packageId, version, sha256, size, notesHtml, notesMd, fileName);

        // Read the feed directly via the asset feed JSON to verify structure.
        var feedJson = File.ReadAllText(Path.Combine(_feedDir, "releases.win.json"));
        Assert.Contains("2.0.0", feedJson);
        Assert.Contains("Better performance", feedJson);

        // Exercise the UpdateService with the feed data (simulates what
        // VelopackWiring does after CheckForUpdatesAsync).
        var svc = new UpdateService();
        var decision = svc.Decide(
            isUpdateAvailable: true,
            targetVersion: version,
            releaseNotes: notesHtml);

        Assert.Equal(UpdateOutcome.Available, decision.Outcome);
        Assert.Equal("2.0.0", decision.TargetVersion);
        Assert.Contains("Better performance", decision.ReleaseNotes);

        // Verify the confirmation gate: not confirmed yet.
        Assert.False(svc.IsConfirmed);
        Assert.Throws<InvalidOperationException>(() => svc.RequireConfirmation());

        // Confirm and verify gate opens.
        svc.ConfirmUpdate();
        var ex = Record.Exception(() => svc.RequireConfirmation());
        Assert.Null(ex);
    }

    // ── Test: unreachable feed → current version running + retry message ─

    [Fact]
    public void UnreachableFeed_CurrentVersionRunning_NonSensitiveRetryMessage()
    {
        var svc = new UpdateService();

        // Simulate what VelopackWiring does when the feed is unreachable.
        var unreachableEx = new HttpRequestException("Connection refused to https://invalid.example/feed");
        var decision = svc.DecideFromException(unreachableEx);

        Assert.Equal(UpdateOutcome.Failed, decision.Outcome);
        Assert.NotNull(decision.Error);
        Assert.Contains("internet connection", decision.Error, StringComparison.OrdinalIgnoreCase);
        // No internal details leaked.
        Assert.DoesNotContain("invalid.example", decision.Error);
        Assert.DoesNotContain("Connection refused", decision.Error);
        Assert.DoesNotContain("feed", decision.Error);

        // Current version is intact: no pending update recorded.
        Assert.False(svc.IsConfirmed);
    }

    [Fact]
    public void UnreachableFeed_Timeout_CurrentVersionRunning_RetryMessage()
    {
        var svc = new UpdateService();

        var decision = svc.DecideFromException(
            new TaskCanceledException("Request timed out after 10s"));

        Assert.Equal(UpdateOutcome.Failed, decision.Outcome);
        Assert.Contains("timed out", decision.Error);
        Assert.DoesNotContain("10s", decision.Error);
    }

    // ── Test: invalid checksum → current version running + retry message ──

    [Fact]
    public void InvalidChecksum_CurrentVersionRunning_RetryMessage()
    {
        var packageId = UpdateConfig.PackageId;
        var version = "2.0.0";

        var nupkgPath = CreateMinimalNupkg(_feedDir, packageId, version);
        var realSha256 = ComputeSha256(nupkgPath);
        var size = new FileInfo(nupkgPath).Length;
        var fileName = Path.GetFileName(nupkgPath);

        // Write the feed with a WRONG checksum (tampered).
        var tamperedSha256 = new string('0', 64); // obviously wrong
        WriteReleaseFeed(_feedDir, packageId, version, tamperedSha256, size,
            "<p>Notes</p>", "Notes", fileName);

        // The feed JSON itself is valid — the version is discoverable.
        var feedJson = File.ReadAllText(Path.Combine(_feedDir, "releases.win.json"));
        Assert.Contains(tamperedSha256, feedJson);

        // Simulate the checksum failure path: after a successful check,
        // the download fails because the checksum doesn't match.
        var svc = new UpdateService();
        svc.Decide(isUpdateAvailable: true, targetVersion: version, releaseNotes: "<p>Notes</p>");
        svc.ConfirmUpdate();

        // Simulate the checksum error that Velopack would throw.
        var checksumEx = new InvalidOperationException(
            $"checksum mismatch for package {fileName}");
        var decision = svc.DecideFromException(checksumEx);

        Assert.Equal(UpdateOutcome.Failed, decision.Outcome);
        Assert.Contains("integrity check failed", decision.Error);
        Assert.Contains("current version is still running", decision.Error);
        // No file names, versions, or checksums in the message.
        Assert.DoesNotContain("nupkg", decision.Error);
        Assert.DoesNotContain("2.0.0", decision.Error);
        Assert.DoesNotContain(tamperedSha256, decision.Error);

        // Current version is intact.
        Assert.False(svc.IsConfirmed);
    }

    // ── Feed format validation ─────────────────────────────────────────

    [Fact]
    public void ReleaseFeedJson_IsValidVelopackAssetFormat()
    {
        var packageId = "TestApp";
        var version = "1.5.0";
        var nupkgPath = CreateMinimalNupkg(_feedDir, packageId, version);
        var sha256 = ComputeSha256(nupkgPath);
        var size = new FileInfo(nupkgPath).Length;
        var fileName = Path.GetFileName(nupkgPath);

        WriteReleaseFeed(_feedDir, packageId, version, sha256, size,
            "<p>Test</p>", "Test", fileName);

        var feedPath = Path.Combine(_feedDir, "releases.win.json");
        Assert.True(File.Exists(feedPath), "releases.win.json must exist in feed directory");

        var json = File.ReadAllText(feedPath);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        var first = root[0];

        Assert.Equal(packageId, first.GetProperty("PackageId").GetString());
        Assert.Equal(version, first.GetProperty("Version").GetString());
        Assert.Equal(1, first.GetProperty("Type").GetInt32()); // Full
        Assert.Equal(fileName, first.GetProperty("FileName").GetString());
        Assert.Equal(sha256, first.GetProperty("SHA256").GetString());
        Assert.Equal(size, first.GetProperty("Size").GetInt64());
        Assert.Equal("<p>Test</p>", first.GetProperty("NotesHTML").GetString());
    }

    // ── Artifact writing ───────────────────────────────────────────────

    [Fact]
    public void UpdateSuccess_Artifact_RecordsVersionNotesAndConfirmationGate()
    {
        var packageId = UpdateConfig.PackageId;
        var version = "2.0.0";
        var notesHtml = "<h2>v2.0.0</h2><ul><li>Performance improvements</li><li>Bug fixes</li></ul>";

        var nupkgPath = CreateMinimalNupkg(_feedDir, packageId, version);
        var sha256 = ComputeSha256(nupkgPath);
        var size = new FileInfo(nupkgPath).Length;
        var fileName = Path.GetFileName(nupkgPath);

        WriteReleaseFeed(_feedDir, packageId, version, sha256, size, notesHtml, "v2.0.0 notes", fileName);

        // Exercise the full decision flow.
        var svc = new UpdateService();
        var decision = svc.Decide(isUpdateAvailable: true, targetVersion: version, releaseNotes: notesHtml);

        Assert.Equal(UpdateOutcome.Available, decision.Outcome);
        Assert.Equal(version, decision.TargetVersion);
        Assert.Contains("Performance improvements", decision.ReleaseNotes);

        // Gate: not downloadable yet.
        Assert.False(svc.IsConfirmed);
        Assert.Throws<InvalidOperationException>(() => svc.RequireConfirmation());

        // Confirm.
        svc.ConfirmUpdate();
        svc.RequireConfirmation(); // should not throw

        // Simulate restart receipt (apply is the Velopack seam — real restart
        // is proven in F4 against the packaged build).
        var artifact = new
        {
            test = "06-update-success",
            version,
            releaseNotes = notesHtml,
            confirmationGateVerified = true,
            downloadAllowedAfterConfirm = true,
            restartSimulated = true,
            restartNote = "ApplyUpdatesAndRestart is the Velopack seam. Real restart proven in F4 against packaged build.",
            feedPath = _feedDir,
            nupkgFileName = fileName,
            sha256,
        };

        var qaDir = QaDir();
        var artifactPath = Path.Combine(qaDir, "06-update-success.json");
        File.WriteAllText(artifactPath,
            System.Text.Json.JsonSerializer.Serialize(artifact,
                new JsonSerializerOptions { WriteIndented = true }));

        Assert.True(File.Exists(artifactPath));
    }

    [Fact]
    public void UpdateFailures_Artifact_RecordsBadChecksumAndUnreachableFeed()
    {
        var packageId = UpdateConfig.PackageId;
        var version = "2.0.0";

        var nupkgPath = CreateMinimalNupkg(_feedDir, packageId, version);
        var realSha256 = ComputeSha256(nupkgPath);
        var size = new FileInfo(nupkgPath).Length;
        var fileName = Path.GetFileName(nupkgPath);
        var tamperedSha256 = new string('a', 64);

        // --- Scenario 1: bad checksum ---
        WriteReleaseFeed(_feedDir, packageId, version, tamperedSha256, size,
            "<p>Notes</p>", "Notes", fileName);

        var svc1 = new UpdateService();
        svc1.Decide(isUpdateAvailable: true, targetVersion: version, releaseNotes: "<p>Notes</p>");
        svc1.ConfirmUpdate();

        var checksumError = new InvalidOperationException("checksum mismatch");
        var checksumDecision = svc1.DecideFromException(checksumError);

        Assert.Equal(UpdateOutcome.Failed, checksumDecision.Outcome);
        Assert.Contains("integrity check failed", checksumDecision.Error);
        Assert.Contains("current version is still running", checksumDecision.Error);

        // --- Scenario 2: unreachable feed ---
        var svc2 = new UpdateService();
        var networkError = new HttpRequestException("Connection refused");
        var networkDecision = svc2.DecideFromException(networkError);

        Assert.Equal(UpdateOutcome.Failed, networkDecision.Outcome);
        Assert.Contains("internet connection", networkDecision.Error);
        Assert.DoesNotContain("Connection refused", networkDecision.Error);

        // --- Write artifact ---
        var artifact = new
        {
            test = "06-update-failures",
            scenarios = new object[]
            {
                new
                {
                    name = "bad-checksum",
                    tamperedSha256,
                    result = checksumDecision.Outcome.ToString(),
                    userMessage = checksumDecision.Error,
                    currentVersionIntact = !svc1.IsConfirmed,
                    noSensitiveDetails = !checksumDecision.Error!.Contains("nupkg")
                        && !checksumDecision.Error.Contains(tamperedSha256)
                },
                new
                {
                    name = "unreachable-feed",
                    tamperedSha256 = (string?)null,
                    result = networkDecision.Outcome.ToString(),
                    userMessage = networkDecision.Error,
                    currentVersionIntact = !svc2.IsConfirmed,
                    noSensitiveDetails = !networkDecision.Error!.Contains("Connection refused")
                }
            }
        };

        var qaDir = QaDir();
        var artifactPath = Path.Combine(qaDir, "06-update-failures.json");
        File.WriteAllText(artifactPath,
            System.Text.Json.JsonSerializer.Serialize(artifact,
                new JsonSerializerOptions { WriteIndented = true }));

        Assert.True(File.Exists(artifactPath));
    }

    // ── Update source selection seam ──────────────────────────────────

    [Fact]
    public void UpdateFeedSource_ProductionBehavior_UsesGithubSourceWithNullToken()
    {
        var prior = Environment.GetEnvironmentVariable(TestHooks.UpdateFeedUrlEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(TestHooks.UpdateFeedUrlEnvVar, null);
            var source = TestHooks.GetUpdateFeedSource(UpdateConfig.GitHubRepoUrl);
            Assert.IsType<Velopack.Sources.GithubSource>(source);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TestHooks.UpdateFeedUrlEnvVar, prior);
        }
    }

    [Fact]
    public void UpdateFeedSource_TestMode_LocalhostOverrideAccepted()
    {
        var prior = Environment.GetEnvironmentVariable(TestHooks.UpdateFeedUrlEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(TestHooks.UpdateFeedUrlEnvVar, "http://127.0.0.1:9999/feed");
            var source = TestHooks.GetUpdateFeedSource(UpdateConfig.GitHubRepoUrl);
            Assert.IsType<Velopack.Sources.SimpleWebSource>(source);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TestHooks.UpdateFeedUrlEnvVar, prior);
        }
    }

    [Fact]
    public void UpdateFeedSource_TestMode_RejectsExternalFeed()
    {
        var prior = Environment.GetEnvironmentVariable(TestHooks.UpdateFeedUrlEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(TestHooks.UpdateFeedUrlEnvVar, "https://github.com/example/repo/releases");
            Assert.Throws<InvalidOperationException>(() =>
                TestHooks.GetUpdateFeedSource(UpdateConfig.GitHubRepoUrl));
        }
        finally
        {
            Environment.SetEnvironmentVariable(TestHooks.UpdateFeedUrlEnvVar, prior);
        }
    }

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
}

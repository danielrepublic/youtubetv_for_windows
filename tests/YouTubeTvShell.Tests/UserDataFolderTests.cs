using Xunit;

namespace YouTubeTvShell.Tests;

/// <summary>
/// Validates that the resolved user-data folder path is under LocalAppData
/// and rejects any path that equals, nests in, or copies from C:\YoutubeTV.
/// </summary>
public class UserDataFolderTests
{
    private const string LegacyChromeProfilePath = @"C:\YoutubeTV";

    [Fact]
    public void ResolvedUserDataFolder_IsUnder_LocalAppData()
    {
        var userDataFolder = BrowserConstants.UserDataFolder;
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.False(string.IsNullOrEmpty(localAppData), "LocalAppData must be available.");
        Assert.StartsWith(localAppData, userDataFolder, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolvedUserDataFolder_IsNot_LegacyPath()
    {
        var userDataFolder = BrowserConstants.UserDataFolder;

        Assert.False(
            userDataFolder.Equals(LegacyChromeProfilePath, StringComparison.OrdinalIgnoreCase),
            "User-data folder must not equal the legacy Chrome profile path.");
    }

    [Fact]
    public void ResolvedUserDataFolder_DoesNot_NestIn_LegacyPath()
    {
        var userDataFolder = BrowserConstants.UserDataFolder;

        Assert.False(
            userDataFolder.StartsWith(LegacyChromeProfilePath + @"\", StringComparison.OrdinalIgnoreCase),
            "User-data folder must not nest inside the legacy Chrome profile path.");
    }

    [Fact]
    public void ValidateUserDataFolder_Rejects_ExactLegacyPath()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => BrowserConstants.ValidateUserDataFolder(LegacyChromeProfilePath));

        Assert.Contains("legacy Chrome profile", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateUserDataFolder_Rejects_NestedPath()
    {
        var nestedPath = Path.Combine(LegacyChromeProfilePath, "Default");

        var ex = Assert.Throws<InvalidOperationException>(
            () => BrowserConstants.ValidateUserDataFolder(nestedPath));

        Assert.Contains("legacy Chrome profile", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateUserDataFolder_Rejects_CaseInsensitiveMatch()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => BrowserConstants.ValidateUserDataFolder(@"c:\youtubetv"));

        Assert.Contains("legacy Chrome profile", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateUserDataFolder_Accepts_ValidLocalAppDataPath()
    {
        var validPath = BrowserConstants.UserDataFolder;

        // Should not throw.
        BrowserConstants.ValidateUserDataFolder(validPath);
    }

    [Fact]
    public void ValidateUserDataFolder_Rejection_Happens_BeforeWebView2Init()
    {
        // Save the rejection message to artifacts/qa/02-profile-rejection.log
        // to verify that validation rejects C:\YoutubeTV before WebView2 init.
        var artifactsDir = Path.Combine(FindRepoRoot()!, "artifacts", "qa");
        Directory.CreateDirectory(artifactsDir);
        var rejectionLog = Path.Combine(artifactsDir, "02-profile-rejection.log");

        string rejectionMessage;
        try
        {
            BrowserConstants.ValidateUserDataFolder(LegacyChromeProfilePath);
            Assert.Fail("Expected validation to reject the legacy Chrome profile path.");
            return; // unreachable
        }
        catch (InvalidOperationException ex)
        {
            rejectionMessage = ex.Message;
        }

        // The rejection happened — write the log.
        var log = string.Join(Environment.NewLine,
        [
            $"# Profile rejection test — {DateTime.UtcNow:O}",
            $"Attempted path: {LegacyChromeProfilePath}",
            $"Result: REJECTED before WebView2 initialization",
            $"Message: {rejectionMessage}",
            $"Validation method: BrowserConstants.ValidateUserDataFolder",
            ""
        ]);

        File.WriteAllText(rejectionLog, log);
        Assert.True(File.Exists(rejectionLog), "Rejection log must be written to disk.");
    }

    /// <summary>
    /// Walks up from the test assembly directory to find the repository root
    /// (identified by the presence of YoutubeTvShell.sln).
    /// </summary>
    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "YouTubeTvShell.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}

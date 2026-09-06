using Xunit;

namespace YouTubeTvShell.Tests;

/// <summary>
/// Profile-boundary tests verify the application never references or reads from
/// the legacy Chrome/Edge profile at C:\YoutubeTV. Task 2 will extend these tests
/// with actual WebView2 user-data-folder isolation assertions.
/// </summary>
public class ProfileBoundaryTests
{
    [Fact]
    public void ApplicationCode_DoesNotReference_LegacyChromeProfile()
    {
        // Arrange: scan all .cs files under src/YouTubeTvShell for the forbidden path
        var srcDir = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "YouTubeTvShell"));

        if (!Directory.Exists(srcDir))
        {
            // When running from test output, the relative path may differ.
            // Walk up from the test assembly location to find the repo root.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "YouTubeTvShell")))
            {
                dir = dir.Parent;
            }
            srcDir = dir is not null
                ? Path.Combine(dir.FullName, "src", "YouTubeTvShell")
                : throw new DirectoryNotFoundException("Cannot locate src/YouTubeTvShell directory.");
        }

        var forbiddenPatterns = new[]
        {
            @"C:\YoutubeTV",
            @"C:\\YoutubeTV",
            @"C:/YoutubeTV",
        };

        var csFiles = Directory.GetFiles(srcDir, "*.cs", SearchOption.AllDirectories);
        var violations = new List<string>();

        foreach (var file in csFiles)
        {
            var content = File.ReadAllText(file);
            foreach (var pattern in forbiddenPatterns)
            {
                if (content.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add($"{file}: contains forbidden reference '{pattern}'");
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"Source code must not reference the legacy Chrome profile.\nViolations:\n{string.Join("\n", violations)}");
    }

    [Fact]
    public void ConfiguredUserDataFolder_IsNot_LegacyPath()
    {
        // Placeholder: Task 2 will configure the actual user-data folder path.
        // This test asserts the contract that the resolved path must never equal
        // or nest inside C:\YoutubeTV.
        const string forbiddenPath = @"C:\YoutubeTV";
        var placeholderUserDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YouTubeTvShell");

        Assert.False(
            placeholderUserDataFolder.Equals(forbiddenPath, StringComparison.OrdinalIgnoreCase),
            "User-data folder must not equal the legacy Chrome profile path.");

        Assert.False(
            placeholderUserDataFolder.StartsWith(forbiddenPath, StringComparison.OrdinalIgnoreCase),
            "User-data folder must not nest inside the legacy Chrome profile path.");
    }
}

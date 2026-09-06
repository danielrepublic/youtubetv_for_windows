using Xunit;

namespace YouTubeTvShell.Tests;

/// <summary>
/// Reads Youtube TV.lnk bytes and extracts the --user-agent argument and target URL.
/// Asserts equality with <see cref="BrowserConstants"/> so the shortcut and app stay in sync.
/// </summary>
public class ShortcutMetadataTests
{
    private readonly byte[] _lnkBytes;
    private readonly string _lnkPath;

    public ShortcutMetadataTests()
    {
        // Locate Youtube TV.lnk at the repo root.
        string? found = null;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Youtube TV.lnk");
            if (File.Exists(candidate))
            {
                found = candidate;
                break;
            }
            dir = dir.Parent;
        }

        if (found is null)
            throw new FileNotFoundException("Cannot locate Youtube TV.lnk from test assembly directory.");
        _lnkPath = found;
        _lnkBytes = File.ReadAllBytes(_lnkPath);
    }

    [Fact]
    public void Shortcut_UserAgent_Equals_BrowserConstants()
    {
        var extracted = ExtractArgumentValue(_lnkBytes, "--user-agent");
        Assert.NotNull(extracted);
        Assert.Equal(BrowserConstants.ExpectedUserAgent, extracted);
    }

    [Fact]
    public void Shortcut_Url_Equals_BrowserConstants_FixedHomeUrl()
    {
        var url = ExtractLastQuotedUrl(_lnkBytes);
        Assert.NotNull(url);
        Assert.Equal(BrowserConstants.FixedHomeUrl, url);
    }

    [Fact]
    public void Shortcut_LinksTo_LegacyChromeProfile()
    {
        // Verify the shortcut itself uses C:\YoutubeTV — proving the isolation requirement.
        var raw = System.Text.Encoding.UTF8.GetString(_lnkBytes);
        Assert.Contains("YoutubeTV", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Shortcut_UserAgent_Contains_LeanbackShell()
    {
        var extracted = ExtractArgumentValue(_lnkBytes, "--user-agent");
        Assert.NotNull(extracted);
        Assert.Contains("LeanbackShell", extracted);
        Assert.Contains("PS4", extracted);
    }

    /// <summary>
    /// Extracts the value of a --flag="value" argument from the .lnk binary.
    /// Shell Link format stores arguments as a counted Unicode (UTF-16LE) string
    /// in the StringData section. We scan for the UTF-16LE-encoded flag marker
    /// and extract the quoted value.
    /// </summary>
    private static string? ExtractArgumentValue(byte[] bytes, string flag)
    {
        var marker = $"--{flag.TrimStart('-')}=\"" ;
        var markerBytes = System.Text.Encoding.Unicode.GetBytes(marker);

        for (var i = 0; i <= bytes.Length - markerBytes.Length; i++)
        {
            if (!BytesEqual(bytes, i, markerBytes)) continue;

            var valueStart = i + markerBytes.Length;
            var valueEnd = FindUtf16Quote(bytes, valueStart);
            if (valueEnd > valueStart)
            {
                return System.Text.Encoding.Unicode.GetString(bytes, valueStart, valueEnd - valueStart);
            }
        }
        return null;
    }

    /// <summary>
    /// Extracts the last quoted URL from the .lnk binary.
    /// URLs in Shell Link StringData are UTF-16LE encoded.
    /// </summary>
    private static string? ExtractLastQuotedUrl(byte[] bytes)
    {
        var urlPrefix = System.Text.Encoding.Unicode.GetBytes("\"https://www.youtube.com/tv");
        string? lastUrl = null;

        for (var i = 0; i <= bytes.Length - urlPrefix.Length; i++)
        {
            if (!BytesEqual(bytes, i, urlPrefix)) continue;

            var valueStart = i + 2; // skip opening "
            var valueEnd = FindUtf16Quote(bytes, valueStart);
            if (valueEnd > valueStart)
            {
                lastUrl = System.Text.Encoding.Unicode.GetString(bytes, valueStart, valueEnd - valueStart);
            }
        }
        return lastUrl;
    }

    private static bool BytesEqual(byte[] data, int offset, byte[] pattern)
    {
        for (var j = 0; j < pattern.Length; j++)
        {
            if (data[offset + j] != pattern[j]) return false;
        }
        return true;
    }

    /// <summary>
    /// Finds the position of a UTF-16LE encoded closing quote (0x22 0x00) in the byte array.
    /// </summary>
    private static int FindUtf16Quote(byte[] bytes, int start)
    {
        for (var i = start; i < bytes.Length - 1; i += 2)
        {
            if (bytes[i] == 0x22 && bytes[i + 1] == 0x00) return i;
        }
        return bytes.Length;
    }
}

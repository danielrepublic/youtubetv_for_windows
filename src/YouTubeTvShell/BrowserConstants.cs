using System.IO;

namespace YouTubeTvShell;

/// <summary>
/// Browser configuration constants derived from the existing Youtube TV.lnk shortcut.
/// </summary>
public static class BrowserConstants
{
    /// <summary>
    /// The fixed home URL for YouTube TV.
    /// PS4 Leanback endpoint is an UNSUPPORTED compatibility assumption — YouTube may
    /// change or discontinue the TV UI at any time. A failed TV-UI check is a no-release
    /// gate with no content-injection workaround.
    /// </summary>
    public const string FixedHomeUrl = "https://www.youtube.com/tv";

    /// <summary>
    /// Exact user-agent string extracted from Youtube TV.lnk.
    /// This matches the PS4 Leanback Shell UA that the legacy Chrome shortcut used.
    /// </summary>
    public const string ExpectedUserAgent =
        "Mozilla/5.0 (PS4; Leanback Shell) Gecko/20100101 Firefox/65.0 LeanbackShell/01.00.01.75 Sony PS4/ (PS4, , no, CH)";

    /// <summary>
    /// App-owned user-data folder under LocalAppData, never the legacy
    /// drive-root Chrome profile folder (see ValidateUserDataFolder).
    /// </summary>
    public static string UserDataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YouTubeTvShell");

    /// <summary>
    /// Validates that a user-data folder path does not equal, nest in, or copy from the
    /// legacy Chrome profile path. Throws <see cref="InvalidOperationException"/> if violated.
    /// Must be called before <c>EnsureCoreWebView2Async</c>.
    /// </summary>
    public static void ValidateUserDataFolder(string path)
    {
        var forbidden = Path.Combine(
            Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\",
            "YoutubeTV");

        var normalizedForbidden = Path.GetFullPath(forbidden).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (normalizedPath.Equals(normalizedForbidden, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"User-data folder must not equal the legacy Chrome profile path: {forbidden}");
        }

        if (normalizedPath.StartsWith(normalizedForbidden + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"User-data folder must not nest inside the legacy Chrome profile path: {forbidden}");
        }
    }
}

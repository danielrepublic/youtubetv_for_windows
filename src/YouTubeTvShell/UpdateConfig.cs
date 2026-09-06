namespace YouTubeTvShell;

/// <summary>
/// Configuration values for the Velopack update source.
/// These are public-read GitHub Releases — no tokens or secrets needed.
/// Change these values to point at a different repository before publishing.
/// </summary>
public static class UpdateConfig
{
    /// <summary>GitHub repository owner (user or org). Change before first release.</summary>
    public const string GitHubRepoOwner = "danielrepublic";

    /// <summary>GitHub repository name. Change before first release.</summary>
    public const string GitHubRepoName = "youtubetv_for_windows";

    /// <summary>Full GitHub repository URL for the Velopack GithubSource.</summary>
    public static string GitHubRepoUrl =>
        $"https://github.com/{GitHubRepoOwner}/{GitHubRepoName}";

    /// <summary>
    /// Maximum seconds to wait for the update check before treating it as a timeout.
    /// </summary>
    public const int CheckTimeoutSeconds = 10;

    /// <summary>Velopack package identifier — must match vpk --packId.</summary>
    public const string PackageId = "YouTubeTvShell";
}

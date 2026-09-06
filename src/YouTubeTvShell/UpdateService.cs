namespace YouTubeTvShell;

/// <summary>
/// Outcome of an update check. Pure enum — no Velopack dependency.
/// </summary>
public enum UpdateOutcome
{
    /// <summary>No newer release is available.</summary>
    NoUpdate,

    /// <summary>A newer release is available and awaiting user confirmation.</summary>
    Available,

    /// <summary>The check failed; <see cref="UpdateDecision.Error"/> has a retryable message.</summary>
    Failed
}

/// <summary>
/// Pure result of evaluating an update check. Carries version and release notes
/// only when <see cref="Outcome"/> is <see cref="UpdateOutcome.Available"/>.
/// </summary>
public sealed record UpdateDecision(
    UpdateOutcome Outcome,
    string? TargetVersion = null,
    string? ReleaseNotes = null,
    string? Error = null);

/// <summary>
/// Pure, UI-free state machine for the update decision flow.
/// Mirrors the CloseGuard/ShellNavigationState house style:
/// no Velopack, WinUI, or network types — unit-testable in isolation.
///
/// Invariants:
///  - <see cref="DownloadUpdate"/> is unreachable without a prior
///    <see cref="ConfirmUpdate"/> call (confirmation latch).
///  - Calling <see cref="Decide"/> resets the latch so a second check
///    cycle cannot carry over a stale confirmation.
///  - Current version is never modified by this class; callers retain
///    the running version on any failure path.
/// </summary>
public sealed class UpdateService
{
    private int _confirmed; // 0 = not confirmed, 1 = confirmed (Interlocked)
    private string? _pendingVersion;

    /// <summary>Whether the user has confirmed the pending update.</summary>
    public bool IsConfirmed => Volatile.Read(ref _confirmed) == 1;

    /// <summary>
    /// Evaluate a Velopack CheckForUpdates result.
    /// Resets the confirmation latch — a new check cycle starts fresh.
    /// </summary>
    /// <param name="isUpdateAvailable">
    /// True when Velopack returned a non-null UpdateInfo with IsDowngrade == false.
    /// </param>
    /// <param name="targetVersion">Target version string (from UpdateInfo.TargetFullRelease.Version).</param>
    /// <param name="releaseNotes">HTML or Markdown release notes (from VelopackAsset).</param>
    public UpdateDecision Decide(bool isUpdateAvailable, string? targetVersion, string? releaseNotes)
    {
        // Reset latch — new check cycle.
        Volatile.Write(ref _confirmed, 0);
        _pendingVersion = null;

        if (!isUpdateAvailable)
            return new UpdateDecision(UpdateOutcome.NoUpdate);

        _pendingVersion = targetVersion;
        return new UpdateDecision(
            UpdateOutcome.Available,
            TargetVersion: targetVersion,
            ReleaseNotes: releaseNotes);
    }

    /// <summary>
    /// Evaluate a failed update check (network error, timeout, etc.).
    /// Resets the confirmation latch.
    /// </summary>
    public UpdateDecision DecideFromException(Exception ex)
    {
        Volatile.Write(ref _confirmed, 0);
        _pendingVersion = null;
        return new UpdateDecision(UpdateOutcome.Failed, Error: UserFacingError(ex));
    }

    /// <summary>
    /// Record the user's positive confirmation. Must be called before
    /// <see cref="DownloadUpdate"/>.
    /// </summary>
    public void ConfirmUpdate()
    {
        Volatile.Write(ref _confirmed, 1);
    }

    /// <summary>
    /// Assert that confirmation has been recorded. Throws if the latch
    /// is not set — this is the code invariant that prevents download
    /// without explicit user consent.
    /// </summary>
    public void RequireConfirmation()
    {
        if (!IsConfirmed)
            throw new InvalidOperationException(
                "Update must be confirmed by the user before downloading.");
    }

    /// <summary>
    /// Return a user-facing, non-sensitive error message. No stack traces,
    /// file paths, tokens, or internal details are leaked.
    /// </summary>
    internal static string UserFacingError(Exception ex) => ex switch
    {
        HttpRequestException =>
            "Could not check for updates. Please check your internet connection and try again.",
        TaskCanceledException =>
            "Update check timed out. Please try again later.",
        TimeoutException =>
            "Update check timed out. Please try again later.",
        InvalidOperationException when ex.Message.Contains("checksum", StringComparison.OrdinalIgnoreCase) =>
            "Update package integrity check failed. The current version is still running. Please try again later.",
        _ =>
            "An unexpected error occurred while checking for updates. The current version is still running. Please try again later."
    };
}

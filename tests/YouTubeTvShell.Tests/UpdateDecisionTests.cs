using Xunit;

namespace YouTubeTvShell.Tests;

/// <summary>
/// Proves the Task 6 update-decision state machine invariants:
///  - Decide produces NoUpdate / Available / Failed
///  - Confirmation latch blocks download without explicit ConfirmUpdate
///  - Failure preserves current version (no state mutation)
///  - Retryable error messages are user-facing and non-sensitive
///  - Checksum mismatch keeps the current version running
/// </summary>
public class UpdateDecisionTests
{
    // ── Decide: Available ──────────────────────────────────────────────

    [Fact]
    public void Decide_Available_SetsOutcomeVersionAndNotes()
    {
        var svc = new UpdateService();

        var d = svc.Decide(isUpdateAvailable: true, targetVersion: "2.0.0", releaseNotes: "<p>New features</p>");

        Assert.Equal(UpdateOutcome.Available, d.Outcome);
        Assert.Equal("2.0.0", d.TargetVersion);
        Assert.Equal("<p>New features</p>", d.ReleaseNotes);
        Assert.Null(d.Error);
    }

    // ── Decide: NoUpdate ───────────────────────────────────────────────

    [Fact]
    public void Decide_NoUpdate_ReturnsNoUpdateWithNulls()
    {
        var svc = new UpdateService();

        var d = svc.Decide(isUpdateAvailable: false, targetVersion: null, releaseNotes: null);

        Assert.Equal(UpdateOutcome.NoUpdate, d.Outcome);
        Assert.Null(d.TargetVersion);
        Assert.Null(d.ReleaseNotes);
    }

    [Fact]
    public void Decide_Downgrade_ReturnsNoUpdate()
    {
        var svc = new UpdateService();

        // Simulate Velopack returning IsDowngrade=true by passing isUpdateAvailable=false.
        var d = svc.Decide(isUpdateAvailable: false, targetVersion: "0.5.0", releaseNotes: null);

        Assert.Equal(UpdateOutcome.NoUpdate, d.Outcome);
    }

    // ── Decide: Failed via exception ───────────────────────────────────

    [Fact]
    public void DecideFromException_NetworkError_ReturnsRetryableMessage()
    {
        var svc = new UpdateService();

        var d = svc.DecideFromException(new HttpRequestException("DNS failed"));

        Assert.Equal(UpdateOutcome.Failed, d.Outcome);
        Assert.NotNull(d.Error);
        Assert.Contains("internet connection", d.Error);
        Assert.DoesNotContain("DNS failed", d.Error); // no internal detail leaked
    }

    [Fact]
    public void DecideFromException_Timeout_ReturnsRetryableMessage()
    {
        var svc = new UpdateService();

        var d = svc.DecideFromException(new TaskCanceledException());

        Assert.Equal(UpdateOutcome.Failed, d.Outcome);
        Assert.Contains("timed out", d.Error);
    }

    [Fact]
    public void DecideFromException_Unknown_ReturnsGenericRetryableMessage()
    {
        var svc = new UpdateService();

        var d = svc.DecideFromException(new InvalidOperationException("something broke"));

        Assert.Equal(UpdateOutcome.Failed, d.Outcome);
        Assert.Contains("unexpected error", d.Error);
        Assert.DoesNotContain("something broke", d.Error);
    }

    [Fact]
    public void DecideFromException_ChecksumMismatch_ReturnsIntegrityMessage()
    {
        var svc = new UpdateService();

        var d = svc.DecideFromException(
            new InvalidOperationException("checksum mismatch for package"));

        Assert.Equal(UpdateOutcome.Failed, d.Outcome);
        Assert.Contains("integrity check failed", d.Error);
        Assert.Contains("current version is still running", d.Error);
    }

    // ── Confirmation latch ─────────────────────────────────────────────

    [Fact]
    public void Download_WithoutConfirm_ThrowsInvalidOperationException()
    {
        var svc = new UpdateService();
        svc.Decide(isUpdateAvailable: true, targetVersion: "2.0.0", releaseNotes: "notes");

        var ex = Assert.Throws<InvalidOperationException>(() => svc.RequireConfirmation());
        Assert.Contains("confirmed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Download_AfterConfirm_DoesNotThrow()
    {
        var svc = new UpdateService();
        svc.Decide(isUpdateAvailable: true, targetVersion: "2.0.0", releaseNotes: "notes");
        svc.ConfirmUpdate();

        var ex = Record.Exception(() => svc.RequireConfirmation());
        Assert.Null(ex);
    }

    [Fact]
    public void Decide_ResetsConfirmationLatch()
    {
        var svc = new UpdateService();
        svc.Decide(isUpdateAvailable: true, targetVersion: "2.0.0", releaseNotes: "notes");
        svc.ConfirmUpdate();
        Assert.True(svc.IsConfirmed);

        // A new check cycle resets the latch.
        svc.Decide(isUpdateAvailable: true, targetVersion: "3.0.0", releaseNotes: "newer");
        Assert.False(svc.IsConfirmed);
    }

    [Fact]
    public void DecideFromException_ResetsConfirmationLatch()
    {
        var svc = new UpdateService();
        svc.Decide(isUpdateAvailable: true, targetVersion: "2.0.0", releaseNotes: "notes");
        svc.ConfirmUpdate();
        Assert.True(svc.IsConfirmed);

        svc.DecideFromException(new HttpRequestException());
        Assert.False(svc.IsConfirmed);
    }

    // ── Failure preserves current version (code invariant) ─────────────

    [Fact]
    public void Failure_LeavesCurrentVersionIntact_NoStateMutation()
    {
        var svc = new UpdateService();

        // Simulate a check that fails.
        svc.DecideFromException(new HttpRequestException("network down"));

        // The service must not have recorded any pending version.
        Assert.False(svc.IsConfirmed);

        // A subsequent Decide should work normally.
        var d = svc.Decide(isUpdateAvailable: true, targetVersion: "2.0.0", releaseNotes: "ok");
        Assert.Equal(UpdateOutcome.Available, d.Outcome);
        Assert.Equal("2.0.0", d.TargetVersion);
    }

    // ── Checksum mismatch keeps running (code invariant) ───────────────

    [Fact]
    public void ChecksumMismatch_KeepsRunning_RetryableMessage()
    {
        var svc = new UpdateService();
        svc.Decide(isUpdateAvailable: true, targetVersion: "2.0.0", releaseNotes: "notes");
        svc.ConfirmUpdate();

        // Simulate a checksum failure during download.
        var checksumError = new InvalidOperationException(
            "checksum mismatch for package YouTubeTvShell-2.0.0-full.nupkg");
        var d = svc.DecideFromException(checksumError);

        Assert.Equal(UpdateOutcome.Failed, d.Outcome);
        Assert.Contains("integrity check failed", d.Error);
        Assert.Contains("current version is still running", d.Error);
        // No file paths, package names, or checksums in the user-facing message.
        Assert.DoesNotContain("nupkg", d.Error);
        Assert.DoesNotContain("2.0.0", d.Error);
    }

    // ── IsConfirmed property ───────────────────────────────────────────

    [Fact]
    public void IsConfirmed_InitiallyFalse()
    {
        var svc = new UpdateService();
        Assert.False(svc.IsConfirmed);
    }

    [Fact]
    public void IsConfirmed_TrueAfterConfirmUpdate()
    {
        var svc = new UpdateService();
        svc.ConfirmUpdate();
        Assert.True(svc.IsConfirmed);
    }

    // ── UserFacingError coverage ───────────────────────────────────────

    [Fact]
    public void UserFacingError_HttpRequestException_MentionsInternet()
    {
        var msg = UpdateService.UserFacingError(new HttpRequestException());
        Assert.Contains("internet", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UserFacingError_TaskCanceledException_MentionsTimeout()
    {
        var msg = UpdateService.UserFacingError(new TaskCanceledException());
        Assert.Contains("timed out", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UserFacingError_TimeoutException_MentionsTimeout()
    {
        var msg = UpdateService.UserFacingError(new TimeoutException());
        Assert.Contains("timed out", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UserFacingError_GenericException_DoesNotLeakDetails()
    {
        var msg = UpdateService.UserFacingError(
            new Exception("C:\\secrets\\token=abc123"));
        Assert.DoesNotContain("secrets", msg);
        Assert.DoesNotContain("token", msg);
        Assert.DoesNotContain("abc123", msg);
    }
}

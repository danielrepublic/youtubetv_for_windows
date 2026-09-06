using Xunit;

namespace YouTubeTvShell.Tests;

/// <summary>
/// Proves the Task 4 close policy against the UI-free <see cref="CloseGuard"/>:
/// exactly-once disposal under concurrency, Esc never closes, and a close
/// during pending navigation exits cleanly without an unhandled exception.
/// </summary>
public class CloseGuardTests
{
    [Fact]
    public void ConcurrentCloses_DisposalRunsExactlyOnce()
    {
        var guard = new CloseGuard();
        int runs = 0;

        Parallel.For(0, 64, _ => guard.Close(() => Interlocked.Increment(ref runs), navigationPending: false));

        Assert.Equal(1, Volatile.Read(ref runs));
        Assert.True(guard.DisposalStarted);
    }

    [Fact]
    public void SecondClose_ReportsAlreadyDisposed_WithoutRerunningDisposal()
    {
        var guard = new CloseGuard();
        int runs = 0;

        var first = guard.Close(() => Interlocked.Increment(ref runs), navigationPending: false);
        var second = guard.Close(() => Interlocked.Increment(ref runs), navigationPending: false);

        Assert.True(first.DisposalRan);
        Assert.False(first.AlreadyDisposed);
        Assert.False(second.DisposalRan);
        Assert.True(second.AlreadyDisposed);
        Assert.Equal(1, runs);
    }

    [Fact]
    public void EscNeverCloses_NativeAndAltF4AlwaysClose()
    {
        Assert.False(CloseGuard.ShouldAllowClose(CloseRequestKind.Escape));
        Assert.True(CloseGuard.ShouldAllowClose(CloseRequestKind.Native));
        Assert.True(CloseGuard.ShouldAllowClose(CloseRequestKind.AltF4));
        Assert.True(CloseGuard.ShouldAllowClose(CloseRequestKind.Other));
    }

    [Fact]
    public void CloseDuringPendingNavigation_ExitsCleanly_NoException()
    {
        var guard = new CloseGuard();

        var outcome = guard.Close(() => { }, navigationPending: true);

        Assert.True(outcome.DisposalRan);
        Assert.True(outcome.NavigationWasPending);
        Assert.Null(outcome.Error);
    }

    [Fact]
    public void ThrowingDispose_ReportsErrorType_StillCountsAsDisposed()
    {
        var guard = new CloseGuard();

        var outcome = guard.Close(() => throw new InvalidOperationException("webview gone"), navigationPending: false);

        Assert.True(outcome.DisposalRan);
        Assert.NotNull(outcome.Error);
        Assert.Contains("InvalidOperationException", outcome.Error);

        // A retry after a faulted disposal must not run disposal again.
        var retry = guard.Close(() => { }, navigationPending: false);
        Assert.True(retry.AlreadyDisposed);
    }

    [Fact]
    public void SysF4_IsNativeCloseAccelerator_PlainF4OrNoAlt_IsNot()
    {
        Assert.True(CloseGuard.IsNativeCloseAccelerator(CloseGuard.VkF4, isSystemKeyDown: true));
        Assert.False(CloseGuard.IsNativeCloseAccelerator(CloseGuard.VkF4, isSystemKeyDown: false));
        Assert.False(CloseGuard.IsNativeCloseAccelerator(CloseGuard.VkEscape, isSystemKeyDown: true));
    }
}

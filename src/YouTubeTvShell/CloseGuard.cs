namespace YouTubeTvShell;

/// <summary>
/// Classifies a request to close the main window. Task 4 close policy:
/// native and Alt+F4 requests always close; Escape never closes.
/// </summary>
public enum CloseRequestKind
{
    Native,
    AltF4,
    Escape,
    Other,
}

/// <summary>
/// Receipt for a <see cref="CloseGuard.Close"/> call. Never throws by construction.
/// </summary>
public sealed record CloseOutcome(
    bool DisposalRan,
    bool AlreadyDisposed,
    bool NavigationWasPending,
    string? Error);

/// <summary>
/// Pure, UI-free close and disposal decision logic for the main window (Task 4).
/// Uses no UI types so it can be unit tested without a window or WebView2.
/// Thread-safe: disposal runs exactly once even under concurrent close.
/// </summary>
public sealed class CloseGuard
{
    public const uint VkEscape = 0x1B;
    public const uint VkF4 = 0x73;

    private int _disposalState;

    public bool DisposalStarted => Volatile.Read(ref _disposalState) != 0;

    public bool TryBeginDisposal() => Interlocked.CompareExchange(ref _disposalState, 1, 0) == 0;

    public static bool ShouldAllowClose(CloseRequestKind kind) => kind != CloseRequestKind.Escape;

    public static bool IsNativeCloseAccelerator(uint virtualKey, bool isSystemKeyDown) =>
        isSystemKeyDown && virtualKey == VkF4;

    public CloseOutcome Close(Action? disposeWebView, bool navigationPending)
    {
        if (!TryBeginDisposal())
        {
            return new CloseOutcome(
                DisposalRan: false,
                AlreadyDisposed: true,
                NavigationWasPending: navigationPending,
                Error: null);
        }

        try
        {
            disposeWebView?.Invoke();
            return new CloseOutcome(
                DisposalRan: true,
                AlreadyDisposed: false,
                NavigationWasPending: navigationPending,
                Error: null);
        }
        catch (Exception ex)
        {
            return new CloseOutcome(
                DisposalRan: true,
                AlreadyDisposed: false,
                NavigationWasPending: navigationPending,
                Error: ex.GetType().FullName ?? "System.Exception");
        }
    }
}

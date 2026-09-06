using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace YouTubeTvShell;

// ── Single-instance types (pure decision logic) ─────────────────────────

/// <summary>
/// Decision the application startup should act on.
/// </summary>
public enum SingleInstanceDecision
{
    /// <summary>This is the first instance; create the main window.</summary>
    ProceedAsOwner,

    /// <summary>Another instance already exists; foreground it and exit.</summary>
    ExitAndForegroundExisting
}

/// <summary>
/// Pure evaluation of the single-instance policy.  The caller supplies a
/// boolean that represents whether this process successfully registered as
/// the sole owner.  Unit tests inject controlled values; production code
/// wires the real Mutex / AppInstance guard.
/// </summary>
public static class SingleInstancePolicy
{
    /// <param name="isFirstInstance">
    /// True when this process registered successfully and no other instance
    /// was already running.
    /// </param>
    public static SingleInstanceDecision Decide(bool isFirstInstance)
    {
        return isFirstInstance
            ? SingleInstanceDecision.ProceedAsOwner
            : SingleInstanceDecision.ExitAndForegroundExisting;
    }
}

// ── Host-level Esc interception + single-instance stub ──────────────────

public sealed partial class MainWindow
{
    private ShellNavigationState? _escNavState;

    /// <summary>
    /// Wire host-level Esc key interception.
    /// Call once after <c>InitializeComponent()</c> in the MainWindow constructor.
    /// Task 2 / Task 4 will add the call; this partial provides the implementation.
    /// </summary>
    internal void InitializeEscHandling()
    {
        _escNavState = new ShellNavigationState();

        if (Content is UIElement root)
        {
            // handledEventsToo = true ensures we see Esc even if another
            // handler marked it handled (e.g. a focused control).
            root.AddHandler(
                UIElement.KeyDownEvent,
                new KeyEventHandler(OnHostKeyDown),
                handledEventsToo: true);
        }
    }

    /// <summary>
    /// Expose navigation state so Task 2's WebViewSetup partial can feed
    /// NavigationCompleted / NavigationFailed events into it.
    /// </summary>
    internal ShellNavigationState NavigationState =>
        _escNavState ?? throw new InvalidOperationException(
            "Call InitializeEscHandling() before accessing NavigationState.");

    // ── Esc key handler ─────────────────────────────────────────────────

    private void OnHostKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Escape)
            return;

        var navState = _escNavState;
        if (navState is null)
            return;

        switch (navState.HandleEsc())
        {
            case EscDecision.NavigateHome:
                // Navigate to the fixed home URL.
                // NavigationCompleted will call RecordHome() to confirm arrival.
                NavigateToHome();
                args.Handled = true;
                break;

            case EscDecision.NoOp:
                // Already at home — swallow Esc so it never becomes a close
                // shortcut (Task 4 owns close behavior).
                args.Handled = true;
                break;

            case EscDecision.ShowError:
                // Prior home navigation failed. Surface a host-level error;
                // do not retry blindly.  Task 2/4 wire the actual error UI.
                args.Handled = true;
                break;
        }
    }

    // ── Navigation bridge (partial method — Task 2 provides the body) ───

    /// <summary>
    /// Initiate navigation to the fixed home URL via WebView2.
    /// Declared as a partial method so Task 2's WebViewSetup partial can
    /// provide the real implementation (<c>WebView.Navigate(url)</c>).
    /// Until then, calls here compile away to a no-op.
    /// </summary>
    partial void NavigateToHome();

    // ── Navigation event bridges (call from Task 2's NavigationCompleted) ─

    /// <summary>
    /// Feed a successful navigation result into the state machine.
    /// Task 2 calls this from WebView2's NavigationCompleted handler.
    /// </summary>
    internal void OnNavigationCompleted(string finalUrl)
    {
        _escNavState?.RecordNavigated(finalUrl);
    }

    /// <summary>
    /// Feed a failed home-navigation attempt into the state machine.
    /// Task 2 calls this from WebView2's NavigationFailed handler when
    /// the target was the home URL.
    /// </summary>
    internal void OnHomeNavigationFailed()
    {
        _escNavState?.RecordNavigationFailure();
    }

    // ── Single-instance integration (real named-Mutex guard) ──────────

    private static Mutex? _singleInstanceMutex;

    /// <summary>
    /// Evaluate single-instance policy using a process-lifetime named mutex.
    /// First caller to own <c>YouTubeTvShell-SingleInstance</c> proceeds;
    /// later callers must foreground the existing window and exit.
    /// The mutex handle is intentionally leaked for the process lifetime —
    /// the OS releases it on exit. See decisions.md for the Mutex-over-AppInstance rationale.
    /// </summary>
    internal static SingleInstanceDecision EvaluateSingleInstance()
    {
        try
        {
            var mutex = new Mutex(initiallyOwned: true, name: "YouTubeTvShell-SingleInstance", createdNew: out bool createdNew);
            if (createdNew)
            {
                _singleInstanceMutex = mutex;
                return SingleInstancePolicy.Decide(isFirstInstance: true);
            }

            mutex.Dispose();
            return SingleInstancePolicy.Decide(isFirstInstance: false);
        }
        catch
        {
            // If the guard itself is unavailable, fail safe toward single ownership
            // so the app still starts (Task 5 harness proves the contention path).
            return SingleInstancePolicy.Decide(isFirstInstance: true);
        }
    }
}

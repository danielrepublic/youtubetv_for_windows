using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using Microsoft.Web.WebView2.Core;

namespace YouTubeTvShell;

/// <summary>
/// Native window lifecycle: fullscreen-on-launch windowing, exactly-once
/// WebView2 disposal on close, Alt+F4 passthrough while WebView2 owns keyboard
/// focus, and Esc never closing the window (Esc is owned by the EscHandling partial).
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly CloseGuard _closeGuard = new();

    /// <summary>
    /// Wire fullscreen presentation and the native close path.
    /// Call once after <c>InitializeComponent()</c> in the MainWindow constructor.
    /// </summary>
    internal void InitializeWindowLifecycle()
    {
        // Maximize (not OverlappedPresenterState.Fullscreen): the window fills the
        // screen while the caption and system menu stay alive, so the native close
        // command is always reachable. Only OverlappedPresenter.Maximize is used.
        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.Maximize();

        Closed += MainWindow_Closed;
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _closeGuard.Close(DisposeWebView, navigationPending: _navigationPending);
    }

    private void DisposeWebView()
    {
        WebView?.Close();
    }

    /// <summary>
    /// WebView2 accelerator routing note: <c>AcceleratorKeyPressed</c> lives on
    /// <c>CoreWebView2Controller</c>, which the WinUI XAML <c>WebView2</c> control
    /// does not surface (verified against WebView2 1.0.4191.47 metadata), so no
    /// handler is attached here. The Alt+F4 guarantee therefore rests on two
    /// properties this file owns: (1) we never mark system keys handled anywhere
    /// in the host — Esc is the only key we intercept, via the EscHandling
    /// partial, and it can never become a close shortcut
    /// (<see cref="CloseGuard.ShouldAllowClose"/>); (2) the <c>Closed</c> path
    /// below disposes exactly once. Whether Alt+F4 reaches the window proc while
    /// WebView2 owns focus is proven at the Task 5 CDP level
    /// (focus WebView2, send Alt+F4, assert process exit); if the live control
    /// swallows it, the fallback is disabling browser accelerator keys.
    /// The <see cref="CloseGuard.IsNativeCloseAccelerator"/> classifier plus its
    /// unit tests pin the Sys+F4 recognition logic used by that verification.
    /// </summary>
}

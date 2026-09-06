namespace YouTubeTvShell;

/// <summary>
/// Decision the host should make when the user presses Escape.
/// Pure output — never carries a WebView or DOM reference.
/// </summary>
public enum EscDecision
{
    /// <summary>Shell is not at home; navigate to the fixed home URL.</summary>
    NavigateHome,

    /// <summary>Shell is already at home; swallow the key (no-op).</summary>
    NoOp,

    /// <summary>A prior home navigation failed; surface a host-level error.</summary>
    ShowError
}

/// <summary>
/// Pure, testable state machine that tracks whether the shell is currently
/// at the YouTube TV home page.  Zero WebView, DOM, or UI-framework
/// dependencies — unit tests prove every transition deterministically.
/// </summary>
public sealed class ShellNavigationState
{
    internal const string FixedHomeUrl = BrowserConstants.FixedHomeUrl;

    private bool _isHome;
    private bool _lastNavigationFailed;

    /// <summary>Whether the shell is currently showing the home URL.</summary>
    public bool IsHome => _isHome;

    /// <summary>
    /// Record that a navigation completed to <paramref name="url"/>.
    /// Updates home state based on the final URL — never optimistically.
    /// </summary>
    public void RecordNavigated(string url)
    {
        _isHome = string.Equals(url, FixedHomeUrl, StringComparison.OrdinalIgnoreCase);
        _lastNavigationFailed = false;
    }

    /// <summary>
    /// Record that a home navigation completed successfully.
    /// Must only be called after NavigationCompleted confirms the URL.
    /// </summary>
    public void RecordHome()
    {
        _isHome = true;
        _lastNavigationFailed = false;
    }

    /// <summary>
    /// Record that a home navigation failed.
    /// Shell remains in non-home state so the next Esc can retry.
    /// </summary>
    public void RecordNavigationFailure()
    {
        _lastNavigationFailed = true;
    }

    /// <summary>
    /// Evaluate what the host should do when the user presses Escape.
    /// Pure decision — the caller is responsible for acting on it.
    /// </summary>
    public EscDecision HandleEsc()
    {
        if (_isHome)
            return EscDecision.NoOp;

        if (_lastNavigationFailed)
            return EscDecision.ShowError;

        return EscDecision.NavigateHome;
    }
}

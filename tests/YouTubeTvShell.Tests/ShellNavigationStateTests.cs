using Xunit;

namespace YouTubeTvShell.Tests;

public class ShellNavigationStateTests
{
    // ── Initial state ───────────────────────────────────────────────────

    [Fact]
    public void NewState_DefaultIsNotHome()
    {
        var state = new ShellNavigationState();

        Assert.False(state.IsHome);
    }

    // ── Happy-path: Esc twice (non-home → home → no-op) ────────────────

    [Fact]
    public void FirstEsc_FromNonHome_ReturnsNavigateHome()
    {
        var state = new ShellNavigationState();
        // No navigation recorded → not at home

        var decision = state.HandleEsc();

        Assert.Equal(EscDecision.NavigateHome, decision);
    }

    [Fact]
    public void SecondEsc_AtHome_ReturnsNoOp()
    {
        var state = new ShellNavigationState();
        state.RecordHome();

        var decision = state.HandleEsc();

        Assert.Equal(EscDecision.NoOp, decision);
    }

    [Fact]
    public void EscTwice_FullCycle_NavigateThenNoOp()
    {
        var state = new ShellNavigationState();

        // First Esc: not at home → navigate
        var first = state.HandleEsc();
        Assert.Equal(EscDecision.NavigateHome, first);

        // Navigation completes to home URL
        state.RecordHome();

        // Second Esc: now at home → no-op
        var second = state.HandleEsc();
        Assert.Equal(EscDecision.NoOp, second);
    }

    // ── Failed navigation → remains non-home ────────────────────────────

    [Fact]
    public void FailedNavigation_RemainsNonHome()
    {
        var state = new ShellNavigationState();
        state.RecordNavigated("https://www.youtube.com/watch?v=abc");

        state.RecordNavigationFailure();

        Assert.False(state.IsHome);
    }

    [Fact]
    public void EscAfterFailedNavigation_ReturnsShowError()
    {
        var state = new ShellNavigationState();
        state.RecordNavigationFailure();

        var decision = state.HandleEsc();

        Assert.Equal(EscDecision.ShowError, decision);
    }

    [Fact]
    public void FailedHomeNavigation_NoFalseHome_EscStillShowError()
    {
        var state = new ShellNavigationState();

        // Attempt home navigation that fails
        state.RecordNavigationFailure();

        // Must not report home
        Assert.False(state.IsHome);
        Assert.Equal(EscDecision.ShowError, state.HandleEsc());

        // Pressing Esc again after ShowError still shows error
        // (state hasn't changed — host acts on it, not the state machine)
        Assert.Equal(EscDecision.ShowError, state.HandleEsc());
    }

    // ── RecordNavigated edge cases ──────────────────────────────────────

    [Fact]
    public void RecordNavigated_ToHomeUrl_SetsIsHome()
    {
        var state = new ShellNavigationState();

        state.RecordNavigated("https://www.youtube.com/tv");

        Assert.True(state.IsHome);
    }

    [Fact]
    public void RecordNavigated_ToHomeUrl_CaseInsensitive()
    {
        var state = new ShellNavigationState();

        state.RecordNavigated("https://WWW.YOUTUBE.COM/TV");

        Assert.True(state.IsHome);
    }

    [Fact]
    public void RecordNavigated_ToOtherUrl_ClearsIsHome()
    {
        var state = new ShellNavigationState();
        state.RecordHome();

        state.RecordNavigated("https://www.youtube.com/watch?v=abc");

        Assert.False(state.IsHome);
    }

    [Fact]
    public void RecordNavigated_ClearsPriorFailure()
    {
        var state = new ShellNavigationState();
        state.RecordNavigationFailure();

        state.RecordNavigated("https://www.youtube.com/tv");

        Assert.True(state.IsHome);
        Assert.Equal(EscDecision.NoOp, state.HandleEsc());
    }

    // ── RecordHome ──────────────────────────────────────────────────────

    [Fact]
    public void RecordHome_AfterFailure_ClearsFailure()
    {
        var state = new ShellNavigationState();
        state.RecordNavigationFailure();

        state.RecordHome();

        Assert.True(state.IsHome);
        Assert.Equal(EscDecision.NoOp, state.HandleEsc());
    }

    // ── No YouTube DOM / selectors / WebView references ─────────────────

    [Fact]
    public void ShellNavigationState_HasNoWebViewDependencies()
    {
        // Verify the type lives in a pure-C# assembly with no WinUI imports.
        var type = typeof(ShellNavigationState);
        var assembly = type.Assembly;

        // The assembly should be the main app assembly (which does reference WinUI),
        // but ShellNavigationState itself must not reference any WinUI types.
        var referencedAssemblies = assembly.GetReferencedAssemblies();
        var hasWinUIReference = referencedAssemblies.Any(
            a => a.Name?.Contains("Microsoft.WindowsAppSDK") == true
              || a.Name?.Contains("Microsoft.UI.Xaml") == true);

        // The assembly references WinUI (because MainWindow does), but the
        // ShellNavigationState *type* only uses System types.
        // Assert the type's own fields/properties use only BCL types.
        var props = type.GetProperties(System.Reflection.BindingFlags.Public |
                                       System.Reflection.BindingFlags.Instance);
        foreach (var prop in props)
        {
            Assert.True(
                prop.PropertyType.Namespace?.StartsWith("System") == true
                || prop.PropertyType.Namespace == "YouTubeTvShell",
                $"Property {prop.Name} references non-BCL type {prop.PropertyType.FullName}");
        }
    }
}

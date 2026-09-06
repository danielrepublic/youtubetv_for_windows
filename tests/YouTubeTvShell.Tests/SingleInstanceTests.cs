using Xunit;

namespace YouTubeTvShell.Tests;

public class SingleInstanceTests
{
    // ── Policy decision logic ───────────────────────────────────────────

    [Fact]
    public void FirstInstance_DecisionIsProceedAsOwner()
    {
        var decision = SingleInstancePolicy.Decide(isFirstInstance: true);

        Assert.Equal(SingleInstanceDecision.ProceedAsOwner, decision);
    }

    [Fact]
    public void SecondInstance_DecisionIsExitAndForegroundExisting()
    {
        var decision = SingleInstancePolicy.Decide(isFirstInstance: false);

        Assert.Equal(SingleInstanceDecision.ExitAndForegroundExisting, decision);
    }

    [Fact]
    public void PolicyIsDeterministic_SameInputSameOutput()
    {
        // Call multiple times with the same input — must always return the same decision.
        Assert.Equal(SingleInstanceDecision.ProceedAsOwner,
            SingleInstancePolicy.Decide(isFirstInstance: true));
        Assert.Equal(SingleInstanceDecision.ProceedAsOwner,
            SingleInstancePolicy.Decide(isFirstInstance: true));
        Assert.Equal(SingleInstanceDecision.ExitAndForegroundExisting,
            SingleInstancePolicy.Decide(isFirstInstance: false));
        Assert.Equal(SingleInstanceDecision.ExitAndForegroundExisting,
            SingleInstancePolicy.Decide(isFirstInstance: false));
    }

    // ── Second-launch → no-second-window contract ───────────────────────

    [Fact]
    public void SecondLaunch_DecisionPreventsSecondWindow()
    {
        // When isFirstInstance is false, the decision must be ExitAndForegroundExisting.
        // The caller (App.OnLaunched) must exit without creating MainWindow.
        var decision = SingleInstancePolicy.Decide(isFirstInstance: false);

        Assert.Equal(SingleInstanceDecision.ExitAndForegroundExisting, decision);
        Assert.NotEqual(SingleInstanceDecision.ProceedAsOwner, decision);
    }

    [Fact]
    public void FirstLaunch_DecisionPermitsWindowCreation()
    {
        var decision = SingleInstancePolicy.Decide(isFirstInstance: true);

        Assert.Equal(SingleInstanceDecision.ProceedAsOwner, decision);
        Assert.NotEqual(SingleInstanceDecision.ExitAndForegroundExisting, decision);
    }
}

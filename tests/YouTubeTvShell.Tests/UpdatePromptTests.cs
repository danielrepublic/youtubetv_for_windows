using Xunit;

namespace YouTubeTvShell.Tests;

/// <summary>
/// Pins the update-prompt dialog model without instantiating WinUI types:
/// prompt-text building (version + plain-text notes) and the
/// Confirm/Cancel latch transitions the dialog drives. The dialog itself is
/// null-guarded for headless contexts and covered by live/manual QA instead.
/// </summary>
public class UpdatePromptTests
{
    [Fact]
    public void BuildPromptText_HtmlNotes_IncludesVersionAndPlainText()
    {
        var text = App.BuildPromptText("2.0.0", "<h1>Highlights</h1><p>Faster &amp; leaner</p>");

        Assert.Contains("2.0.0", text);
        Assert.Contains("Highlights", text);
        Assert.Contains("Faster & leaner", text);
        Assert.DoesNotContain("<h1>", text);
        Assert.DoesNotContain("&amp;", text);
    }

    [Fact]
    public void BuildPromptText_EmptyNotes_FallsBackToVersionOnly()
    {
        Assert.Equal("Version 2.0.0 is available.", App.BuildPromptText("2.0.0", null));
        Assert.Equal("Version 2.0.0 is available.", App.BuildPromptText("2.0.0", "  "));
        Assert.Equal("Version Unknown version is available.", App.BuildPromptText(null, null));
    }

    [Fact]
    public void ConfirmLatch_Transitions_BlockDownloadUntilConfirmed()
    {
        // Fresh service mirrors dialog-Cancel/close: latch unset, download blocked.
        var svc = new UpdateService();
        Assert.False(svc.IsConfirmed);
        Assert.Throws<InvalidOperationException>(svc.RequireConfirmation);

        // Dialog-Confirm records the latch, unblocking download.
        svc.ConfirmUpdate();
        Assert.True(svc.IsConfirmed);
        svc.RequireConfirmation(); // must not throw

        // A new check cycle resets the latch — no stale confirmation carries over.
        svc.Decide(isUpdateAvailable: true, targetVersion: "2.0.0", releaseNotes: "notes");
        Assert.False(svc.IsConfirmed);
        Assert.Throws<InvalidOperationException>(svc.RequireConfirmation);
    }
}

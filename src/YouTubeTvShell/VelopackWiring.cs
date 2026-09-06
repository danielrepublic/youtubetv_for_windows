using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Velopack;
using Velopack.Sources;

namespace YouTubeTvShell;

/// <summary>
/// Velopack update wiring for <see cref="App"/>. Owns the UpdateManager lifecycle,
/// startup check, confirmation dialog, and download-apply path.
///
/// Design:
///  - The <see cref="UpdateService"/> holds the pure decision state machine.
///  - This file holds the Velopack boundary: UpdateManager, GithubSource, and the
///    prompt/download/restart integration.
///  - On startup the check fires asynchronously (fire-and-forget) and never blocks
///    window launch.
///  - Zero bytes are downloaded before explicit user confirmation.
///  - All errors produce a retryable user-facing message; no stack traces, file
///    paths, or tokens leak to the UI.
/// </summary>
public sealed partial class App
{
    internal static UpdateService UpdateService { get; } = new();

    /// <summary>Stored between check and download — Velopack needs the original UpdateInfo.</summary>
    private static UpdateInfo? _pendingVelopackInfo;

    /// <summary>
    /// Start the update check. Fire-and-forget — the caller (OnLaunched) returns
    /// immediately so the window activates without waiting for the network.
    /// </summary>
    internal static void StartUpdateCheck()
    {
        _ = CheckForUpdatesOnStartupAsync();
    }

    /// <summary>
    /// Full update cycle: check → decide → prompt → confirm → download → restart.
    /// Every failure path surfaces a retryable message and leaves the current
    /// version running.
    /// </summary>
    internal static async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            var source = TestHooks.GetUpdateFeedSource(UpdateConfig.GitHubRepoUrl);

            var mgr = new UpdateManager(source);

            // If already installed via Velopack, we can check.
            // If running from dev/build, IsInstalled is false — skip.
            if (!mgr.IsInstalled)
                return;

            // CheckForUpdatesAsync has no CancellationToken overload;
            // wrap in a timeout using Task.WhenAny.
            var checkTask = mgr.CheckForUpdatesAsync();
            if (await Task.WhenAny(checkTask, Task.Delay(TimeSpan.FromSeconds(UpdateConfig.CheckTimeoutSeconds)))
                != checkTask)
            {
                throw new TaskCanceledException("Update check timed out.");
            }

            var info = await checkTask;
            _pendingVelopackInfo = info;

            var decision = UpdateService.Decide(
                isUpdateAvailable: info is { IsDowngrade: false },
                targetVersion: info?.TargetFullRelease?.Version?.ToString(),
                releaseNotes: info?.TargetFullRelease?.NotesHTML
                           ?? info?.TargetFullRelease?.NotesMarkdown
                           ?? "");

            if (decision.Outcome == UpdateOutcome.Available)
            {
                // Prompt the user with a real Confirm/Cancel dialog, then
                // download only after explicit confirmation.
                await PromptAndUpdateAsync(mgr, info!, decision);
            }
        }
        catch (OperationCanceledException)
        {
            var decision = UpdateService.DecideFromException(
                new TaskCanceledException("Update check timed out."));
            OnUpdateError(decision.Error!);
        }
        catch (Exception ex)
        {
            var decision = UpdateService.DecideFromException(ex);
            OnUpdateError(decision.Error!);
        }
    }

    /// <summary>
    /// Prompt the user with version + release notes, then download and apply
    /// only after explicit confirmation. The prompt data is exposed via events
    /// so tests can intercept it without depending on WinUI dialog types.
    /// </summary>
    internal static async Task PromptAndUpdateAsync(
        UpdateManager mgr,
        UpdateInfo info,
        UpdateDecision decision)
    {
        // Expose prompt data for test capture and host UI wiring.
        UpdatePromptRaised?.Invoke(null, decision);

        // Show the real Confirm/Cancel dialog. False covers Cancel, dialog
        // close, and the headless/test context (no window or UI thread):
        // zero bytes are downloaded in all of those cases.
        if (!await ShowUpdatePromptAsync(decision))
            return;

        // The dialog confirmed — record it on the latch, then proceed.
        // The latch check below stays as defense for programmatic callers.
        UpdateService.ConfirmUpdate();
        if (!UpdateService.IsConfirmed)
            return; // Waiting for host confirmation — not yet confirmed.

        try
        {
            await mgr.DownloadUpdatesAsync(info, progress: null, cancelToken: CancellationToken.None);
        }
        catch (Exception ex)
        {
            _pendingVelopackInfo = null;
            OnUpdateError(UpdateService.UserFacingError(ex));
            return;
        }

        try
        {
            // ApplyUpdatesAndRestart replaces the process. Code after this
            // line runs only if apply preparation fails.
            mgr.ApplyUpdatesAndRestart(info.TargetFullRelease);
        }
        catch (Exception ex)
        {
            _pendingVelopackInfo = null;
            OnUpdateError(UpdateService.UserFacingError(ex));
        }
    }

    /// <summary>
    /// Builds the update-prompt dialog text: target version plus the release
    /// notes converted to plain text (Velopack serves NotesHTML; tags are
    /// stripped and entities decoded — see <see cref="ToPlainText"/>).
    /// Pure and UI-free so unit tests can pin it without WinUI types.
    /// </summary>
    internal static string BuildPromptText(string? version, string? notes)
    {
        var v = string.IsNullOrWhiteSpace(version) ? "Unknown version" : version.Trim();
        var plain = ToPlainText(notes);
        return string.IsNullOrEmpty(plain)
            ? $"Version {v} is available."
            : $"Version {v} is available.\n\n{plain}";
    }

    /// <summary>
    /// Converts Velopack release notes (HTML or Markdown) to plain dialog text.
    /// Strips tags, decodes entities, and collapses blank lines. Never throws.
    /// </summary>
    internal static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;
        try
        {
            var noTags = Regex.Replace(html, "<[^>]*>", " ");
            var decoded = WebUtility.HtmlDecode(noTags);
            var collapsed = Regex.Replace(decoded, @"[ \t\xa0]+", " ");
            var lines = collapsed
                .Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0);
            return string.Join("\n", lines);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Shows the update-available ContentDialog (title "Update available",
    /// version + notes, Confirm/Cancel). Runs on the UI thread via the current
    /// dispatcher. Returns true only on explicit Confirm. Returns false —
    /// downloading nothing — on Cancel, dialog close, missing window, missing
    /// UI thread (tests/headless), or any dialog error. Never throws.
    /// </summary>
    internal static async Task<bool> ShowUpdatePromptAsync(UpdateDecision decision)
    {
        try
        {
            var root = GetMainWindowXamlRoot();
            if (root is null)
                return false;

            var queue = DispatcherQueue.GetForCurrentThread();
            if (queue is null)
                return false;

            if (queue.HasThreadAccess)
                return await ShowDialogOnUiThreadAsync(root, decision);

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!queue.TryEnqueue(async () =>
            {
                try
                {
                    tcs.SetResult(await ShowDialogOnUiThreadAsync(root, decision));
                }
                catch
                {
                    tcs.SetResult(false);
                }
            }))
            {
                return false;
            }

            return await tcs.Task;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> ShowDialogOnUiThreadAsync(XamlRoot root, UpdateDecision decision)
    {
        var dialog = new ContentDialog
        {
            Title = "Update available",
            Content = BuildPromptText(decision.TargetVersion, decision.ReleaseNotes),
            PrimaryButtonText = "Confirm",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = root,
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    /// <summary>
    /// Resolves the main window's XamlRoot for the prompt dialog. Reads the
    /// App partial's window field via reflection so this file stays the only
    /// one touched (App.xaml.cs and MainWindow.xaml.cs are sibling-owned).
    /// Null in tests/headless — the caller treats that as Cancel. Never throws.
    /// </summary>
    private static XamlRoot? GetMainWindowXamlRoot()
    {
        try
        {
            if (Application.Current is not App app)
                return null;
            var field = typeof(App).GetField("_window", BindingFlags.NonPublic | BindingFlags.Instance);
            return (field?.GetValue(app) as Window)?.Content?.XamlRoot;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Raised when an update is available and the user should be prompted.
    /// Handlers receive the <see cref="UpdateDecision"/> with version and notes.
    /// </summary>
    internal static event EventHandler<UpdateDecision>? UpdatePromptRaised;

    /// <summary>
    /// Raised when an update operation fails. The message is user-facing and
    /// retryable — no sensitive details.
    /// </summary>
    internal static event EventHandler<string>? UpdateErrorRaised;

    private static void OnUpdateError(string message)
    {
        UpdateErrorRaised?.Invoke(null, message);
    }
}

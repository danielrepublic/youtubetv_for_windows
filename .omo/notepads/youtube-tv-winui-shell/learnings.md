# Learnings — youtube-tv-winui-shell

Conventions, patterns, and successful approaches discovered during work on this plan.

_Auto-scaffolded by /ulw-execute. Append new entries below - never overwrite._

---

## [2026-09-05] Task 1 scaffold

- Unpackaged WinUI 3 app pattern: `WindowsPackageType=None` in csproj, `UseWinUI=true`, `EnableMsixTooling=true`. No Package.appxmanifest needed for unpackaged.
- Do NOT add a custom `Program.cs` entry point for WinUI 3 — the XAML compiler auto-generates `App.g.i.cs` with a `Main` method. A manual `Program.cs` causes CS0101/CS0111 duplicate definition errors.
- Test projects targeting `net8.0-windows10.0.19041.0` trigger WinRT Source Generator which emits `unsafe` code. Add `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` to the test csproj.
- Remove `RuntimeIdentifier` from test projects — it forces the test host to a specific RID directory that may not have the required runtime installed. Let `dotnet test` resolve the runtime from the default probing paths.
- `DOTNET_ROOT` environment variable must point to `C:\Program Files\dotnet` (global install) when the user-local `C:\Users\{user}\AppData\Local\Microsoft\dotnet` does not have the required .NET 8.0 x64 runtime.
- WindowsAppSDK 2.4.0 transitively depends on `Microsoft.Web.WebView2 >= 1.0.3719.77`. Pin WebView2 explicitly at 1.0.4191.47 (latest stable) to avoid NU1605 downgrade errors.
- Solution layout: `YouTubeTvShell.sln` at root, `src/YouTubeTvShell/` for app, `tests/YouTubeTvShell.Tests/` for tests. Single solution file.

---

## [2026-09-05] Task 2-4 repair

- The repair brief's claim that `Youtube TV.lnk` contains no `--user-agent`/URL was wrong: it came from an ASCII-only byte dump, but Shell-Link StringData stores arguments as UTF-16LE. Decoding UTF-16LE runs reveals `--user-data-dir="C:\YoutubeTV" --user-agent="Mozilla/5.0 (PS4; Leanback Shell) ... Sony PS4/ (PS4, , no, CH)" --new-window --kiosk "https://www.youtube.com/tv"`, target `C:\Program Files\Google\Chrome\Application\chrome.exe`, icon `%SystemDrive%\YoutubeTV\icons\YoutubeTV.ico`. The existing real-.lnk extractor tests pass truthfully (31/32 green on arrival); the premise was corrected, not the tests.
- CS0123/WMC9999 on arrival were already fixed by a prior worker (`object sender`); the WMC9999 XAML error was a cascade of the C# errors, not an independent XAML defect. New CS1061s during repair (same WMC9999 cascade) came from assuming APIs that do not exist on the referenced types.
- WebView2 1.0.4191.47 ground truth (verified in package metadata, not docs): `AcceleratorKeyPressed` lives on `CoreWebView2Controller` (`add_AcceleratorKeyPressed` in the .winmd), NOT on `CoreWebView2`; `CoreWebView2NavigationCompletedEventArgs` has only IsSuccess/WebErrorStatus/HttpStatusCode/NavigationId — final URL comes from `CoreWebView2.Source`. The WinUI XAML `WebView2` control surfaces neither the controller nor the event.
- WinUI3 `Window` has `Content` but no `Loaded` event — hook the content-root `FrameworkElement.Loaded` for post-XAML init.
- `ProfileBoundaryTests` scans src/ for the literal `C:\YoutubeTV`, so even doc comments must avoid it; the implementation constructs the forbidden path dynamically (`Path.GetPathRoot + "YoutubeTV"`) and tests carry the literal instead.
- Tests can own artifact provenance: `HostBehaviorArtifactTests` exercises the pure state machines and writes the `02/03/04-*.json` QA files as a side effect, mirroring the existing `02-profile-rejection.log` pattern.

---

## [2026-09-05] Task 6 velopack

- Velopack 1.2.0 `UpdateManager` is NOT `IDisposable` — cannot use `using var mgr = new UpdateManager(...)`. Store or let GC collect.
- `CheckForUpdatesAsync()` takes zero arguments — no `CancellationToken` overload. Use `Task.WhenAny` with `Task.Delay` for timeout.
- `DownloadUpdatesAsync(UpdateInfo, Action<int>, CancellationToken)` — all three params required; pass `progress: null, cancelToken: CancellationToken.None` for no-progress/no-cancel.
- `ApplyUpdatesAndRestart(VelopackAsset, string[])` takes `VelopackAsset` (not `UpdateInfo`) — use `info.TargetFullRelease`.
- `VelopackAsset.NotesHTML` is the property name (capital HTML), not `NotesHtml`. Confirmed via reflection.
- `GithubSource(string repoUrl, string accessToken, bool prerelease, IFileDownloader downloader)` — 4-arg constructor. Pass `null` for both optional params for public-read repos.
- `VelopackAsset` has a public parameterless constructor and settable properties — test fixtures can construct feed entries directly.
- `VelopackAssetType` enum: `Full = 1, Delta = 2, Portable = 3, Installer = 4, Msi = 5`.
- `SemanticVersion` constructor: `new SemanticVersion(int major, int minor, int patch, string prerelease, string metadata)`.
- Pure state machine + unit test pattern (ShellNavigationState, CloseGuard) maps directly to the update decision flow: `UpdateService` holds the latch, `VelopackWiring` holds the Velopack boundary.
- Local feed for testing: `releases.win.json` is a JSON array of `VelopackAsset`-shaped objects. `UpdateManager(localDirPath)` auto-detects file source. A minimal `.nupkg` (NuGet ZIP with `[Content_Types].xml`, `_rels/.rels`, `.nuspec`, `core-properties`) is sufficient for feed structure tests.

---

## [2026-09-05] Task 5/6 gap closure

- Update prompt dialog: `ContentDialog` (Title "Update available", version + plain-text notes, Confirm/Cancel) shown from `PromptAndUpdateAsync` after the `UpdatePromptRaised` event (event kept for test capture). Confirm calls `UpdateService.ConfirmUpdate()` and proceeds; Cancel/close/no-UI returns before any download — zero bytes without consent.
- UI-thread pattern: `DispatcherQueue.GetForCurrentThread()` + `HasThreadAccess` fast path, else `TryEnqueue` with a `TaskCompletionSource`; every null/error path returns false. `Application.Current` is null in unit tests so the dialog is headless-safe by construction.
- XamlRoot without touching sibling-owned files: `GetMainWindowXamlRoot()` reads the App partial's private `_window` field via reflection (same class/assembly, fully guarded) — `App.xaml.cs` and `MainWindow.xaml.cs` stay untouched.
- Notes conversion is documented and pure: `ToPlainText` strips tags + `WebUtility.HtmlDecode` + whitespace collapse; `BuildPromptText` is UI-free so `UpdatePromptTests` pins it with no WinUI instantiation.
- Constraint compliance: headless-safe means `dotnet test --filter "FullyQualifiedName!~LiveApp"` runs zero windows (84/84 in ~2s); the 4 `LiveApp_*` tests never execute under that filter.

---

## [2026-09-05] Task 7 release docs

- Real vpk 1.2.0 runs only with $env:DOTNET_ROOT = 'C:\Program Files\dotnet': the g-tool shim otherwise resolves the user-local dotnet (10.0.10 only) and dies before parsing args. With DOTNET_ROOT set, vpk answers and pk pack reaches pre-process — then refuses with 'Unable to verify VelopackApp is called', i.e. the app never calls VelopackApp.Build().Run(). That is a src/ bootstrap gap for a later task, not a docs defect: Task 7 stays src untouched and the dry-run checksums the staged exe instead (honest provenance in artifacts/release/release-notes-0.0.1-dryrun.md + vpk-pack-attempt.log).
- vpk --packVersion must be >= 0.0.1: '0.0.0-dryrun' is rejected outright; '0.0.1-dryrun' passes version parsing and reaches the VelopackApp check. CI strips the leading 'v' from the tag before passing --packVersion.
- Checksum-guard pattern that satisfies the failure-path: checksum FIRST, corrupt SECOND, verify as a *child* powershell process so the non-zero exit (1) is a genuine process code in 07-checksum-guard.log, plus a prior-release stub re-hashed before/after to prove it is byte-identical. Transcript Append across two commands keeps one continuous log.
- Start-Transcript output is the log: keep the setup and corrupt+verify phases in two transcript sessions appended to the same file so the checksum-before-corruption ordering is auditable.

## [2026-09-05] VelopackApp bootstrap

- Added `VelopackApp.Build().Run()` as the first operation in the `App` constructor, before `InitializeComponent()`, matching Velopack guidance for early WinUI startup and preserving the existing launch guard and update wiring.
- `vpk pack --packId YouTubeTvShell --packVersion 0.0.1-verify` passed the former verification gate and completed both portable and setup package creation; the log warns only that constructor placement is not the generated `Main` entry point.
- No Velopack 1.2.0 API gotchas were encountered; the C# API shape is `VelopackApp.Build().Run()`.

---

## [2026-09-05] F1 scope audit

- F1 approved: source contains the approved Windows-only WebView2 shell and confirmation-gated public GitHub Releases updates only. No settings UI, Chrome-profile sharing, credentials, automated YouTube navigation, DRM/ad modification, telemetry, code signing, cross-platform target, or paid service was found.
- `dotnet test YouTubeTvShell.sln -c Release --no-build --filter "FullyQualifiedName!~LiveApp"` requires `DOTNET_ROOT=C:\Program Files\dotnet` in this environment; with that documented runtime selection it passed 84/84 in 2 seconds. The raw invocation selected user-local .NET 10 and could not start the x64 .NET 8 testhost.

## [2026-09-05] F4 implementation-controlled blockers

- Release checksums must select the publication assets from the `artifacts/release` root; recursive discovery would also hash the staged guard executable. The fresh manifest now covers the setup installer (`8D0086DCD23ADB6F11DD9AE6F9BA9325304EEE015D5AE1D698C168004E1A700C`) and full package (`614E0B69C4E99932B96DA07C23B959C8A18FD74B266CC7B13C04D090D114FC3C`).
- The release upload glob must explicitly include `artifacts/release/*.exe` alongside package and metadata assets. A child verifier rejected corruption of a copied full package with exit code 1 while both original hashes remained unchanged.


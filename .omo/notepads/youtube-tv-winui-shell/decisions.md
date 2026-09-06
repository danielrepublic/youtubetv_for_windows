# Decisions — youtube-tv-winui-shell

Architectural choices and rationales discovered during work on this plan.

_Auto-scaffolded by /ulw-execute. Append new entries below - never overwrite._

---

## [2026-09-05] Task 1 scaffold

- **SDK choice**: Microsoft.WindowsAppSDK 2.4.0 (latest stable, 2026-08-13). Supports Windows 10 1809+, well beyond our 1903 minimum. No reason to use older 1.x line.
- **WebView2**: Pinned at 1.0.4191.47 explicitly even though WindowsAppSDK includes it transitively. Transparency and version control.
- **Target framework**: `net8.0-windows10.0.19041.0` with `SupportedOSPlatformVersion=10.0.19041.0`. Matches plan requirement for Windows 10 1903+ coverage. Using SDK 19041 gives broader compile-time API surface than 18362.
- **Unpackaged over MSIX**: `WindowsPackageType=None`. Velopack handles distribution; no MSIX store needed. Unsigned/SmartScreen friction is accepted per plan.
- **Test framework**: xunit 2.9.3 (v2 latest stable) over xunit.v3. Playwright's `Microsoft.Playwright.Xunit` package requires xunit 2.8+ and targets v2. Using v2 avoids compatibility issues.
- **No Package.appxmanifest**: Not needed for unpackaged desktop apps. The `app.manifest` provides DPI awareness and OS compatibility declarations.
- **No Program.cs**: WinUI 3 XAML compiler auto-generates the entry point. Manual entry point causes build errors.

---

## [2026-09-05] Task 2-4 repair

- **Presenter choice**: `OverlappedPresenter.Maximize()` at launch, not `OverlappedPresenterState.Fullscreen`. Maximize fills the screen while the caption and system menu stay alive, so the native close command is always reachable; Fullscreen would hide the caption. No restricted-presenter APIs are used anywhere.
- **Single-instance guard choice**: process-lifetime named `Mutex` (`YouTubeTvShell-SingleInstance`) over `AppInstance.FindOrRegisterForKey`. The mutex works identically for unpackaged apps with no package-identity dependency; the handle is intentionally held for process lifetime (OS releases on exit). Second launch exits without creating `MainWindow` and best-effort foregrounds the existing `YouTube TV` window via `FindWindowW`/`SetForegroundWindow`. `SingleInstancePolicy.Decide` stays a pure decision API so its tests need no OS.
- **UA-blocker stance**: no blocker filed — the checked-in .lnk DOES contain the PS4 Leanback UA and tv URL (UTF-16LE decode), so the plan's shortcut-compatibility premise holds and the real-.lnk regression tests stand as written. The repair brief's contrary claim is recorded as a corrected misread in learnings.md, not a blocker.
- **Alt+F4 wiring stance**: no `AcceleratorKeyPressed` handler attached because the event exists only on `CoreWebView2Controller`, which the WinUI XAML control does not surface. Guarantee rests on never marking system keys handled (Esc is the sole intercepted key and can never close) plus exactly-once `Closed` disposal; live focus behavior is Task 5's CDP proof, with disabling browser accelerator keys as the fallback if the control swallows Alt+F4.

---

## [2026-09-05] Task 6 velopack

- **Confirmation latch design**: `Interlocked.CompareExchange` on an `int` field (0/1), mirroring `CloseGuard.TryBeginDisposal`. `Decide()` resets the latch so a new check cycle cannot carry over a stale confirmation. `RequireConfirmation()` throws if latch is not set — this is the code invariant that prevents download without consent.
- **Feed layout for tests**: `releases.win.json` (JSON array of VelopackAsset-shaped objects) + `.nupkg` (NuGet ZIP) in a temp directory. `UpdateManager(localDirPath)` auto-selects `SimpleFileSource`. Minimal nupkg has `[Content_Types].xml`, `_rels/.rels`, `.nuspec`, `core-properties`.
- **Timeout approach**: `CheckForUpdatesAsync` has no CancellationToken overload. Use `Task.WhenAny(checkTask, Task.Delay(timeout))` — 10s default. Throw `TaskCanceledException` on timeout.
- **VelopackWiring as partial App**: keeps `UpdateInfo` reference between check and download. The pure `UpdateService` never touches Velopack types. Events (`UpdatePromptRaised`, `UpdateErrorRaised`) decouple the wiring from WinUI dialog types so tests can capture prompt data without UI dependencies.
- **UpdateManager not IDisposable**: unlike most SDK clients, `UpdateManager` does not implement `IDisposable`. Store or let GC collect — no `using` statement.
- **Mutex-vs-AppInstance**: N/A for this task. Single-instance guard (Task 3) uses named Mutex; update check runs after single-instance passes.
- **Error message policy**: `UserFacingError(Exception)` maps exception types to user-facing strings. No stack traces, file paths, checksums, or tokens leak. Generic fallback for unknown exceptions.

---

## [2026-09-05] Task 7 release docs

- **CI gating choice**: single uild-test-package job (restore/build Release, filtered !~LiveApp unit tests, vpk pack pinned 1.2.0, SHA-256 emit + verify, artifact upload) with a separate publish job on 
eeds: — fail-closed by job dependency so any red step refuses publication. Live tests stay manual-gated with an in-file comment citing the interactive-desktop + WebView2-runtime + 90s-wait rationale from issues.md; a red filtered run still blocks publish.
- **vpk availability verdict**: REAL install (dotnet tool install -g vpk --version 1.2.0 succeeded, CLI 1.2.0 confirmed) but REAL pack blocked by the missing VelopackApp bootstrap in src/ (out of scope, not edited). Dry-run artifacts are therefore: real TRX from the filtered suite, real vpk attempt log, staged-exe + SHA256SUMS.txt over the built exe, release-notes draft — each labeled real vs skipped in the notes draft.
- **No src/test/plan edits**: docs + workflow + artifacts + notepad appends only; the C:\YoutubeTV literal appears in docs/ (profile-boundary scan covers src/ only) and the workflow carries no tokens (built-in github.token for upload).


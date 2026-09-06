# youtube-tv-winui-shell - Work Plan

## TL;DR (For humans)

Create a Windows-only YouTube TV shell that replaces the current Chrome kiosk shortcut with a WinUI 3/WebView2 application. It launches fullscreen, retains the shortcut's TV-compatible PS4 Leanback user agent, and—unlike kiosk—always closes with Alt+F4 or the native close button.

The application owns a separate WebView2 profile, never touches `C:\YoutubeTV`, and treats YouTube as an external service: host behavior is automated; sign-in, entitlement, and DRM playback remain a user-owned local check. The PS4 Leanback endpoint is not a supported provider contract and may cease to present the TV UI without notice; a failed local manual compatibility check is a release blocker, not a defect to work around with content injection. It will distribute family updates through unsigned Velopack packages on GitHub Releases, with explicit confirmation before installation. It will not add a settings page, code signing, telemetry, cross-platform support, content injection, ad blocking, DRM workarounds, or automated YouTube interaction.

## Scope

### In

- C#/.NET 8, WinUI 3, and WebView2 application targeting Windows 10 version 1903+ and Windows 11 x64.
- A single-instance, fullscreen-on-launch window that owns the normal Windows close lifecycle.
- Exact reuse of the existing shortcut's PS4 Leanback user-agent and `https://www.youtube.com/tv` home endpoint.
- Treat the PS4 Leanback endpoint as an unsupported external compatibility assumption; if the local family-user checklist cannot load its TV UI, do not release a workaround that modifies YouTube or bypasses its controls.
- An app-owned WebView2 user-data folder under LocalAppData, not `C:\YoutubeTV` or any Chrome/Edge profile.
- Host-state Esc behavior: non-home -> direct home navigation; recorded home -> no action.
- Playwright automation for host behavior only, a deterministic local test page, and a documented manual YouTube validation checklist.
- Velopack packaging, GitHub Releases source, startup update check, release notes, confirmation, failure preservation, and release documentation.

### Out / Must-NOT-Have

- YouTube API use, DOM injection, automated sign-in, automated navigation/playback, cookies/credentials in code or tests, DRM modification, ad blocking, or sharing/importing `C:\YoutubeTV`.
- Settings screen, UA selector, profile manager, parental controls, casting, tray mode, cross-platform builds, code signing, paid update services, silent update installation, or telemetry.
- Treating a successful manual YouTube session as evidence that automated host tests passed.

## Verification strategy

- Use .NET unit tests for deterministic state, paths, release metadata parsing, and update decisions.
- Use Playwright's WebView2 CDP connection only against a local controlled page to exercise the actual host process: launch, fullscreen state, keyboard focus, Esc transitions, Alt+F4, native close, and isolated data directories.
- Run tests with a temporary `WEBVIEW2_USER_DATA_FOLDER`; assert it is not `C:\YoutubeTV` and does not resolve inside any Chrome profile path.
- Validate YouTube only as an explicitly non-automated, local family-user checklist: first sign-in in the app profile; successful TV UI load; subscription playback where entitled; close/relaunch retains only that app profile. Never place accounts, cookies, screenshots containing account data, or playback recordings in the repository.
- Persist test logs, JUnit/TRX output, and screenshots for controlled local pages under `artifacts/` (gitignored); store release build checksums with the GitHub Release assets.

## Execution strategy

Wave 1 establishes the project, application boundaries, host navigation state, and reliable native window lifecycle. Wave 2 adds repeatable host QA and packaging/updating. Tasks 1–4 can proceed after task 1; tasks 5–7 depend on the corresponding host and package surfaces. Do not attempt a production release until every final-verification task approves.

## Todos

- [x] 1. Establish the C# WinUI 3 shell and deterministic project boundaries
  - References: `AGENTS.md`; `docs/agents/domain.md`; `.omo/drafts/youtube-tv-winui-shell.md`; Microsoft WebView2 user-data-folder documentation.
  - Implementation: create the .NET 8 WinUI 3 solution and application project; pin the Windows App SDK, WebView2 SDK, test SDK, Playwright, and Velopack dependencies in one documented dependency manifest; target Windows 10 version 1903+ x64; add `.gitignore` entries for `artifacts/`, local user-data folders, Velopack output, and test credentials.
  - Acceptance criteria: a clean checkout restores and builds the WinUI project without creating a Chrome/Edge profile in the repository; dependency versions are explicit; no executable, package, credential, cookie, or generated profile is tracked.
  - Happy-path QA: run `dotnet restore`, `dotnet build -c Release`, and `dotnet test -c Release`; save full command output to `artifacts/qa/01-build.log` and test result XML to `artifacts/qa/01-tests.trx`.
  - Failure-path QA: run the repository secret/profile scan against a seeded fake `C:\YoutubeTV` path reference; expect the scan to fail if application code attempts to import or read it; save output to `artifacts/qa/01-profile-boundary.log`.
  - Commit: `chore: scaffold WinUI YouTube TV shell`.
  - Recommended task executor category: unspecified-high — creates the multi-file Windows application and its build/test boundary.

- [x] 2. Preserve shortcut compatibility without sharing the Chrome profile
  - References: `Youtube TV.lnk`; session `ses_f915f4f41ffex4gMkrzXojWpta` (shortcut findings); Microsoft WebView2 user-data-folder documentation; `.omo/drafts/youtube-tv-winui-shell.md`.
  - Implementation: add a read-only development utility/test fixture that extracts the exact `--user-agent` argument and target URL from `Youtube TV.lnk`; encode the extracted literal as an application constant with a regression test; initialize WebView2 with a dedicated LocalAppData user-data folder and the fixed UA before first navigation; use `https://www.youtube.com/tv` as the only host-defined home URL; record that the endpoint is an unsupported compatibility assumption rather than a provider guarantee.
  - Acceptance criteria: the runtime UA exactly equals the shortcut's extracted argument; the first WebView2 navigation uses the fixed home URL; the configured user-data folder is under the application LocalAppData location and never equals, nests in, or copies from `C:\YoutubeTV`; the release checklist names a failed TV-UI compatibility check as a no-release condition and forbids a content-injection workaround.
  - Happy-path QA: execute the shortcut metadata test and a WebView2 startup test that writes the captured UA, initial URL, and resolved data-folder path to `artifacts/qa/02-webview-config.json`.
  - Failure-path QA: set the configured user-data folder to `C:\YoutubeTV` in a test-only configuration; expect validation to reject startup before WebView2 initializes; save the rejection message to `artifacts/qa/02-profile-rejection.log`.
  - Commit: `feat: isolate WebView2 profile and preserve TV user agent`.
  - Recommended task executor category: unspecified-high — combines Windows shortcut metadata, WebView2 initialization, and security-sensitive data isolation.

- [x] 3. Implement the explicit host navigation and single-instance state model
  - References: `.omo/drafts/youtube-tv-winui-shell.md`; task 2's fixed home URL contract; Playwright WebView2 documentation.
  - Implementation: implement an explicit shell state that records whether the host last navigated to the fixed home URL; intercept Esc at the host boundary without inspecting or injecting into YouTube DOM; from any non-home state, navigate directly to the fixed home URL and record home only after navigation completes; at home, handle Esc as a no-op; enforce a single instance that foregrounds the existing window on a second launch.
  - Acceptance criteria: Esc never depends on a YouTube selector or DOM shape; first Esc from a controlled non-home URL produces exactly one direct home navigation; a second Esc at home leaves URL and window intact; a second process launch does not create a second main window.
  - Happy-path QA: use the controlled local test page through Playwright/CDP, send Esc twice, and write URL/navigation-event/window-count assertions to `artifacts/qa/03-esc-state.json`.
  - Failure-path QA: simulate a failed home navigation; expect the shell to remain non-home, show a host error state, and not falsely mark itself home; save evidence to `artifacts/qa/03-home-navigation-failure.json`.
  - Commit: `feat: add deterministic TV home and Esc navigation state`.
  - Recommended task executor category: unspecified-high — requires coordinated host state, WebView events, and process lifecycle behavior.

- [x] 4. Make fullscreen and native closing reliable with WebView2 focus
  - References: MicrosoftEdge/WebView2Samples `SampleApps/WebView2_WinUI3_Sample`; WebView2 `AcceleratorKeyPressed` documentation; `.omo/drafts/youtube-tv-winui-shell.md`.
  - Implementation: launch the main window fullscreen without kiosk mode; keep Windows' native close route unblocked; dispose WebView2 during window close; make Alt+F4 close the app even while WebView2 owns keyboard focus; preserve Esc handling from task 3 without turning Esc into a close shortcut; provide a visible native close surface whenever the app is not fullscreen.
  - Acceptance criteria: on startup the main window is fullscreen; Alt+F4 terminates the process from both host and focused-WebView2 contexts; the native close command terminates it; Esc cannot terminate it; WebView2 disposal runs exactly once per close.
  - Happy-path QA: attach Playwright over CDP to the launched app, focus WebView2, send Alt+F4, and assert process exit plus cleanup log in `artifacts/qa/04-alt-f4-webview-focus.json`.
  - Failure-path QA: exercise Esc at home and a close event during a pending controlled-page navigation; assert the former keeps the process alive and the latter exits without an unhandled exception; save `artifacts/qa/04-close-edge-cases.json`.
  - Commit: `feat: support fullscreen TV shell with reliable native exit`.
  - Recommended task executor category: deep — native keyboard routing, fullscreen behavior, and WebView lifecycle must be proven together.

- [x] 5. Build the host-only automated QA harness and manual validation boundary
  - References: Playwright WebView2 documentation; `.omo/drafts/youtube-tv-winui-shell.md`; tasks 2–4.
  - Implementation: configure the app's test launch to use a unique temporary user-data folder and remote-debugging port; add Playwright fixture code to connect over CDP; host a local deterministic test page for all browser automation; add tests for startup configuration, app/profile isolation, Esc state transitions, fullscreen, Alt+F4, native close, no-network shell error, missing WebView2 runtime error, and single-instance behavior; write a non-automated local checklist for YouTube sign-in/playback.
  - Acceptance criteria: no automated test navigates to a YouTube domain; each test owns and cleans a unique test profile; every specified host behavior has a passing happy path and intentional failure assertion; the manual checklist is clearly labelled non-automated and excludes repository evidence containing account data.
  - Happy-path QA: run `dotnet test -c Release --logger trx`; produce `artifacts/qa/05-host-tests.trx`, CDP logs, and controlled-page screenshots.
  - Failure-path QA: run the test suite with the local test server unavailable and with the WebView2 runtime check forced absent; assert explanatory host errors rather than crashes; store `artifacts/qa/05-runtime-network-failures.log`.
  - Commit: `test: automate WebView2 host behavior without YouTube automation`.
  - Recommended task executor category: deep — integrates native process launch, CDP, temporary browser profiles, and negative-path testing.

- [x] 6. Package the app and implement confirmed Velopack updates from GitHub Releases
  - References: Velopack C# integration and `GithubSource` documentation; `.omo/drafts/youtube-tv-winui-shell.md`; task 1 dependency manifest.
  - Implementation: configure Velopack's Windows installer and GitHub Releases source; on app startup check for a newer release without downloading; show target version and release notes; only after an explicit user confirmation download and apply the update via restart; retain the current version and present a retryable error if check, download, checksum, or apply preparation fails; do not add background install, telemetry, channels UI, or code-signing logic.
  - Acceptance criteria: no update download/apply occurs without a positive confirmation; GitHub source and repository identifier are configuration values, not hard-coded secrets; an update failure leaves the current version runnable; release assets contain checksums and version metadata.
  - Happy-path QA: serve a local Velopack-compatible release feed with a higher test version, assert version/notes dialog and confirmation-gated download, then capture restart/apply receipt in `artifacts/qa/06-update-success.json`.
  - Failure-path QA: serve an invalid checksum and an unreachable feed; assert current version remains running and the UI exposes a non-sensitive retry message; save `artifacts/qa/06-update-failures.json`.
  - Commit: `feat: add confirmed GitHub Releases updates`.
  - Recommended task executor category: unspecified-high — packaging, updater state, and release integrity span multiple project boundaries.

- [x] 7. Add family-release documentation and reproducible release checks
  - References: `docs/agents/issue-tracker.md`; `.omo/drafts/youtube-tv-winui-shell.md`; Velopack release documentation; tasks 5–6 outputs.
  - Implementation: document local build, installer creation, GitHub Release publication, checksum upload, update confirmation, rollback/failure recovery, dedicated-profile/privacy boundary, unsupported Leanback compatibility risk, and manual YouTube validation; document the unsigned/SmartScreen risk as accepted; add a CI/release workflow that builds, runs host-only tests, packages Velopack assets, emits SHA-256 checksums, and refuses publication when tests fail.
  - Acceptance criteria: a family maintainer can create and publish a release without credentials in repository files; all instructions state that installers are unsigned and require user confirmation; manual validation is clearly separate from CI success; the documented manual TV-UI check is a no-release gate with no workaround beyond stopping the release; publication cannot proceed after failed build/test/checksum steps.
  - Happy-path QA: run the release workflow in dry-run mode and verify package, checksum, test result, and release-notes artifacts under `artifacts/release/`.
  - Failure-path QA: corrupt a staged package after checksum generation; expect the dry-run publication guard to fail and retain the prior published release untouched; store `artifacts/qa/07-checksum-guard.log`.
  - Commit: `docs: document family release and update recovery`.
  - Recommended task executor category: unspecified-high — coordinates CI, packaging provenance, and operational documentation.

## Final verification wave

- [x] F1. Audit every implemented behavior against the approved scope and Must-NOT-Have list
  - Verify the implementation and release assets contain no settings UI, Chrome-profile sharing, credentials, YouTube automation, DRM/ad modification, telemetry, signing, cross-platform target, or paid service.
  - Run: scope audit script plus `dotnet test -c Release`; save findings to `artifacts/final/F1-scope-audit.md`.
  - Recommended task executor category: unspecified-high — requires whole-plan compliance review.

- [x] F2. Review C# / WinUI / WebView2 lifecycle quality and dependency safety
  - Verify exactly-once WebView2 disposal, native-close behavior, test-profile cleanup, explicit dependency versions, and no unhandled exceptions in all negative-path logs.
  - Run: `dotnet build -c Release`, static analysis, and the complete host test suite; save output to `artifacts/final/F2-quality.log`.
  - Recommended task executor category: unspecified-high — requires cross-module code quality review.

- [~] F3. Execute real host QA from a packaged build without touching YouTube
  - Install the packaged test build in an isolated Windows account/profile; exercise fullscreen startup, WebView focus, Alt+F4, native close, Esc non-home, Esc at home, second launch, and update-decline behavior against the controlled local page.
  - Save process, screenshot, and assertion evidence to `artifacts/final/F3-packaged-host-qa/`.
  - Recommended task executor category: unspecified-high — validates the real packaged desktop surface.

- [~] F4. Validate release/update integrity and record the external-service boundary
  - Verify a higher local release feed performs confirmation-gated update and that an invalid checksum keeps the existing version runnable; verify documentation states the separate family-user manual YouTube checklist and forbids storing its evidence in the repository.
  - Save receipt to `artifacts/final/F4-update-integrity.json` and `artifacts/final/F4-boundary-audit.md`.
  - Recommended task executor category: unspecified-high — validates release contract and scope boundary together.

## Commit strategy

1. `chore: scaffold WinUI YouTube TV shell`
2. `feat: isolate WebView2 profile and preserve TV user agent`
3. `feat: add deterministic TV home and Esc navigation state`
4. `feat: support fullscreen TV shell with reliable native exit`
5. `test: automate WebView2 host behavior without YouTube automation`
6. `feat: add confirmed GitHub Releases updates`
7. `docs: document family release and update recovery`

## Success criteria

- The Windows packaged app launches fullscreen into the fixed YouTube TV URL using the exact legacy shortcut UA and a new app-owned profile.
- With focus inside WebView2, Alt+F4 and the native close command close the app cleanly; the app is not kiosk-mode dependent.
- Esc from any non-home host state directly navigates to the fixed home URL; a subsequent Esc at home changes neither navigation nor window state.
- Automated tests prove host-only behavior without contacting YouTube; manual account validation is explicit, local, and credential-free from the repository.
- A GitHub Releases update is shown, downloaded, and installed only after user confirmation; failure leaves the previous version runnable.

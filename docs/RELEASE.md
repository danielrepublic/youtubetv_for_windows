# Family Release Guide — YouTubeTvShell

A step-by-step guide for a family maintainer with no repository secrets.
Follow it end to end to build, package, publish, install, and recover
a YouTubeTvShell release. No credentials or tokens live in this
repository — GitHub authentication happens in your browser / `gh` login
only, never in a file.

On-disk truth this guide is pinned to (verify before releasing):

- Velopack **1.2.0** (`src/YouTubeTvShell/YouTubeTvShell.csproj`,
  `DEPENDENCIES.md`). The `vpk` CLI version must match: `1.2.0`.
- Unpackaged app: `WindowsPackageType=None` in the app csproj.
  Distribution is Velopack, not MSIX.
- Package identifier `YouTubeTvShell`
  (`src/YouTubeTvShell/UpdateConfig.cs`, `UpdateConfig.PackageId`).
  The `vpk pack --packId` value must equal it.
- Update source: public-read GitHub Releases via
  `Velopack.Sources.GithubSource` (`src/YouTubeTvShell/VelopackWiring.cs`).
  No access token is used (`accessToken: null`).
- Update decision state machine with confirmation latch:
  `src/YouTubeTvShell/UpdateService.cs`
  (`Decide` / `ConfirmUpdate` / `RequireConfirmation`).
- Release feed layout: `releases.win.json` (JSON array of VelopackAsset
  objects) plus `YouTubeTvShell-<version>-full.nupkg`
  (see `tests/YouTubeTvShell.Tests/UpdateFeedTests.cs`).
- Single-instance guard: named Mutex `YouTubeTvShell-SingleInstance`
  (`src/YouTubeTvShell/EscHandling.cs`).
- App-owned WebView2 profile: `%LocalAppData%\YouTubeTvShell`.
  It never reads, copies, or shares the legacy `C:\YoutubeTV`
  Chrome profile directory.

## Step 0 — Point the updater at the real repository (required once)

`src/YouTubeTvShell/UpdateConfig.cs` currently ships with:

```csharp
public const string GitHubRepoOwner = "OWNER_PLACEHOLDER";
public const string GitHubRepoName = "youtubetv_for_windows";
```

Before the first release, replace `OWNER_PLACEHOLDER` with the real
GitHub user/org that owns this repository, rebuild, and re-run the
filtered test suite. The updater URL is derived as
`https://github.com/<owner>/youtubetv_for_windows`; with the
placeholder still in place, update checks hit a nonexistent repository
and fail with the retryable "could not check" message instead of
finding releases. Do not invent an owner — use the account that will
actually host the GitHub Releases.

## 1. Prerequisites

- Windows 10 version 1903+ or Windows 11, x64.
- .NET 8 SDK (`8.0.420` per `DEPENDENCIES.md`; any 8.0.x works).
  If `dotnet` resolves to a user-local install missing the x64 runtime,
  set `$env:DOTNET_ROOT = "C:\Program Files\dotnet"` first.
- Velopack CLI `vpk` at exactly **1.2.0** (must match the NuGet pin):
  `dotnet tool install -g vpk --version 1.2.0`
  (or `dnx vpk@1.2.0`). No other version.
- WebView2 Runtime on the install/test machine (Evergreen installer
  from Microsoft). The app shows a host error instead of crashing when
  it is absent, but playback obviously cannot work without it.
- A GitHub account with permission to create Releases in the target
  repository. Authenticate with `gh auth login` or the GitHub web UI —
  never paste a token into any repo file or workflow file.

## 2. Local Release build

From the repository root:

```powershell
$env:DOTNET_ROOT = "C:\Program Files\dotnet"
dotnet restore YouTubeTvShell.sln
dotnet build YouTubeTvShell.sln -c Release
```

Expect 0 warnings / 0 errors. The app binary lands under
`src/YouTubeTvShell/bin/x64/Release/net8.0-windows10.0.19041.0/win-x64/`.

## 3. Run the host-only tests (CI gate, windowless)

```powershell
$env:DOTNET_ROOT = "C:\Program Files\dotnet"
dotnet test YouTubeTvShell.sln -c Release --no-build `
  --filter "FullyQualifiedName!~LiveApp"
```

Expect 84 passed / 0 failed in ~2 seconds with zero app windows.
The 4 `LiveApp_*` tests in
`tests/YouTubeTvShell.Tests/HostBehaviorTests.cs` launch the real
`YouTubeTvShell.exe` over CDP with 90-second waits each — they are
explicitly excluded here and run only as a deliberate manual batch on
an interactive desktop with the WebView2 runtime (see section 8 and
`.github/workflows/release.yml` for the documented reason).
A red test run blocks the release: do not package, do not publish.

## 4. Package with vpk

```powershell
vpk pack --packId YouTubeTvShell --packVersion <VERSION> `
  --packDir <path-to-Release-publish-output> --mainExe YouTubeTvShell.exe `
  --outputDir artifacts/release
```

- `--packId` must be exactly `YouTubeTvShell` (matches
  `UpdateConfig.PackageId`; the feed tests assert this).
- `--packVersion` is the release version (e.g. `1.0.0`).
  Tag the GitHub Release with the same version as `v<VERSION>`
  (e.g. `v1.0.0`) so the CI workflow ref matches.
- The pack output is a Velopack feed directory:
  `releases.win.json` + `YouTubeTvShell-<VERSION>-full.nupkg`
  (+ installer stub when configured). This is the same layout the
  `UpdateFeedTests` local-feed harness serves from a temp directory.

## 5. Emit SHA-256 checksums

```powershell
Get-ChildItem artifacts/release/* -Include *.nupkg, *.exe |
  ForEach-Object { Get-FileHash $_.FullName -Algorithm SHA256 } |
  Format-Table -AutoSize | Out-File artifacts/release/SHA256SUMS.txt
```

Publish `SHA256SUMS.txt` alongside the assets so any machine can verify
`Get-FileHash <asset> -Algorithm SHA256` against the recorded value
before installing.

## 6. Create the GitHub Release and upload assets

1. On GitHub: Releases → Draft a new release → tag `v<VERSION>`
   (same version packed above), title, and release notes describing
   what changed.
2. Upload every file from `artifacts/release/`:
   `releases.win.json`, the `*-full.nupkg`, the installer (if produced),
   and `SHA256SUMS.txt`.
3. Publish the release. The in-app updater (`GithubSource` against the
   repository URL from Step 0) discovers it on next launch.
4. No secrets are involved: the feed is public-read; the workflow and
   docs contain no tokens by design.

## 7. Install on the family machine

1. Download the installer asset from the GitHub Release page.
2. Run it. See the unsigned-installer box below first.
3. Launch YouTube TV from the installed shortcut. First launch creates
   `%LocalAppData%\YouTubeTvShell` — the app's own WebView2 profile.
4. Sign in inside the app. The profile stays in the app folder only.

> **Accepted risk: installers are UNSIGNED.**
> There is no code signing in this project by design (plan scope
> excludes it). Windows SmartScreen will warn that the publisher is
> unknown. This is accepted: confirm the prompt only for builds you
> produced yourself from this repository (matching version + checksum),
> never for a file forwarded by a third party. Do not promise signing
> to family members — tell them to expect the warning every update.

## 8. Update-confirmation walkthrough

Startup behavior (`VelopackWiring.CheckForUpdatesOnStartupAsync`):

1. On launch the app checks GitHub Releases in the background
   (10-second timeout, `UpdateConfig.CheckTimeoutSeconds`) without
   blocking the window and without downloading anything.
2. If a newer release exists, a dialog titled **"Update available"**
   shows the target version plus the release notes as plain text
   (`App.BuildPromptText`, HTML stripped) with **Confirm** / **Cancel**.
3. **Confirm** records the latch (`UpdateService.ConfirmUpdate()`) and
   only then downloads and applies via restart
   (`DownloadUpdatesAsync` → `ApplyUpdatesAndRestart`).
4. **Cancel**, closing the dialog, or any headless/test context
   downloads zero bytes — the latch (`RequireConfirmation`) throws for
   programmatic callers that skip confirmation, and the app keeps
   running the current version.
5. Any failure (unreachable feed, bad checksum, timed-out check,
   failed apply preparation) leaves the current version runnable and
   shows a retryable message with no stack traces, paths, or tokens
   (`UpdateService.UserFacingError`). Retry on next launch.

## 9. Rollback / failure recovery

- **Bad update offered / download fails / checksum fails:** decline or
  let it fail; the current version keeps running. Retry on next launch
  once the feed is healthy.
- **New version misbehaves after restart:** reinstall the prior
  version's installer from its GitHub Release page (releases are never
  deleted or overwritten — each version keeps its own assets), then
  verify with section 8 that no further update is forced.
- **Corrupt local install:** uninstall, delete nothing outside
  `%LocalAppData%\YouTubeTvShell` unless doing a clean-profile reset
  (which signs you out), reinstall the known-good version.
- CI enforces the same fail-closed rule: any red build/test/checksum
  step refuses publication, so a broken build never becomes the
  "latest" release the updater offers.

## 10. Dedicated-profile / privacy boundary

- The app owns `%LocalAppData%\YouTubeTvShell` and nothing else.
  It never reads, copies, imports, or writes the legacy `C:\YoutubeTV`
  directory or any Chrome/Edge profile (enforced by validation before
  WebView2 initializes; covered by `ProfileBoundaryTests`).
- Automated tests use unique temporary profiles and never navigate to
  a YouTube domain. Sign-in and playback are human-only.
- Never store accounts, cookies, tokens, screenshots containing
  account data, or playback recordings in the repository, issues,
  PRs, releases, or chat logs. Manual validation is recorded as
  pass/fail per checkbox only.

## 11. Unsupported Leanback compatibility risk + no-release gate

`https://www.youtube.com/tv` with the legacy PS4 Leanback user agent is
an **unsupported external compatibility assumption, not a provider
contract**. YouTube may change or discontinue the TV UI at any time,
and no app update can fix that from our side.

- If the manual TV-UI load check (section 12) fails, **STOP THE
  RELEASE**. A failed compatibility check is a release blocker.
- No workaround that modifies YouTube, injects content/DOM, blocks
  ads, or bypasses its controls is ever permitted. The only acceptable
  action is to stop the release until the endpoint works again.

## 12. Manual YouTube validation (human-only, separate from CI)

CI success never substitutes for this checklist, and passing this
checklist never substitutes for CI. Before publishing, a family member
on the target machine completes:

- [docs/manual-youtube-checklist.md](manual-youtube-checklist.md) —
  first sign-in in the app profile, TV-UI load, entitled-subscription
  playback, close/relaunch profile retention, and the Leanback
  no-release gate above.

That file is the single source of truth for the procedure; it is
linked here, not duplicated, so the two cannot drift apart.
Record only pass/fail per checkbox (e.g. "2026-09-05: 1–4 pass on
build 1.0.0"). Its no-release gate (section 11) applies to every
release without exception.

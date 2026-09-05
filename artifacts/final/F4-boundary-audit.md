# F4 Boundary Audit — 2026-09-05

## Verdict: REJECT

This is a fresh, local-only F4 verification. The dated evidence directory is
`artifacts/final/F4-run-20260905/`. No YouTube page, sign-in, playback, real
GitHub release, external release feed, credential, or repository mutation was
used. `ApplyUpdatesAndRestart` was not invoked because it would replace and
restart the verifier process; this report makes no claim of a real installed
version switch.

The prior F4 artifacts were read first and were `REJECT`. Their reported
checksum and installer-upload blockers were independently re-checked below;
the owner placeholder and human-only gate remain blocked.

## Fresh command evidence

All .NET commands used the repository-documented global runtime selection:

```powershell
$env:DOTNET_ROOT = "C:\Program Files\dotnet"
dotnet restore YouTubeTvShell.sln
dotnet build YouTubeTvShell.sln -c Release --no-restore
dotnet test YouTubeTvShell.sln -c Release --no-build `
  --filter "FullyQualifiedName~UpdateFeedTests|FullyQualifiedName~UpdateDecisionTests|FullyQualifiedName~UpdatePromptTests" `
  --logger "trx;LogFileName=F4-update-focused.trx" `
  --results-directory artifacts/final/F4-run-20260905/test-results
```

Observed exit codes were restore `0`, build `0`, and focused test `0`. The fresh
TRX contains 29 passed, 0 failed, and 0 not executed tests:
`artifacts/final/F4-run-20260905/test-results/F4-update-focused.trx`.

The documented host-only release gate was also run once with the same global
runtime selection. It returned `0` with 84 passed, 0 failed, and 0 not
executed; the four `LiveApp_*` tests were excluded as the documented
interactive/manual gate. Evidence: `host-results/F4-host-only.trx` and
`host-only-test-summary.json`.

The fresh package command was:

```powershell
$env:DOTNET_ROOT = "C:\Program Files\dotnet"
vpk pack --packId YouTubeTvShell --packVersion 0.0.1-f4-verify `
  --packDir src/YouTubeTvShell/bin/x64/Release/net8.0-windows10.0.19041.0/win-x64 `
  --mainExe YouTubeTvShell.exe `
  --outputDir artifacts/final/F4-run-20260905/package
```

Velopack CLI `1.2.0` returned `0` and produced the installer, full package,
portable package, `assets.win.json`, `RELEASES`, and `releases.win.json`.

## Update integrity

| Check | Result | Fresh evidence |
|---|---|---|
| No download before positive confirmation | **PASS** | `VelopackWiring.cs:118-125` returns for Cancel/close/headless prompt; `UpdateService.cs:58-103` enforces the latch; actual local decline produced no full package. |
| Higher local feed check/download | **PASS with scope** | `actual-local-feed.json` shows `SimpleFileSource` returned the higher asset and `DownloadUpdatesAsync` produced the full nupkg after the harness positive decision. Apply/restart was intentionally not run. |
| Explicit decline | **PASS** | `actual-local-feed.json` shows `userDecision=declined`, `downloadCalled=false`, no full package after check, and an unchanged current-version marker. |
| Invalid checksum | **PASS** | Local feed download raised `ChecksumFailedException`; no full package was installed and current version was preserved. The focused TRX also passes the invalid-checksum and non-sensitive-message tests. |
| Unreachable feed | **PASS** | An unused loopback port raised `HttpRequestException`; no full package was downloaded and current version was preserved. `UpdateService.UserFacingError` maps the error to a retryable, non-sensitive message. |
| Failed apply/download preservation | **PASS at exercised failure seam** | Download/checksum failures leave no new full package and retain the current-version state. Actual apply interruption is not claimed; `mid_operation_interrupts` is not triggered because ApplyUpdatesAndRestart was not run. |

The actual local-feed harness was temporary and used only the fresh package,
`SimpleFileSource`, `SimpleWebSource` on loopback, and `TestVelopackLocator`.
It left no harness or feed work directory.

## Package and checksum audit

Fresh publication assets and independent SHA-256 results:

| Asset | Bytes | SHA-256 |
|---|---:|---|
| `YouTubeTvShell-win-Setup.exe` | 17,095,749 | `ED06102AD6979BE6BC7D08E7D04AFEFB2ACEB5D8A8DE954ACBF1B35E90D9C6F4` |
| `YouTubeTvShell-0.0.1-f4-verify-full.nupkg` | 12,634,181 | `1DF7398370779F721CB9721B34C160CE7130E0D64044F275A4EE233B221B88D8` |

`artifacts/final/F4-run-20260905/package/SHA256SUMS.txt` covers exactly these
two publishable asset types. `sha256-records.json` records matching
`Get-FileHash` and independent `certutil -hashfile` values. The generated
`releases.win.json` also matches the full nupkg SHA-256 and byte count.

A second fresh guard copied the package to a temporary directory, corrupted
only the copy after manifest generation, and ran a child verifier. The child
returned exit code `1` with `CHECKSUM MISMATCH`; the source nupkg hash was
identical before and after. See `corruption-guard.log`.

## Workflow upload contract

The current `.github/workflows/release.yml` has both checksum selection and
release upload coverage:

- checksum step selects root `*.nupkg` and `*.exe` files;
- `action-gh-release` uploads `artifacts/release/*.nupkg`;
- it uploads `artifacts/release/*.exe` (including the setup installer);
- it uploads `artifacts/release/releases.win.json`;
- it uploads `artifacts/release/SHA256SUMS.txt`.

This clears the prior F4 installer-upload finding. The exact current source
contract is also recorded in `feed-and-workflow-summary.json`.

## Secret and untrusted-text boundary

- `VelopackWiring.cs` passes `accessToken: null` to `GithubSource`.
- Release notes are converted by `ToPlainText`: tags are stripped and entities
  decoded before the `ContentDialog` receives the text. No HTML control or
  script execution path is used.
- `UpdatePromptTests.BuildPromptText_HtmlNotes_IncludesVersionAndPlainText`
  passed in the fresh TRX.
- The relevant source, docs, workflow, and update tests had no
  credential-like token or private-key matches in the static scan.
- The release workflow has no repository secret reference; the documented
  release authentication boundary is browser/`gh` outside repository content.

This is a plain-text rendering boundary, not permission to trust release-note
instructions. Notes are displayed as data only.

## Manual YouTube boundary — unresolved human gate

`docs/manual-youtube-checklist.md` remains explicitly non-automated and
human-only. It requires the target-machine user to validate sign-in, TV UI
load, entitled playback, and app-profile retention, recording only checkbox
pass/fail outside repository evidence. This worker did not navigate to YouTube
and did not create account, cookie, screenshot, or playback evidence.

The checklist and `docs/RELEASE.md` both make a failed Leanback TV-UI load a
**STOP THE RELEASE** condition. They prohibit DOM/content injection, ad
blocking, DRM changes, and control bypasses. Automated host tests cannot
substitute for this gate. Status: **BLOCKED_HUMAN_GATE**.

## GitHub owner — unresolved placeholder blocker

`src/YouTubeTvShell/UpdateConfig.cs:11` still reads:

```csharp
public const string GitHubRepoOwner = "OWNER_PLACEHOLDER";
```

The delegated read-only owner research found that the local remote proposes
`danielrepublic`, but the corresponding GitHub repository page and REST
repository endpoint returned 404. That does not authoritatively establish the
owner that will host releases, so the value was intentionally not changed.
See `artifacts/research/F4-owner-research.md`.

**Required input:** provide the authoritative GitHub repository URL (or
owner/org plus repository name) that will host the public release assets, and
confirm that it is reachable under that owner. Do not infer it from the
Windows username, local path, or project name.

## Adversarial-class disposition

| Class | Result | Evidence/disposition |
|---|---|---|
| `malformed_input` | **PASS** | Bad feed SHA-256 and invalid-checksum tests. |
| `untrusted_external_text` / prompt injection | **PASS with scope** | Plain-text-only notes path and fresh HTML/plain-text test; no execution path. |
| `cancel_resume` | **PASS with scope** | Local decline, latch reset, and retry-message tests; no stale confirmation carries across a new check. |
| `stale_state` | **PASS** | Fresh dated TRX/package/feed/manifest; prior artifacts were comparison-only. |
| `dirty_worktree` | **PASS** | Product source, tests, workflow, and pre-existing evidence were preserved; only F4 evidence was written. |
| `long_external_commands` | **PASS** | Restore/build/test/package were bounded to 600 seconds; local evidence commands to 120 seconds; no external service was contacted. |
| `misleading_success_output` | **PASS** | Independent `certutil` hashes match the manifest, Get-FileHash, and feed metadata. |
| `mid_operation_interrupts` | **NOT APPLICABLE / NOT TRIGGERED** | Applying/restarting was intentionally not run; failed download/checksum paths were exercised and preserved state. |
| `flaky-tests` | **NOT APPLICABLE** | Live GUI/CDP tests are manual-gated and excluded; no flaky pass was substituted. |

## Cleanup receipt

`artifacts/final/F4-run-20260905/cleanup-receipt.json` records zero remaining
YouTubeTvShell processes, Velopack test feeds, local-feed harness/work
directories, package-guard copies, and stale host-test temp profiles. The
dated F4 directory is intentionally retained as evidence, not a temporary test
resource.

## Release decision

The former checksum-coverage and workflow-installer findings are cleared by
fresh evidence. F4 remains **REJECT** because:

1. `GitHubRepoOwner` is still `OWNER_PLACEHOLDER` and the actual release owner
   was not authoritatively identified; and
2. the required human-only YouTube checklist has not been completed outside
   repository evidence.

Do not publish or approve F4 until both gates are resolved. A failed Leanback
UI check remains an automatic no-release outcome.

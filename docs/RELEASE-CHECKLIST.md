# Pre-Publish Release Checklist — no-release gates

Complete every gate before creating the GitHub Release. Any unchecked
gate stops the release — there are no waivers and no workarounds.

## Automated gates (must all be green)

- [ ] `dotnet build YouTubeTvShell.sln -c Release` — 0 warnings, 0 errors.
- [ ] Filtered host-only suite passes windowlessly:
  `dotnet test YouTubeTvShell.sln -c Release --no-build
  --filter "FullyQualifiedName!~LiveApp"` — 84 passed, 0 failed.
  (The 4 `LiveApp_*` tests are manual-gated: they launch the real GUI
  with 90-second waits and need an interactive desktop + WebView2
  runtime, which CI runners do not have.)
- [ ] `UpdateConfig.GitHubRepoOwner` no longer reads
  `OWNER_PLACEHOLDER` — replaced with the real release owner
  (see `docs/RELEASE.md` Step 0).
- [ ] `vpk pack --packId YouTubeTvShell` output present under
  `artifacts/release/`: `releases.win.json` +
  `YouTubeTvShell-<version>-full.nupkg`.
- [ ] `SHA256SUMS.txt` emitted over the packed assets and verified
  (re-hash matches before upload).
- [ ] No credentials, tokens, cookies, or account data in the docs,
  workflow, or release notes.

## Manual gates (human-only, separate from CI)

- [ ] [docs/manual-youtube-checklist.md](manual-youtube-checklist.md)
  completed on the target machine: sign-in, TV-UI load,
  entitled-playback, close/relaunch profile retention — pass/fail only,
  no account evidence stored.
- [ ] Leanback no-release gate: if the TV UI did not load,
  **the release stops here**. No content-injection, ad-blocking, DOM,
  or DRM workaround is permitted — stopping is the only action.
- [ ] Unsigned-installer expectation set: the family member installing
  knows SmartScreen will warn and confirms only a self-produced build
  (version + checksum match).

## Publication rule

Publish only when every box above is checked. CI enforces the
automated half fail-closed (see `.github/workflows/release.yml`):
any red build/test/checksum step refuses publication by job
dependency, so a broken build can never become `latest`.

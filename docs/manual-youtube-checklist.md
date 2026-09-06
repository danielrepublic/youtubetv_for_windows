# Manual YouTube Validation Checklist — NON-AUTOMATED

> **NON-AUTOMATED local family-user checklist.** This procedure is performed by a
> human on their own machine with their own account. It is NEVER automated, and
> passing it is NEVER evidence that automated host tests passed (nor vice versa).
> **NO accounts, cookies, screenshots containing account data, or playback
> recordings ever enter the repository** — do not attach any of them to issues,
> PRs, releases, or chat logs.

## Preconditions

- Install the unsigned test/release build on the family machine.
- The app owns its WebView2 profile under `%LocalAppData%\YouTubeTvShell`.
  It must never read, copy, or share `C:\YoutubeTV` or any Chrome/Edge profile.
- You understand installers are **unsigned**: Windows SmartScreen will warn.
  This is an accepted risk — confirm the publisher prompt only for builds you
  produced yourself (see Task 7 release docs).

## Steps

1. **First sign-in in the app profile**
   - [ ] Launch the app, navigate to sign-in, and sign in with the family account.
   - [ ] Confirm the account avatar/profile appears in the TV UI.

2. **TV UI load check**
   - [ ] The leanback TV interface loads and is navigable with keyboard
         (arrows/Enter/Esc behave as the TV UI expects).

3. **Entitled-subscription playback check**
   - [ ] Play a video the account is entitled to and confirm video + audio play.
   - [ ] Confirm no DRM-error or playback-error screen persists.

4. **Close/relaunch retains app profile only**
   - [ ] Close the app with Alt+F4, relaunch, and confirm you are still signed in.
   - [ ] Confirm nothing was imported from, or written to, `C:\YoutubeTV` or any
         browser profile.

## No-release gate: unsupported Leanback endpoint

The `https://www.youtube.com/tv` (PS4 Leanback) endpoint is an **unsupported
external compatibility assumption**, not a provider contract. YouTube may
change or discontinue the TV UI at any time.

- [ ] If the TV UI does **not** load in this checklist, **STOP THE RELEASE**.
      A failed TV-UI compatibility check is a release blocker, not a defect.
- [ ] **No workaround that modifies YouTube, injects content/DOM, blocks ads,
      or bypasses its controls is ever permitted.** The only acceptable action
      is to stop the release until the endpoint works again.

## Evidence rules

- Record only pass/fail per checkbox (e.g. "2026-09-05: 1–4 pass on build X").
- NEVER store account identifiers, cookies, tokens, screenshots showing
  account data, or video/audio recordings in the repo, issues, or releases.

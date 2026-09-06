# youtube-tv-winui-shell - Planning Draft

- status: reviewed-and-approved
- intent: clear
- review_required: true
- classification: standard
- plan_path: .omo/plans/youtube-tv-winui-shell.md
- pending_action: hand off .omo/plans/youtube-tv-winui-shell.md to a separate execution worker

## Dual-review request

- requested_by: user
- requested_on: 2026-09-05
- round: 2
- plan_sha256: 5A947E9ABF344DD0036CF308447C45F28AF3E5A0DE3808AC82954F1827FB9C7A
- momus: round-1-approved; round-2-approved (session `ses_f90fd6e57ffem1zlwaR3bF8NfZ`)
- independent_oracle: round-1-changes-requested; round-2-approved (session `ses_f90fd6d44ffeGl6NXlNAumYbgy`)
- convergence_limit: 5 rounds; only evidence-backed blockers in approved scope qualify.

## Review ledger

- round-1 accepted blocker: the fixed PS4 Leanback endpoint is an unsupported provider compatibility assumption; document its possible loss and forbid workaround scope expansion.
- round-1 non-blocking notes: document unsigned update risk; make the profile scan implementation explicit; prove single-instance and Alt+F4 behavior through host QA.
- round-2 fix: the plan now treats a failed manual TV-UI compatibility check as a no-release gate and explicitly forbids content-injection or bypass workarounds.
- round-2 result: both Momus and Oracle approved SHA-256 `5A947E9ABF344DD0036CF308447C45F28AF3E5A0DE3808AC82954F1827FB9C7A`.

## Approved decisions

- Build a Windows-only, family-use desktop shell in C#/.NET 8 with WinUI 3 and WebView2.
- Preserve the existing shortcut's PS4 Leanback user-agent exactly; extract the literal argument from `Youtube TV.lnk` before configuring the app.
- Start fullscreen. Alt+F4 and native close always exit the app, including while WebView2 has focus.
- Esc from a non-home shell state directly navigates to `https://www.youtube.com/tv`; Esc at the recorded home state is a no-op.
- Use an app-owned WebView2 user-data folder; never read, import, or share `C:\YoutubeTV`.
- Automate only host behavior. A family user manually validates sign-in, entitlement, and DRM playback in the isolated profile.
- Package with Velopack and publish to GitHub Releases. Check automatically at startup; show version and notes; download, restart, and install only after confirmation.
- Prototype excludes settings UI, cross-platform support, code signing, paid services, telemetry, DRM/ad/content modification, and YouTube UI automation.

## Defaults adopted after gap review

- Target Windows 10 version 1903+ and Windows 11 on x64; fail clearly when the required WebView2 runtime is unavailable.
- Ship a Velopack Windows installer rather than MSIX. Unsigned-install / SmartScreen friction is an accepted family-distribution risk.
- Enforce a single app instance: a second launch foregrounds the first instance and exits.
- On update failure, retain and continue using the currently installed version; surface a clear failure message and leave retry to the next launch.

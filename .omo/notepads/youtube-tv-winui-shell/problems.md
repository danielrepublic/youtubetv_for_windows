# Problems — youtube-tv-winui-shell

Unresolved blockers and technical debt discovered during work on this plan.

_Auto-scaffolded by /ulw-execute. Append new entries below - never overwrite._

---

## [2026-09-05] Task 2-4 repair — correction, NOT a blocker

- The repair brief asserted the checked-in `Youtube TV.lnk` contains no `--user-agent`/URL args and ordered the shortcut tests reworked around that premise. Direct UTF-16LE inspection of the 2515-byte file refutes it: the args string (with the exact PS4 Leanback UA and `https://www.youtube.com/tv`) IS present. The brief's byte dump searched ASCII bytes only, which cannot match UTF-16LE StringData. No blocker exists; the real-.lnk regression tests were kept as written and pass. No human decision is required on shortcut content.
- Residual risk (Task 5 owns it): Alt+F4-while-WebView2-focused is proven only at logic level here (`CloseGuard` classifier + never-handled guarantee). If CDP testing shows the XAML WebView2 control swallowing Alt+F4, a fallback (e.g. disabling browser accelerator keys) will be needed.

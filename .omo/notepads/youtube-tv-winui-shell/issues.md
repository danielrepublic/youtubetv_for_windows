# Issues — youtube-tv-winui-shell

Problems and gotchas encountered during work on this plan.

_Auto-scaffolded by /ulw-execute. Append new entries below - never overwrite._

---

## [2026-09-05] Task 1 scaffold

- **NU1605 WebView2 downgrade**: WindowsAppSDK 2.4.0 requires WebView2 >= 1.0.3719.77. Pinning an older version (e.g., 1.0.3296.44) causes a hard restore error. Fix: pin at 1.0.4191.47 or later.
- **CS0227 unsafe code in test project**: Test projects with `net8.0-windows10.0.19041.0` TFM trigger WinRT Source Generator that emits `unsafe` code blocks. Fix: add `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` to test csproj.
- **CS0101/CS0111 duplicate Program class**: WinUI 3 XAML compiler generates `App.g.i.cs` with a `Program` class and `Main` method. A manually authored `Program.cs` conflicts. Fix: do not add `Program.cs` for WinUI 3 apps.
- **Test host runtime resolution**: Setting `RuntimeIdentifier=win-x64` in test csproj causes the test host to look for .NET runtime in `C:\Users\{user}\AppData\Local\Microsoft\dotnet` instead of the global `C:\Program Files\dotnet`. Fix: remove `RuntimeIdentifier` from test projects, or set `$env:DOTNET_ROOT = "C:\Program Files\dotnet"` before running tests.
- **WMC1509/WMC9999 XAML compiler warnings**: These appeared transiently during the build issues above and resolved once the csproj was correctly configured. No action needed if build succeeds.

---

## [2026-09-05] Task 2-4 repair

- **CS1061 `AcceleratorKeyPressed` on CoreWebView2**: the event does not exist there — it is on `CoreWebView2Controller` (confirmed in the 1.0.4191.47 .winmd), which the WinUI XAML control does not surface. Fix: no handler; document the guarantee boundary instead.
- **CS1061 `Uri` on CoreWebView2NavigationCompletedEventArgs**: the args type has no URL — use `CoreWebView2.Source` for the completed URL.
- **CS0103 `Loaded` in MainWindow ctor**: WinUI3 `Window` exposes `Content` but no `Loaded` event. Fix: subscribe to the content-root `FrameworkElement.Loaded`.
- **ProfileBoundaryTests vs comments**: the literal-path scan covers doc comments too — keep the `C:\YoutubeTV` literal out of all src/ comments.
- **WMC9999 recurrence**: again purely a cascade of the C# errors above; cleared to 0 warnings / 0 errors with no XAML change.

---

## [2026-09-05] Task 6 velopack

- **UpdateManager not IDisposable**: expected `using var mgr = new UpdateManager(source)` to compile — it did not. `UpdateManager` does not implement `IDisposable` in Velopack 1.2.0. Fix: remove `using` keyword, let GC collect.
- **CheckForUpdatesAsync no CancellationToken**: expected `CheckForUpdatesAsync(CancellationToken)` overload — does not exist. Fix: wrap in `Task.WhenAny` with `Task.Delay`.
- **VelopackAsset.NotesHTML casing**: expected `NotesHtml` (camelCase) — actual property is `NotesHTML` (capital HTML). Confirmed via reflection against the 1.2.0 DLL. Fix: use exact casing.
- **Anonymous type array inference (CS0826)**: C# cannot infer the best type for `new[] { new { a, b }, new { a, c } }` when the anonymous objects have different shapes. Fix: use `new object[]` explicitly and align property shapes.

## [2026-09-05] Live-test wall-clock hazard (orchestrator note)
- The 4 LiveApp_* tests each carry 90s window/CDP waits plus retries (Alt+F4 x3/10s, Esc x3/10s, native-close 30s, second-launch 30s): full live run needs several minutes. Short tool timeouts kill the testhost mid-flight and orphan YouTubeTvShell.exe windows that keep holding the single-instance mutex, making every later run slower. Fix: kill orphans first, run live tests in ONE bounded batch with a 10-minute timeout, never repeat casually. Unit-level suite (81 tests) runs in ~2s with no windows.

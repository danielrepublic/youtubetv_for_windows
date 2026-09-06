# Dependency Manifest — YouTubeTvShell

All dependency versions are pinned explicitly. Update this document and the corresponding `.csproj` files together.

## Application Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.WindowsAppSDK` | **2.4.0** | WinUI 3 framework, XAML runtime, and Windows App Runtime for unpackaged desktop apps. Latest stable (2026-08-13). Includes WebView2 SDK transitively. Supports Windows 10 1809+. |
| `Microsoft.Web.WebView2` | **1.0.4191.47** | Explicitly pinned WebView2 SDK for embedding Chromium-based web content. Included transitively by WindowsAppSDK (requires >= 1.0.3719.77) but pinned here for version transparency. Latest stable (2026-08-28). |
| `Velopack` | **1.2.0** | Cross-platform installer and auto-update framework. Used for GitHub Releases packaging, startup update checks, and confirmed update installation. Latest stable (2026). |

## Test Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.NET.Test.Sdk` | **18.9.0** | Test platform SDK enabling `dotnet test` discovery and execution. Latest stable (2026-08-14). |
| `xunit` | **2.9.3** | Unit testing framework. v2 latest stable; compatible with Playwright's recommended xunit 2.8+ requirement. |
| `xunit.runner.visualstudio` | **3.1.5** | Visual Studio / `dotnet test` adapter for xunit test discovery and execution. |
| `Microsoft.Playwright` | **1.62.0** | Browser automation library for host-only QA via CDP connection to WebView2. Will be used in Task 5 for controlled-page testing. Latest stable (2026-08-11). |

## Target Platform

| Property | Value | Rationale |
|----------|-------|-----------|
| `TargetFramework` | `net8.0-windows10.0.19041.0` | .NET 8 LTS with Windows SDK 19041 (May 2020 Update) compile-time API surface. |
| `SupportedOSPlatformVersion` | `10.0.19041.0` | Minimum installable OS: Windows 10 version 1903 (build 18362), using 19041 SDK for broader API access. |
| `Platforms` | `x64` | Single architecture target per plan scope. |
| `WindowsPackageType` | `None` | Unpackaged desktop app; Velopack handles distribution instead of MSIX. |

## Tool Versions

| Tool | Version | Purpose |
|------|---------|---------|
| .NET SDK | 8.0.420 | Build toolchain. |
| `vpk` (Velopack CLI) | Match Velopack NuGet (1.2.0) | Package creation via `dnx vpk@1.2.0`. |

## Version Update Policy

- Update this manifest and both `.csproj` files simultaneously.
- Never use preview/experimental SDK releases in production.
- Velopack `vpk` tool version must match the NuGet package version.

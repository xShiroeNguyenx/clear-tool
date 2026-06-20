# ClearTool

[![Latest release](https://img.shields.io/github/v/release/xShiroeNguyenx/clear-tool?label=download&logo=github)](https://github.com/xShiroeNguyenx/clear-tool/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-0078d4)

**A safe, treemap-based disk cleanup tool for Windows 11.**

ClearTool scans an entire drive, visualizes disk usage as an interactive treemap (WinDirStat-style), classifies files and folders by deletion safety, and lets you reclaim space with confidence — deletions go to the Recycle Bin by default, and a multi-layer protection system makes it impossible to delete system-critical paths.

Built with **.NET 10 (LTS)** + **WPF** + [WPF-UI](https://github.com/lepoco/wpfui) (Fluent / Windows 11 design, Mica backdrop, auto light/dark theme).

---

## Features

- 🔍 **Full-drive scan** — fast single-pass enumeration that survives access-denied folders, skips junctions/symlinks (no infinite loops), handles long paths (>260 chars), and ignores OneDrive placeholder files (they don't occupy real disk space).
- 🗺️ **Interactive treemap** — squarified layout rendered with `DrawingVisual` (handles 100k+ tiles), colored by safety level, depth-shaded. Double-click to drill into a folder, breadcrumb navigation, adjustable detail level (4–8 levels), hover tooltips, click-to-inspect.
- 🚦 **Safety classification** — a rule engine labels known locations:
  - 🟢 **Safe** — regenerable caches: `%TEMP%`, npm/pip caches, browser caches (`Cache`, `Code Cache`, `GPUCache`), VS Code / Electron app caches (`CachedData`), Windows Update download cache, Playwright browsers, app logs…
  - 🟡 **Caution** — recoverable but costly: `node_modules`, `.m2`, `.gradle`, `.vscode` extensions, `*-backup` folders…
  - 🔴 **Keep** — system paths that are *never* suggested: `C:\Windows`, `Program Files`, `ProgramData`, recovery areas, `pagefile.sys`…
- 🛡️ **Multi-layer deletion safety**
  - Recycle Bin by default (restorable); permanent delete is a separate, explicitly-confirmed option.
  - An **independent `ProtectedRoots` guard** inside the deletion service refuses system paths, drive roots, `C:\Users`, and the user profile root — even if a rule or the UI is wrong.
  - Deletion uses the shell `IFileOperation` API (`FOF_SILENT | FOF_NOERRORUI`) — no surprise Explorer dialogs mid-batch; locked files are reported and skipped without aborting the batch.
- 🔐 **Per-task elevation** — runs as a normal user (`asInvoker`). Items that need admin (e.g. `C:\Windows\Temp`, Windows Update cache) trigger a UAC prompt for that one task only; the elevated instance executes a whitelisted task and exits. WinSxS cleanup via `DISM /StartComponentCleanup` is available as an explicit advanced action.
- ♻️ **Empty Recycle Bin** — deleting to the bin doesn't free space until the bin is emptied; ClearTool says so honestly and provides a one-click "Empty Recycle Bin" with size preview (`SHQueryRecycleBin` / `SHEmptyRecycleBin`).
- 📊 **Transparency** — after a scan, the gap between Windows' "used space" and the scanned total is shown ("≈ X GB inaccessible — shadow copies, junctions, NTFS metadata"), with a one-click *restart as Administrator* to scan more.
- 🌗 **Fluent / Windows 11 UI** — Mica window, theme follows the system (live light/dark switching re-tints the whole app including the treemap), tabbed suggestion groups.
- 🔄 **Built-in auto-update** — on launch ClearTool silently asks the GitHub Releases API for the latest version; if a newer one exists it shows a one-line banner with **Tải & cập nhật** (download & update). One click downloads the new single-file exe, then a tiny helper script waits for the app to exit, swaps the exe in place, and relaunches — no manual reinstall. Network/offline errors are swallowed so the check never nags. Version comparison is numeric (`1.10.0 > 1.9.0`) and unit-tested.

## Getting started

### Run

**[⬇ Download ClearTool.App.exe](https://github.com/xShiroeNguyenx/clear-tool/releases/latest/download/ClearTool.App.exe)** (latest release — self-contained single file, ~75 MB) and just run it — **no .NET runtime installation required**. Windows 10/11 x64.

All versions: [Releases page](https://github.com/xShiroeNguyenx/clear-tool/releases).

> Windows SmartScreen may warn on first run because the exe is unsigned — click *More info → Run anyway*.

### Build from source

Requirements: **.NET 10 SDK** (pinned via `global.json`).

```powershell
git clone https://github.com/xShiroeNguyenx/clear-tool.git
cd clear-tool

dotnet build ClearTool.sln          # build
dotnet test ClearTool.sln           # run unit tests (68 tests)
dotnet run --project src\ClearTool.App   # run the app
```

Publish a self-contained single-file exe:

```powershell
dotnet publish src\ClearTool.App\ClearTool.App.csproj -c Release -r win-x64 `
  -p:PublishSingleFile=true -p:SelfContained=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -o publish
```

> Note: `PublishTrimmed`/AOT are intentionally **not** used — WPF does not support them.

## Architecture

```
ClearTool.sln
├─ src/ClearTool.Core          # no UI — net10.0-windows
│  ├─ Scanning/                # FileSystemEnumerator-based scanner (error-tolerant, cancellable, throttled progress)
│  ├─ Model/                   # TreeNode (memory-lean: no per-node full path)
│  ├─ Rules/                   # SafetyLevel rules engine + default catalog (pure, fully unit-testable)
│  ├─ Treemap/                 # Squarified treemap layout (pure math, benchmarked at 155k nodes)
│  ├─ Deletion/                # IFileOperation deletion + independent ProtectedRoots guard + Recycle Bin utils
│  └─ Admin/                   # Per-task elevation (ElevatedTask / ElevationHelper / DISM WinSxS cleanup)
├─ src/ClearTool.App           # WPF + WPF-UI, MVVM (CommunityToolkit.Mvvm), DI
│  ├─ Controls/                # TreemapControl (DrawingVisual rendering, hit-testing, theme-aware)
│  ├─ ViewModels/ Views/ Services/ Resources/
│  └─ Assets/                  # app icon (generated by tools/generate-icon.ps1)
├─ tests/ClearTool.Core.Tests  # xUnit: rules, guard, deletion (sandboxed), scanner (junction-loop proof), treemap layout
└─ tools/generate-icon.ps1     # reproducible app-icon generator (GDI+)
```

Key design decisions:

| Decision | Why |
|---|---|
| `FileSystemEnumerator<T>` instead of `Directory.Enumerate*` | `ContinueOnError` lets the scan survive access-denied entries instead of throwing on the first protected folder |
| Reparse points (junctions/symlinks) skipped | prevents infinite recursion and double counting |
| Treemap = `DrawingVisual` host, *not* element-per-tile | tens of thousands of tiles would collapse a `UIElement`-based approach; min-pixel culling + depth limit keep it fast |
| `ProtectedRoots` guard is independent of the rules engine | defense in depth — a bug in rules or UI still cannot delete `C:\Windows` |
| Elevated instance only accepts **whitelisted rule ids**, never raw paths | a command-line argument can never direct an elevated delete at an arbitrary path |
| Recycle Bin clean-up special-cased via `SHEmptyRecycleBin` | "recycling" the contents of `$Recycle.Bin` itself would be meaningless |

## Why doesn't the scanned total match Windows' "used space"?

The difference is disk space no file scanner is allowed to see: Volume Shadow Copies / System Restore (`System Volume Information`), NTFS metadata (MFT, journal), skipped junctions, OneDrive placeholders (counted as 0 by design), and cluster slack. ClearTool shows this gap explicitly after each scan instead of hiding it.

## Testing

68 unit tests cover the high-risk areas without touching real user data:

- **Rules engine** — fake in-memory trees: priorities, KEEP-wins-ties, KEEP inheritance, admin flags.
- **ProtectedRoots guard** — system paths refused even when passed directly; case/trailing-slash/`..` cannot bypass it.
- **Deletion** — temp-sandbox only: recycle/permanent, locked files don't break the batch.
- **Scanner** — sandbox with a junction pointing *upward* to prove loop-safety; aggregate size correctness; cancellation.
- **Treemap layout** — proportionality, bounds, depth limit, culling, painter's order + a 155k-node benchmark.

## Tech stack

.NET 10 (LTS) · WPF · [WPF-UI 4.3](https://github.com/lepoco/wpfui) (MIT) · CommunityToolkit.Mvvm · Microsoft.Extensions.DependencyInjection · xUnit + FluentAssertions

## License

MIT

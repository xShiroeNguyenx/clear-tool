# ClearTool — Kế hoạch triển khai

> 📌 **Trạng thái: HOÀN THÀNH** (06/2026) — toàn bộ Phase 0–4 + redesign UI đã xong, 68/68 tests pass.
> Tài liệu cho người dùng/đóng góp: xem [README.md](README.md). File này là nhật ký thiết kế & quyết định kỹ thuật.

Phần mềm dọn ổ đĩa cho Windows 11: **quét toàn ổ → hiển thị treemap theo kích thước → gợi ý file/thư mục nên xóa theo mức độ an toàn → xóa có xác nhận (mặc định vào Thùng rác)**.

> ⚙️ **.NET 10 SDK (10.0.301)** đã cài user-local tại `%LOCALAPPDATA%\Microsoft\dotnet` (không cần UAC). Khi build, set `DOTNET_ROOT=%LOCALAPPDATA%\Microsoft\dotnet` và thêm vào đầu `PATH` (dotnet.exe ở `C:\Program Files\dotnet` chỉ có runtime, không có SDK). KHÔNG dùng .NET 8: hết hỗ trợ tháng 11/2026; .NET 10 LTS được hỗ trợ đến 11/2028.

---

## Context — Vì sao làm ClearTool

Ổ C (153 GB) thường xuyên đầy do nhiều công cụ dev (cache npm/pip, VS Code, Antigravity, WinSxS...), phải dọn thủ công bằng PowerShell rất mất thời gian. ClearTool biến quy trình đó thành một app desktop trực quan và an toàn.

**Quyết định sản phẩm đã chốt:**
- **Tech stack:** .NET 10 (LTS, hỗ trợ đến 11/2028) + WPF + C# — app GUI cho Windows 11.
- **Xử lý file:** gợi ý + xóa có xác nhận; mặc định đưa vào **Recycle Bin** (khôi phục được), có tùy chọn xóa vĩnh viễn.
- **Phạm vi quét:** phân tích toàn ổ dạng **treemap** (như WinDirStat/TreeSize), kết hợp gắn nhãn an toàn theo danh mục.

---

## 4 nguyên tắc thiết kế cốt lõi (giữ nguyên khi code)
1. **Core nhắm `net10.0-windows`** (KHÔNG phải `net10.0` thuần) — vì xóa vào Recycle Bin dùng `Microsoft.VisualBasic.FileIO` (Windows-only). ⚠️ Type này nằm trong framework **Microsoft.WindowsDesktop.App**, KHÔNG tự có trong class library không-UI — Core phải thêm `<FrameworkReference Include="Microsoft.WindowsDesktop.App" />` (chi tiết ở mục 5).
2. **`DeletionService` có hàng rào `ProtectedRoots` ĐỘC LẬP** với rules engine (phòng thủ nhiều lớp — bug ở rule/UI cũng không xóa được thư mục hệ thống).
3. **`longPathAware`** trong manifest + quét hỗ trợ đường dẫn dài (>260 ký tự) — quét toàn ổ chắc chắn gặp.
4. **`IProgress` báo tiến độ có throttle** (~10 lần/giây), KHÔNG báo theo từng file (sẽ làm nghẽn UI).

---

## 1. Cấu trúc Solution (3 project)

```
ClearTool\
  ClearTool.sln
  Directory.Build.props          # Nullable=enable, LangVersion=latest, ImplicitUsings
  global.json                    # pin .NET 10 SDK
  .gitignore

  src\
    ClearTool.Core\              # KHÔNG UI. TFM: net10.0-windows + FrameworkReference Microsoft.WindowsDesktop.App
      Scanning\  IFileSystemScanner.cs  FileSystemScanner.cs  ScanEntry.cs  ScanOptions.cs  ScanProgress.cs
      Model\     TreeNode.cs  NodeKind.cs
      Rules\     SafetyLevel.cs  CleanupRule.cs  IPathMatcher.cs  RuleCatalog.cs  RuleEngine.cs
                 CleanupSuggestion.cs  SuggestionGroup.cs
        Matchers\  EnvKnownLocationMatcher.cs  GlobMatcher.cs  ExactRootMatcher.cs
      Deletion\  IDeletionService.cs  DeletionService.cs  DeleteOptions.cs  DeleteResult.cs  ProtectedRoots.cs
      Admin\     ElevationHelper.cs  SystemCleanupTasks.cs

    ClearTool.App\               # TFM: net10.0-windows, UseWPF=true
      app.manifest               # asInvoker + longPathAware + dpiAware PerMonitorV2
      App.xaml/.cs
      Views\        MainWindow.xaml/.cs  ConfirmDeleteDialog.xaml/.cs
      Controls\     TreemapControl.cs  TreemapVisualHost.cs
      ViewModels\   MainViewModel.cs  SuggestionItemViewModel.cs  SuggestionGroupViewModel.cs  TreemapTileViewModel.cs
      Services\     DialogService.cs  UiDispatcher.cs
      Converters\   BytesToHumanReadableConverter.cs  SafetyLevelToBrushConverter.cs
      Resources\    Styles.xaml  Colors.xaml

  tests\
    ClearTool.Core.Tests\        # TFM: net10.0-windows
      RuleEngineTests.cs  RuleCatalogTests.cs  ProtectedRootsGuardTests.cs
      DeletionServiceTests.cs  ScannerTests.cs
```

---

## 2. Engine quét đĩa (Scanning)

**Quyết định:** kế thừa `System.IO.Enumeration.FileSystemEnumerator<TResult>` (KHÔNG dùng `Directory.EnumerateFileSystemEntries`). Lý do chính là **xử lý lỗi**: override `ContinueOnError(int)` để bỏ qua access-denied/sharing-violation từng entry mà vẫn chạy tiếp — `Directory.Enumerate*` sẽ ném lỗi ngay khi gặp thư mục bị bảo vệ đầu tiên.

```csharp
internal sealed class SafeEnumerator : FileSystemEnumerator<ScanEntry>
{
    public SafeEnumerator(string root, EnumerationOptions o) : base(root, o) { }
    protected override ScanEntry TransformEntry(ref FileSystemEntry e) =>
        new(e.ToFullPath(), e.Length, e.Attributes, e.IsDirectory);
    // Bỏ qua reparse point (junction/symlink) -> tránh vòng lặp vô hạn
    protected override bool ShouldRecursePredicate(ref FileSystemEntry e) =>
        (e.Attributes & FileAttributes.ReparsePoint) == 0;
    protected override bool ContinueOnError(int error) => true;
}
```
`EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, ReturnSpecialDirectories = false }`. (Ghi chú: `IgnoreInaccessible` thực ra thừa khi đã override `ContinueOnError => true` — giữ lại vô hại, chỉ để khỏi thắc mắc sau này.)

- **Tránh vòng lặp:** bỏ qua `FileAttributes.ReparsePoint` (C:\Users\All Users, Documents and Settings... là junction, dễ gây đệ quy vô hạn).
- **Long path:** cần `longPathAware` trong manifest + bật long-path ở OS.
- **OneDrive Files On-Demand:** file placeholder (attribute `RecallOnDataAccess`) báo size logic nhưng KHÔNG chiếm đĩa thật — đánh dấu/loại các entry này khỏi tổng "thu hồi được" và không gợi ý xóa (xóa có thể kích hoạt tải về hoặc mất file cloud-only).
- **Song song:** MVP chạy **đơn luồng** cho đúng & đơn giản; sau có thể fan-out mỗi thư mục con cấp 1 một `SafeEnumerator` qua `Parallel.ForEach`. Để `ScanOptions.DegreeOfParallelism` tùy chỉnh được.
- **Hủy:** kiểm `token.IsCancellationRequested` mỗi N entry trong **vòng lặp consumer gọi `MoveNext()`** (không hook được bên trong `MoveNext` của `FileSystemEnumerator`); chạy scan trên `Task.Run`.
- **Tiến độ:** gộp counter, `Report(...)` tối đa ~10 lần/giây (mỗi ~25ms hoặc 5000 entry).

**Model cây có tổng kích thước:**
```csharp
public sealed class TreeNode {
    public string Name; public NodeKind Kind;          // KHÔNG lưu FullPath per-node (xem ghi chú bộ nhớ)
    public long OwnSize;          // kích thước file (dir = 0)
    public long AggregateSize;    // own + toàn bộ con
    public TreeNode? Parent; public List<TreeNode> Children;
    public FileAttributes Attributes;
    public SafetyLevel Safety = SafetyLevel.Unknown;   // do RuleEngine gán
    public string GetFullPath();  // dựng từ chuỗi Parent khi cần
}
```
Dựng cây bằng `Dictionary<string,TreeNode>` theo path khi quét, rồi một lượt post-order cộng dồn `AggregateSize` lên trên.

> 💾 **Bộ nhớ (quan trọng với 1–2 triệu file):** KHÔNG lưu `FullPath` cho mọi node — riêng chuỗi full-path có thể tốn vài trăm MB. Chỉ lưu `Name`, dựng full path bằng cách nối chuỗi `Parent` khi cần (`GetFullPath()`). `Dictionary<string,TreeNode>` chỉ cần chứa **thư mục** (file không bao giờ làm parent) và có thể vứt bỏ sau khi dựng xong cây.

---

## 3. Vẽ Treemap trong WPF

**Quyết định:** treemap **squarified** tự vẽ, render lên một host `FrameworkElement` chứa `VisualCollection` các `DrawingVisual` (KHÔNG dùng thư viện ngoài, KHÔNG mỗi tile một `UIElement`). Với hàng chục nghìn node, cách element-per-tile sẽ sập; `DrawingVisual` là primitive retained-mode nhẹ — đúng kiểu WinDirStat.

- `TreemapVisualHost : FrameworkElement` override `VisualChildrenCount`/`GetVisualChild`.
- `TreemapControl : Control` tính layout squarified (giữ tỉ lệ tile ~1:1), tô màu theo `SafetyLevel` (xanh/vàng/đỏ), đậm nhạt theo độ sâu.

**Các đòn bẩy hiệu năng (bắt buộc):**
- **Giới hạn độ sâu** ~4–6 cấp từ root hiện tại; double-click để drill (re-root).
- **Bỏ tile dưới ~3–4 px²** (không tạo DrawingVisual, không đệ quy tiếp) — thắng lớn nhất.
- **Chỉ rebuild khi resize / re-root** (throttled).
- **Hit-test bằng `VisualTreeHelper.HitTest`**, mỗi visual gắn `TreeNode`.

> Đây là phần UI rủi ro nhất — dựng riêng, benchmark với cây giả 50k–200k node trước khi nối vào scan thật (Phase 2).

---

## 4. Engine phân loại (Rules / Categorization)

```csharp
public enum SafetyLevel { Safe, Caution, Keep, Unknown }
public interface IPathMatcher { bool Matches(TreeNode node); }
public sealed class CleanupRule {
    public required string Id, DisplayName;
    public required SafetyLevel Level;
    public required IPathMatcher Matcher;
    public string Reason = "";        // hiện ở panel chi tiết
    public bool NeedsAdmin;           // SoftwareDistribution, %WINDIR%\Temp, WinSxS
    public int Priority;              // cao thắng; KEEP thắng khi hòa
}
```
**Matchers:** `EnvKnownLocationMatcher` (giải `%TEMP%`, `%LOCALAPPDATA%`, `%WINDIR%`, `%APPDATA%`, `%USERPROFILE%`), `GlobMatcher` (glob→regex cho `**\node_modules`, `**\*-backup`, `**\Cache`, `**\Code Cache`, `**\GPUCache`, `**\CachedData`, `**\browser_recordings`, `**\.m2`, `**\.gradle`), `ExactRootMatcher` (root chính xác + `pagefile.sys`/`hiberfil.sys`/`swapfile.sys`).

**RuleCatalog — mã hóa kiến thức thực tế từ phiên dọn máy này:**
- 🟢 **SAFE:** `%TEMP%`, `%LOCALAPPDATA%\Temp`, `%WINDIR%\Temp`(admin), npm-cache, pip cache, `%WINDIR%\SoftwareDistribution\Download`(admin), Recycle Bin (⚠️ **special-case**: dọn bằng `SHEmptyRecycleBin`, KHÔNG xóa file trong `C:\$Recycle.Bin` qua DeletionService — "xóa-vào-thùng-rác" chính nội dung thùng rác là vô nghĩa), cache trình duyệt (Chrome `Cache`/`Code Cache`), cache app (VS Code/Cursor/Antigravity `CachedData`/`GPUCache`/`logs`), `ms-playwright`, cache Slack/Zoom, Antigravity `browser_recordings`.
- 🟡 **CAUTION:** `.gemini`, `.vscode`, `.codex` (không phải pure cache), `node_modules`, `.m2`/`.gradle`/eclipse (tải lại được), `*-backup`.
- 🔴 **KEEP/SYSTEM (không bao giờ gợi ý xóa):** `C:\Recovery`, `C:\System Volume Information`, `C:\System Repair`, `C:\Windows`, `C:\Program Files`(`(x86)`), `C:\ProgramData`, `C:\$WinREAgent`, `C:\PerfLogs`, `pagefile.sys`, `hiberfil.sys`, `swapfile.sys`.

**RuleEngine** (`Evaluate(TreeNode root) -> IReadOnlyList<SuggestionGroup>`): duyệt pre-order; nhiều rule khớp thì `Priority` cao thắng, **KEEP luôn thắng khi hòa**; KEEP **kế thừa xuống dưới** và KHÔNG duyệt vào trong (không gợi ý xóa bên trong `C:\Windows`); rule SAFE/CAUTION khớp 1 thư mục thì phát **một** `CleanupSuggestion` cho thư mục đó (reclaimable = `AggregateSize`). Output là `List<CleanupSuggestion>` nhóm theo `SafetyLevel`. Engine **thuần** (chỉ dùng path + `FileAttributes`) → unit test được hoàn toàn, không cần ổ đĩa thật.

---

## 5. Dịch vụ xóa (Deletion)

```csharp
public interface IDeletionService {
    Task<IReadOnlyList<DeleteResult>> DeleteAsync(
        IEnumerable<string> paths, DeleteOptions opts,
        IProgress<DeleteResult> progress, CancellationToken ct);
}
public enum DeleteMode { RecycleBin, Permanent }
```
- **Recycle Bin (mặc định):** `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile/DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin)` — KHÔNG cần NuGet, nhưng Core **bắt buộc** thêm `<FrameworkReference Include="Microsoft.WindowsDesktop.App" />` vào csproj (type này thuộc Windows Desktop framework, class library không-UI không tự có).
- ⚠️ **Hạn chế của VB FileIO (xử lý ở Phase 4):** `UIOption.OnlyErrorDialogs` vẫn có thể bật **dialog lỗi của Explorer** giữa batch chạy nền (không có lựa chọn "no UI" thật sự) — hướng chính thức Phase 4 là P/Invoke `IFileOperation` với `FOF_ALLOWUNDO | FOF_NOERRORUI | FOF_SILENT`. Recycle Bin cũng xử lý **long path (>260 ký tự) không đáng tin** — path quá dài có thể phải xóa vĩnh viễn (cảnh báo user trước).
- **Xóa vĩnh viễn:** `RecycleOption.DeletePermanently` — là lựa chọn riêng, xác nhận riêng.
- **Báo từng item:** `DeleteResult { Path, Success, BytesFreed, Error, WasLocked }`; bắt lỗi từng item; file bị khóa ghi nhận `WasLocked=true` và **tiếp tục** batch; báo dần qua `IProgress`.
- 📌 **Recycle Bin KHÔNG giải phóng dung lượng:** đưa vào Thùng rác chỉ *di chuyển* dữ liệu trong cùng ổ. UI phải nói rõ "đã chuyển vào Thùng rác — dọn Thùng rác để thực sự thu hồi X GB", và app cần nút **Empty Recycle Bin** dùng P/Invoke `SHEmptyRecycleBin` (kèm xác nhận riêng).

> ⚠️ **Hàng rào ProtectedRoots độc lập (bắt buộc):** trước khi xóa, canonicalize đường dẫn (`Path.GetFullPath`, chuẩn hóa hoa/thường + dấu `\`) và **từ chối** mọi path bằng/nằm dưới protected root (toàn bộ nhóm KEEP + root ổ đĩa) hoặc là `pagefile/hiberfil/swapfile.sys`. Bổ sung vào ProtectedRoots: **`C:\Users`** và **root profile của user hiện tại (`%USERPROFILE%`)** — chặn xóa *chính* các root đó (vẫn cho xóa cache bên trong). Dù rule gắn nhãn sai hay UI tick nhầm, service vẫn KHÔNG xóa được thư mục hệ thống hay cả profile user.

---

## 6. Nâng quyền Admin

**Manifest `asInvoker` + relaunch nâng quyền theo nhu cầu.** Chạy thường không cần admin (scan và Recycle Bin đều chạy được); chỉ vài tác vụ hệ thống cần admin.
```xml
<requestedExecutionLevel level="asInvoker" uiAccess="false" />
```
Manifest cũng khai báo **`longPathAware`** (cho scan toàn ổ) + **`dpiAwareness = PerMonitorV2`** + supported-OS Win10/11.

- **Phát hiện:** `WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)`.
- **Theo nhu cầu:** `ProcessStartInfo { UseShellExecute=true, Verb="runas", Arguments="--elevated-task=..." }`; instance nâng quyền chỉ chạy đúng tác vụ đó. Bắt `Win32Exception` khi user hủy UAC.
- **Gating UI:** suggestion `NeedsAdmin` (`%WINDIR%\Temp`, `SoftwareDistribution\Download`, WinSxS) hiện icon khiên, relaunch riêng từng tác vụ. **WinSxS:** `DISM /Online /Cleanup-Image /StartComponentCleanup` — **bỏ `/ResetBase`** (hay treo, không hoàn tác được) — là "Advanced system cleanup" riêng, không tự gợi ý.

---

## 7. MVVM / Giao diện

**Quyết định:** `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`, `IMessenger`).

`MainWindow` (Grid):
- **Thanh trên:** chọn ổ (`DriveInfo.GetDrives()`), nút Scan/Cancel, `ProgressBar` + trạng thái (`"123,456 files · 45.2 GB · đang quét C:\Users\..."`).
- **Giữa:** `TreemapControl` + breadcrumb + nút Back.
- **Phải:** danh sách suggestion nhóm theo `SafetyLevel` (header xanh/vàng/đỏ kèm tổng dung lượng thu hồi + checkbox cả nhóm; mỗi item có checkbox, path, size, lý do, icon khiên nếu cần admin; nhóm KEEP chỉ đọc).
- **Dưới:** chi tiết node đang chọn (path, size, lý do, Recycle Bin/Permanent, yêu cầu admin).
- **Footer:** tổng dung lượng sẽ thu hồi của các item đã tick + nút **Xóa mục đã chọn**.

**Luồng xóa:** Delete → `ConfirmDeleteDialog` (số item, tổng byte, toggle Recycle Bin/Permanent mặc định Recycle Bin, cảnh báo item cần admin) → `DeleteSelectedCommand` gọi `IDeletionService.DeleteAsync` với progress → kết quả từng item (đã xóa/lỗi/bị khóa) → footer: với mode Recycle Bin hiện **"đã chuyển X GB vào Thùng rác — dọn Thùng rác để thực sự thu hồi"** (kèm nút Empty Recycle Bin), với mode Permanent hiện dung lượng đã giải phóng thật → tùy chọn quét lại subtree bị ảnh hưởng. ViewModel nhận `IFileSystemScanner`, `RuleEngine`, `IDeletionService` qua DI (composition root ở `App.xaml.cs`, có thể dùng `Microsoft.Extensions.DependencyInjection`); tác vụ dài chạy `Task.Run`, progress qua `Progress<T>` về UI thread.

---

## 8. NuGet & Đóng gói

- **App:** `CommunityToolkit.Mvvm`; (tùy chọn) `Microsoft.Extensions.DependencyInjection`.
- **Core:** `Microsoft.VisualBasic` — **không cần package**, nhưng phải có `<FrameworkReference Include="Microsoft.WindowsDesktop.App" />` trong `ClearTool.Core.csproj` (class library `net10.0-windows` thuần không tự reference Windows Desktop framework).
- **Tests:** `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `FluentAssertions`.

**Build 1 file .exe:**
```
dotnet publish src\ClearTool.App\ClearTool.App.csproj -c Release -r win-x64 ^
  -p:PublishSingleFile=true -p:SelfContained=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```
**Khuyến nghị self-contained** (không cần cài runtime trước; ~60–80 MB). **KHÔNG** bật `PublishTrimmed` (WPF + reflection + COM trim sẽ vỡ runtime); bỏ AOT/R2R (WPF không hỗ trợ AOT).

---

## 9. Thứ tự build & Kiểm thử

- ✅ **Phase 0 — Scaffold:** solution + 3 project, `Directory.Build.props`, `global.json`, manifest, composition root, MainWindow rỗng. Build + chạy được.
- ✅ **Phase 1 — MVP:** scanner (enumerator, skip reparse, nuốt lỗi, progress throttle, hủy, cây tổng size) + RuleCatalog/RuleEngine + DeletionService (Recycle Bin) **kèm ProtectedRoots guard** + MainWindow (chọn ổ/scan/cancel/progress, suggestion nhóm + checkbox, footer dung lượng, confirm dialog, xóa). **Hết Phase 1 đã có sản phẩm dùng được.** (50 tests pass; publish single-file 72 MB OK)
- ✅ **Phase 2 — Treemap:** layout squarified thuần đặt ở `Core\Treemap\` (test/benchmark không cần UI — 155k node: 737ms); `TreemapControl`/`TreemapVisualHost` render DrawingVisual với depth-limit 6 + min-pixel culling thích ứng (≤~30k tile) + hit-test + tooltip + click chọn + double-click drill-down + breadcrumb/Back + sync selection 2 chiều với danh sách suggestion.
- ✅ **Phase 3 — Admin:** `ElevatedTask` (parse arg testable) + `ElevationHelper.RelaunchElevated` trả `Process` để chờ exit code; `ElevatedTaskRunner` whitelist chỉ rule NeedsAdmin trong catalog (không nhận path tùy ý từ command line); main instance chờ từng tác vụ UAC; nút WinSxS riêng (DISM StartComponentCleanup); title hiện "(Administrator)". ⚠️ Lưu ý đã fix: WPF `Main` là `void` nên phải set `Environment.ExitCode` (Shutdown(code) không propagate).
- ✅ **Phase 4 — Polish:** DeletionService chuyển sang **`IFileOperation`** (`FOF_SILENT|FOF_NOCONFIRMATION|FOF_NOERRORUI`, chạy trên thread STA riêng, fallback VB FileIO khi COM lỗi) — lưu ý: shell silent-abort KHÔNG trả HRESULT gốc (cần progress sink mới có), nên detect file khóa bằng heuristic mở exclusive; nút **Dọn Thùng rác** (SHQueryRecycleBin báo size trước khi confirm); prompt **quét lại sau xóa**; setting **độ sâu treemap 4–8**; logging `%LOCALAPPDATA%\ClearTool\cleartool.log` + global exception handler; publish single-file.
- ✅ **Phase 5 — Redesign UI (WPF-UI / Fluent Win11):** `FluentWindow` + Mica + theme theo hệ thống (đổi live, kể cả palette an toàn — `SafetyPalette` mutate brush không freeze — và treemap dark mode); bố cục mới: TitleBar custom (⚠ WPF-UI 4.x TitleBar KHÔNG render chuỗi `Title` — phải tự đặt TextBlock vào `Header`), header card với Quét là hero action, 2 content card, action bar gom 3 nút dọn; suggestion chuyển **TabControl** (tab An toàn/Cân nhắc); emoji → `SymbolIcon` (⚠ glyph `BinRecycle24` thiếu trong font → dùng `Broom24`); `IDialogService` chuyển async với Wpf.Ui MessageBox; app icon mosaic treemap sinh bằng `tools/generate-icon.ps1` (⚠ entry ICO phải là DIB — entry PNG tự ghép làm CSC báo CS7065); dòng "≈ X GB không truy cập được" sau scan + nút relaunch admin (`TryRestartElevated`).

**Kiểm thử (verification):**
- **Rules engine (giá trị cao, rẻ nhất):** unit test trên cây `TreeNode` giả với path bịa — kiểm mọi entry SAFE/CAUTION/KEEP, KEEP thắng khi hòa, KEEP không duyệt xuống, cờ `NeedsAdmin`. Không đụng ổ đĩa.
- **ProtectedRoots guard test (an toàn sống còn):** assert `DeletionService` **từ chối** `C:\Windows`, `C:\Program Files`, `C:\Users`, `%USERPROFILE%`, root ổ, `pagefile.sys`, path dưới protected root — dù truyền vào trực tiếp.
- **Scanner test:** thư mục sandbox tạm (cây lồng nhau, file kích thước biết trước, một junction trỏ ngược lên để chứng minh không lặp vô hạn, một thư mục bị chặn ACL để chứng minh skip mượt) — assert tổng size, skip reparse, hoàn tất, hủy.
- **Deletion test:** chỉ trên sandbox tạm — assert item RecycleBin vào Thùng rác (khôi phục được), item Permanent biến mất, file bị khóa báo lỗi mà không phá batch. **Không bao giờ nhắm path thật của user/hệ thống.**
- **Smoke thủ công:** quét `C:\` thật trên máy này — không ném lỗi ở thư mục bị chặn, treemap render, suggestion khớp các tier đã biết, xóa-vào-Thùng-rác giải phóng dung lượng và khôi phục được.

**Phần rủi ro nhất & cách giảm rủi ro:** (1) hiệu năng treemap → DrawingVisual + depth-limit + min-pixel culling, benchmark riêng; (2) access-denied → `ContinueOnError` + `IgnoreInaccessible` + test ACL sandbox; (3) an toàn xóa → mặc định Recycle Bin + **hàng rào ProtectedRoots độc lập** + bắt buộc xác nhận + unit test guard.

---

## File quan trọng nhất khi triển khai
- `src\ClearTool.Core\Scanning\FileSystemScanner.cs`
- `src\ClearTool.Core\Rules\RuleCatalog.cs`
- `src\ClearTool.Core\Deletion\DeletionService.cs` (+ `ProtectedRoots.cs`)
- `src\ClearTool.App\Controls\TreemapControl.cs`
- `src\ClearTool.App\ViewModels\MainViewModel.cs`

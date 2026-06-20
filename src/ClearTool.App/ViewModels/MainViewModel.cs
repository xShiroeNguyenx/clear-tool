using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClearTool.App.Converters;
using ClearTool.App.Services;
using ClearTool.Core.Admin;
using ClearTool.Core.Deletion;
using ClearTool.Core.Model;
using ClearTool.Core.Rules;
using ClearTool.Core.Scanning;

namespace ClearTool.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IFileSystemScanner _scanner;
    private readonly RuleEngine _ruleEngine;
    private readonly IDeletionService _deletionService;
    private readonly IDialogService _dialogService;
    private readonly IUpdateService _updateService;

    private CancellationTokenSource? _scanCts;
    private TreeNode? _scanRoot;

    public MainViewModel(
        IFileSystemScanner scanner,
        RuleEngine ruleEngine,
        IDeletionService deletionService,
        IDialogService dialogService,
        IUpdateService updateService)
    {
        _scanner = scanner;
        _ruleEngine = ruleEngine;
        _deletionService = deletionService;
        _dialogService = dialogService;
        _updateService = updateService;

        Drives = new ObservableCollection<string>(
            DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable)
                .Select(d => d.Name));
        SelectedDrive = Drives.FirstOrDefault();
        IsAdministrator = ElevationHelper.IsAdministrator();
    }

    public ObservableCollection<string> Drives { get; }

    public bool IsAdministrator { get; }

    public bool IsNotAdministrator => !IsAdministrator;

    public string WindowTitle => IsAdministrator
        ? "ClearTool — Dọn ổ đĩa (Administrator)"
        : "ClearTool — Dọn ổ đĩa";

    /// <summary>Dòng "X GB không truy cập được" — null khi chưa quét / chênh lệch nhỏ.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInaccessibleInfo))]
    private string? _inaccessibleInfo;

    public bool HasInaccessibleInfo => InaccessibleInfo is not null;

    [RelayCommand]
    private void RestartAsAdministrator()
    {
        if (ElevationHelper.TryRestartElevated())
            System.Windows.Application.Current.Shutdown();
        // user hủy UAC → giữ nguyên instance hiện tại
    }

    // ── Tự cập nhật (GitHub Releases) ───────────────────────────────────

    private UpdateInfo? _availableUpdate;

    /// <summary>Có bản mới hơn → hiện banner cập nhật.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadAndUpdateCommand))]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private string _updateBannerText = "";

    /// <summary>Đang tải/áp dụng bản mới → hiện vòng quay, khóa nút.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadAndUpdateCommand))]
    private bool _isUpdating;

    [ObservableProperty]
    private string? _updateProgressText;

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        var update = await _updateService.CheckForUpdateAsync();
        if (update is null)
            return;
        _availableUpdate = update;
        UpdateBannerText = $"Đã có bản mới {update.TagName} — bạn đang dùng v{_updateService.CurrentVersion}.";
        IsUpdateAvailable = true;
    }

    private bool CanDownloadAndUpdate() => IsUpdateAvailable && !IsUpdating;

    [RelayCommand(CanExecute = nameof(CanDownloadAndUpdate))]
    private async Task DownloadAndUpdateAsync()
    {
        if (_availableUpdate is null)
            return;

        IsUpdating = true;
        UpdateProgressText = "Đang tải bản cập nhật...";
        try
        {
            var progress = new Progress<double>(p =>
                UpdateProgressText = $"Đang tải bản cập nhật... {p * 100:0}%");

            if (await _updateService.DownloadAndApplyAsync(_availableUpdate, progress))
            {
                // Script trung gian đang chờ tiến trình này thoát để ghi đè exe.
                UpdateProgressText = "Đang khởi động lại để áp dụng bản mới...";
                System.Windows.Application.Current.Shutdown();
            }
            else
            {
                IsUpdating = false;
                UpdateProgressText = null;
                OpenReleaseNotes();
            }
        }
        catch (Exception ex)
        {
            IsUpdating = false;
            UpdateProgressText = null;
            await _dialogService.ShowErrorAsync("Lỗi cập nhật",
                $"Tải/cập nhật thất bại: {ex.Message}\n\nBạn có thể tải bản mới thủ công từ trang Releases.");
        }
    }

    [RelayCommand]
    private void OpenReleaseNotes()
    {
        var url = _availableUpdate?.ReleasePageUrl
                  ?? "https://github.com/xShiroeNguyenx/clear-tool/releases/latest";
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Error("OpenReleaseNotes", ex);
        }
    }

    [RelayCommand]
    private void DismissUpdate() => IsUpdateAvailable = false;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    private string? _selectedDrive;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanWinSxsCommand))]
    [NotifyCanExecuteChangedFor(nameof(EmptyRecycleBinCommand))]
    private bool _isScanning;

    /// <summary>True từ lúc bắt đầu quét đến khi gợi ý dựng xong — hiện overlay
    /// loading trên thẻ gợi ý để user không tưởng bị lỗi. Tách khỏi
    /// <see cref="IsScanning"/> để KHÔNG che danh sách khi đang xóa.</summary>
    [ObservableProperty]
    private bool _isLoadingResults;

    /// <summary>Độ sâu hiển thị treemap (4–8).</summary>
    [ObservableProperty]
    private int _treemapDepth = 6;

    public int[] TreemapDepthChoices { get; } = [4, 5, 6, 7, 8];

    [ObservableProperty]
    private string _statusText = "Chọn ổ đĩa và bấm Quét.";

    [ObservableProperty]
    private ObservableCollection<SuggestionGroupViewModel> _suggestionGroups = [];

    [ObservableProperty]
    private SuggestionItemViewModel? _selectedSuggestion;

    partial void OnSelectedSuggestionChanged(SuggestionItemViewModel? value)
    {
        // Sync suggestion → treemap: highlight tile nếu đang hiển thị
        if (value is not null)
            SelectedTreemapNode = value.Suggestion.Node;
    }

    // ── Treemap (Phase 2) ────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NavigateBackCommand))]
    private TreeNode? _treemapRoot;

    public ObservableCollection<TreeNode> Breadcrumbs { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedNodeInfo))]
    private TreeNode? _selectedTreemapNode;

    public string SelectedNodeInfo
    {
        get
        {
            var node = SelectedTreemapNode;
            if (node is null)
                return "Click một ô để xem chi tiết · double-click thư mục để đi sâu vào.";
            var safety = node.Safety switch
            {
                SafetyLevel.Safe => "Xóa an toàn",
                SafetyLevel.Caution => "Cân nhắc",
                SafetyLevel.Keep => "Hệ thống — giữ nguyên",
                _ => "Chưa phân loại",
            };
            return $"{node.GetFullPath()}  ·  {BytesToHumanReadableConverter.Format(node.AggregateSize)}  ·  {safety}";
        }
    }

    [RelayCommand]
    private void DrillInto(TreeNode? node)
    {
        if (node is not { Kind: NodeKind.Directory } || node.Children.Count == 0)
            return;
        TreemapRoot = node;
        RebuildBreadcrumbs();
    }

    private bool CanNavigateBack() => TreemapRoot?.Parent is not null;

    [RelayCommand(CanExecute = nameof(CanNavigateBack))]
    private void NavigateBack()
    {
        if (TreemapRoot?.Parent is { } parent)
        {
            TreemapRoot = parent;
            RebuildBreadcrumbs();
        }
    }

    [RelayCommand]
    private void NavigateTo(TreeNode? node)
    {
        if (node is null)
            return;
        TreemapRoot = node;
        RebuildBreadcrumbs();
    }

    private void RebuildBreadcrumbs()
    {
        Breadcrumbs.Clear();
        var chain = new List<TreeNode>();
        for (var n = TreemapRoot; n is not null; n = n.Parent)
            chain.Add(n);
        chain.Reverse();
        foreach (var n in chain)
            Breadcrumbs.Add(n);
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    private long _totalSelectedBytes;

    public string FooterText => TotalSelectedBytes > 0
        ? $"Sẽ thu hồi: {BytesToHumanReadableConverter.Format(TotalSelectedBytes)}"
        : "Chưa chọn mục nào.";

    partial void OnTotalSelectedBytesChanged(long value) => OnPropertyChanged(nameof(FooterText));

    private bool CanScan() => !IsScanning && SelectedDrive is not null;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        if (SelectedDrive is null)
            return;

        _scanCts = new CancellationTokenSource();
        IsScanning = true;
        IsLoadingResults = true;
        SuggestionGroups = [];
        TotalSelectedBytes = 0;
        TreemapRoot = null;
        SelectedTreemapNode = null;
        InaccessibleInfo = null;
        Breadcrumbs.Clear();

        var progress = new Progress<ScanProgress>(p =>
            StatusText = $"{p.EntriesScanned:N0} mục · {BytesToHumanReadableConverter.Format(p.BytesSeen)} · {p.CurrentPath}");

        try
        {
            _scanRoot = await _scanner.ScanAsync(SelectedDrive, new ScanOptions(), progress, _scanCts.Token);

            StatusText = "Đang phân loại...";
            var groups = await Task.Run(() => _ruleEngine.Evaluate(_scanRoot));

            SuggestionGroups = new ObservableCollection<SuggestionGroupViewModel>(
                groups
                    .Where(g => g.Level is SafetyLevel.Safe or SafetyLevel.Caution)
                    .Select(g => new SuggestionGroupViewModel(g, RecomputeSelectedTotal)));

            TreemapRoot = _scanRoot;
            RebuildBreadcrumbs();

            var total = SuggestionGroups.Sum(g => g.Items.Sum(i => i.ReclaimableBytes));
            StatusText = $"Quét xong {SelectedDrive} — " +
                $"{BytesToHumanReadableConverter.Format(_scanRoot.AggregateSize)} · " +
                $"có thể thu hồi tới {BytesToHumanReadableConverter.Format(total)}.";
            UpdateInaccessibleInfo();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Đã hủy quét.";
        }
        catch (Exception ex)
        {
            StatusText = "Quét lỗi.";
            await _dialogService.ShowErrorAsync("Lỗi quét", ex.Message);
        }
        finally
        {
            IsScanning = false;
            IsLoadingResults = false;
            _scanCts.Dispose();
            _scanCts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(IsScanning))]
    private void CancelScan() => _scanCts?.Cancel();

    private bool CanDelete() => !IsScanning && TotalSelectedBytes > 0;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteSelectedAsync()
    {
        var selected = SuggestionGroups
            .SelectMany(g => g.Items)
            .Where(i => i.IsSelected)
            .ToList();
        if (selected.Count == 0)
            return;

        var response = await _dialogService.ConfirmDeleteAsync(new ConfirmDeleteRequest(
            selected.Count,
            selected.Sum(i => i.ReclaimableBytes),
            selected.Any(i => i.NeedsAdmin && !IsAdministrator)));
        if (!response.Confirmed)
            return;

        // Item cần admin (khi app chưa nâng quyền) xử lý riêng: relaunch UAC từng tác vụ
        List<SuggestionItemViewModel> adminItems =
            IsAdministrator ? [] : selected.Where(i => i.NeedsAdmin).ToList();
        var normalItems = selected.Except(adminItems).ToList();

        // DeleteContentsOnly (vd %TEMP%): xóa nội dung bên trong, giữ thư mục.
        // Recycle Bin root thì giữ nguyên — DeletionService special-case bằng SHEmptyRecycleBin.
        var paths = new List<string>();
        foreach (var item in normalItems)
        {
            if (item.Suggestion.DeleteContentsOnly && !IsRecycleBinPath(item.FullPath))
                paths.AddRange(SafeEnumerateTopLevel(item.FullPath));
            else
                paths.Add(item.FullPath);
        }

        IsScanning = true; // khóa UI trong lúc xóa
        long freedActual = 0;
        int failed = 0, locked = 0;
        var progress = new Progress<DeleteResult>(r =>
        {
            if (r.Success)
                freedActual += r.BytesFreed;
            else if (r.WasLocked)
                locked++;
            else
                failed++;
            StatusText = $"Đang xóa... {r.Path}";
        });

        var completedItems = new List<SuggestionItemViewModel>(normalItems);
        int uacCanceled = 0, adminFailed = 0;
        bool offerRescan = false;
        try
        {
            if (paths.Count > 0)
                await _deletionService.DeleteAsync(paths, new DeleteOptions { Mode = response.Mode },
                    progress, CancellationToken.None);

            // Tác vụ admin: relaunch nâng quyền RIÊNG TỪNG tác vụ, chờ kết quả
            foreach (var item in adminItems)
            {
                StatusText = $"🛡 Đang chạy nâng quyền: {item.DisplayName}...";
                var task = ElevatedTask.CleanRule(item.Suggestion.Rule.Id, response.Mode);
                using var process = ElevationHelper.RelaunchElevated(task);
                if (process is null)
                {
                    uacCanceled++; // user bấm No trên UAC — giữ item lại
                    continue;
                }
                await process.WaitForExitAsync();
                if (process.ExitCode == 0)
                    completedItems.Add(item);
                else
                    adminFailed++;
            }

            foreach (var group in SuggestionGroups)
                foreach (var item in completedItems.Where(group.Items.Contains).ToList())
                    group.Items.Remove(item);
            RecomputeSelectedTotal();

            var suggestionBytes = completedItems.Sum(i => i.ReclaimableBytes);
            StatusText = response.Mode == DeleteMode.RecycleBin
                ? $"Đã chuyển ~{BytesToHumanReadableConverter.Format(suggestionBytes)} vào Thùng rác — dọn Thùng rác để thực sự thu hồi."
                : $"Đã giải phóng ~{BytesToHumanReadableConverter.Format(suggestionBytes)}.";
            if (failed + locked > 0)
                StatusText += $" ({failed} lỗi, {locked} mục bị khóa — bỏ qua)";
            if (uacCanceled > 0)
                StatusText += $" ({uacCanceled} tác vụ admin bị hủy UAC)";
            if (adminFailed > 0)
                StatusText += $" ({adminFailed} tác vụ admin lỗi)";

            offerRescan = completedItems.Count > 0;
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Lỗi khi xóa", ex.Message);
        }
        finally
        {
            IsScanning = false;
        }

        // Tùy chọn quét lại để treemap + gợi ý phản ánh trạng thái mới
        if (offerRescan &&
            await _dialogService.ConfirmAsync("Quét lại", "Quét lại ổ để cập nhật treemap và danh sách gợi ý?"))
        {
            await ScanAsync();
        }
    }

    // ── Dọn Thùng rác — bước "thực sự thu hồi" sau khi xóa vào Recycle Bin ─

    private bool CanEmptyRecycleBin() => !IsScanning;

    [RelayCommand(CanExecute = nameof(CanEmptyRecycleBin))]
    private async Task EmptyRecycleBinAsync()
    {
        var drive = SelectedDrive;
        var (bytes, items) = RecycleBinUtil.Query(drive);
        if (items == 0)
        {
            await _dialogService.ShowInfoAsync("Thùng rác", $"Thùng rác ổ {drive} đang rỗng.");
            return;
        }

        var confirmed = await _dialogService.ConfirmAsync("Dọn Thùng rác",
            $"Thùng rác ổ {drive} đang chứa {items:N0} mục · {BytesToHumanReadableConverter.Format(bytes)}.\n\n" +
            "Dọn sạch? Sau bước này KHÔNG khôi phục được nữa.");
        if (!confirmed)
            return;

        StatusText = "Đang dọn Thùng rác...";
        var ok = await Task.Run(() => RecycleBinUtil.Empty(drive));
        StatusText = ok
            ? $"Đã dọn Thùng rác — thực sự giải phóng {BytesToHumanReadableConverter.Format(bytes)}."
            : "Dọn Thùng rác thất bại.";
    }

    // ── WinSxS — "Advanced system cleanup" riêng, không tự gợi ý ─────────

    private bool CanCleanWinSxs() => !IsScanning;

    [RelayCommand(CanExecute = nameof(CanCleanWinSxs))]
    private async Task CleanWinSxsAsync()
    {
        var confirmed = await _dialogService.ConfirmAsync("Dọn WinSxS",
            "Chạy DISM StartComponentCleanup để dọn các thành phần Windows cũ trong WinSxS.\n\n" +
            "• Cần quyền Administrator (sẽ hiện UAC nếu app chưa nâng quyền)\n" +
            "• Có thể mất vài phút, không hủy giữa chừng được\n\nTiếp tục?");
        if (!confirmed)
            return;

        IsScanning = true;
        StatusText = "🛡 Đang dọn WinSxS (DISM StartComponentCleanup) — có thể mất vài phút...";
        try
        {
            int exitCode;
            if (IsAdministrator)
            {
                exitCode = await SystemCleanupTasks.RunWinSxsCleanupAsync(CancellationToken.None);
            }
            else
            {
                using var process = ElevationHelper.RelaunchElevated(ElevatedTask.WinSxs());
                if (process is null)
                {
                    StatusText = "Đã hủy (UAC).";
                    return;
                }
                await process.WaitForExitAsync();
                exitCode = process.ExitCode;
            }
            StatusText = exitCode == 0
                ? "Dọn WinSxS xong — quét lại để thấy dung lượng thu hồi."
                : $"DISM kết thúc với mã lỗi {exitCode}.";
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Lỗi WinSxS", ex.Message);
            StatusText = "Dọn WinSxS lỗi.";
        }
        finally
        {
            IsScanning = false;
        }
    }

    /// <summary>
    /// So "đã dùng" theo Windows (cluster cấp phát) với tổng quét được — phần
    /// chênh là vùng không truy cập được (shadow copies, junction bỏ qua,
    /// metadata NTFS, cluster slack...).
    /// </summary>
    private void UpdateInaccessibleInfo()
    {
        try
        {
            if (SelectedDrive is null || _scanRoot is null)
                return;
            var driveInfo = new DriveInfo(SelectedDrive);
            long used = driveInfo.TotalSize - driveInfo.TotalFreeSpace;
            long gap = used - _scanRoot.AggregateSize;
            InaccessibleInfo = gap > 512L * 1024 * 1024
                ? $"≈ {BytesToHumanReadableConverter.Format(gap)} không truy cập được " +
                  "(vùng hệ thống: System Restore/shadow copies, junction, metadata NTFS, cluster slack)."
                : null;
        }
        catch (Exception)
        {
            InaccessibleInfo = null;
        }
    }

    private void RecomputeSelectedTotal() =>
        TotalSelectedBytes = SuggestionGroups
            .SelectMany(g => g.Items)
            .Where(i => i.IsSelected)
            .Sum(i => i.ReclaimableBytes);

    private static bool IsRecycleBinPath(string path) =>
        string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(path)),
            "$Recycle.Bin", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> SafeEnumerateTopLevel(string directory)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(directory).ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }
}

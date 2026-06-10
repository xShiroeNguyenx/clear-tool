using System.Diagnostics;

namespace ClearTool.Core.Admin;

/// <summary>
/// Các tác vụ dọn hệ thống cần admin — chạy trong instance đã nâng quyền
/// (Phase 3). WinSxS dùng StartComponentCleanup, KHÔNG dùng /ResetBase
/// (hay treo, không hoàn tác được).
/// </summary>
public static class SystemCleanupTasks
{
    public const string WinSxsTask = "winsxs-cleanup";

    /// <summary>DISM /Online /Cleanup-Image /StartComponentCleanup</summary>
    public static async Task<int> RunWinSxsCleanupAsync(CancellationToken cancellationToken)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "dism.exe",
            Arguments = "/Online /Cleanup-Image /StartComponentCleanup",
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("Không khởi động được DISM.");

        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }
}

using Microsoft.VisualBasic.FileIO;

namespace ClearTool.Core.Deletion;

public sealed class DeletionService(ProtectedRoots protectedRoots) : IDeletionService
{
    public Task<IReadOnlyList<DeleteResult>> DeleteAsync(
        IEnumerable<string> paths,
        DeleteOptions options,
        IProgress<DeleteResult>? progress,
        CancellationToken cancellationToken)
        // IFileOperation yêu cầu STA — chạy batch trên thread STA riêng
        => RunOnStaThread<IReadOnlyList<DeleteResult>>(() =>
        {
            var results = new List<DeleteResult>();
            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = DeleteOne(path, options.Mode);
                results.Add(result);
                progress?.Report(result);
            }
            return results;
        });

    private DeleteResult DeleteOne(string rawPath, DeleteMode mode)
    {
        string path;
        try
        {
            path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rawPath));
        }
        catch (Exception ex)
        {
            return new DeleteResult { Path = rawPath, Success = false, Error = $"Path không hợp lệ: {ex.Message}" };
        }

        // ── Hàng rào an toàn — KHÔNG có ngoại lệ ──────────────────────────
        if (protectedRoots.IsProtected(path, out var reason))
            return new DeleteResult { Path = path, Success = false, Error = $"Bị chặn bởi ProtectedRoots: {reason}" };

        if (IsRecycleBinRoot(path))
            return EmptyRecycleBin(path);

        bool isDirectory = Directory.Exists(path);
        if (!isDirectory && !File.Exists(path))
            return new DeleteResult { Path = path, Success = false, Error = "Không tồn tại (đã bị xóa trước đó?)" };

        long size = 0;
        if (!isDirectory)
        {
            try { size = new FileInfo(path).Length; } catch { }
        }

        try
        {
            int hr = ShellFileOperation.Delete(path, toRecycleBin: mode == DeleteMode.RecycleBin);
            if (hr == ShellFileOperation.S_OK)
                return new DeleteResult { Path = path, Success = true, BytesFreed = size };

            // Shell silent-abort không cho HRESULT gốc — tự dò xem file có bị khóa không
            bool wasLocked = ShellFileOperation.IsSharingViolation(hr)
                || (!isDirectory && IsFileLocked(path));
            return new DeleteResult
            {
                Path = path,
                Success = false,
                Error = wasLocked ? "File đang bị process khác giữ" : ShellFileOperation.DescribeError(hr),
                WasLocked = wasLocked,
            };
        }
        catch (Exception comEx) when (comEx is System.Runtime.InteropServices.COMException
            or InvalidCastException or PlatformNotSupportedException)
        {
            // Shell COM không khả dụng (hiếm) — fallback VB FileIO
            return DeleteWithVbFallback(path, isDirectory, size, mode);
        }
    }

    private static DeleteResult DeleteWithVbFallback(string path, bool isDirectory, long size, DeleteMode mode)
    {
        var recycle = mode == DeleteMode.Permanent
            ? RecycleOption.DeletePermanently
            : RecycleOption.SendToRecycleBin;
        try
        {
            if (isDirectory)
                FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, recycle);
            else
                FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, recycle);
            return new DeleteResult { Path = path, Success = true, BytesFreed = size };
        }
        catch (IOException ex)
        {
            return new DeleteResult { Path = path, Success = false, Error = ex.Message, WasLocked = true };
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or OperationCanceledException)
        {
            return new DeleteResult { Path = path, Success = false, Error = ex.Message };
        }
    }

    /// <summary>File vẫn tồn tại và không mở exclusive được → đang bị giữ.</summary>
    private static bool IsFileLocked(string path)
    {
        if (!File.Exists(path))
            return false;
        try
        {
            using var _ = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ── Recycle Bin: special-case — dọn bằng SHEmptyRecycleBin, không "xóa
    //    vào thùng rác" chính nội dung thùng rác ─────────────────────────────
    private static bool IsRecycleBinRoot(string path) =>
        string.Equals(Path.GetFileName(path), "$Recycle.Bin", StringComparison.OrdinalIgnoreCase);

    private static DeleteResult EmptyRecycleBin(string recycleBinPath)
    {
        var driveRoot = Path.GetPathRoot(recycleBinPath);
        var (bytes, _) = RecycleBinUtil.Query(driveRoot);
        return RecycleBinUtil.Empty(driveRoot)
            ? new DeleteResult { Path = recycleBinPath, Success = true, BytesFreed = bytes }
            : new DeleteResult { Path = recycleBinPath, Success = false, Error = "SHEmptyRecycleBin thất bại" };
    }

    private static Task<T> RunOnStaThread<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(work()); }
            catch (Exception ex) { tcs.SetException(ex); }
        })
        {
            IsBackground = true,
            Name = "ClearTool.Deletion",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}

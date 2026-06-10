using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace ClearTool.Core.Admin;

public static class ElevationHelper
{
    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Mở lại TOÀN BỘ app với quyền admin (UI bình thường, không phải elevated-task).
    /// Trả về false nếu user hủy UAC — caller giữ nguyên instance hiện tại.
    /// </summary>
    public static bool TryRestartElevated()
    {
        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Không xác định được đường dẫn exe.");
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas",
            });
            return true;
        }
        catch (Win32Exception)
        {
            return false; // user hủy UAC
        }
    }

    /// <summary>
    /// Relaunch chính app với quyền admin để chạy đúng MỘT tác vụ.
    /// Trả về Process để caller chờ kết quả (exit code), hoặc null nếu user hủy UAC.
    /// </summary>
    public static Process? RelaunchElevated(ElevatedTask task)
    {
        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Không xác định được đường dẫn exe.");
        try
        {
            return Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas",
                Arguments = task.ToArgumentString(),
            });
        }
        catch (Win32Exception)
        {
            return null; // user hủy UAC
        }
    }
}

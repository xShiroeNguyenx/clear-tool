using System.Runtime.InteropServices;

namespace ClearTool.Core.Deletion;

/// <summary>Truy vấn / dọn Thùng rác qua shell API.</summary>
public static class RecycleBinUtil
{
    /// <summary>Tổng dung lượng + số mục trong Thùng rác (driveRoot null = mọi ổ).</summary>
    public static (long Bytes, long Items) Query(string? driveRoot)
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        int hr = SHQueryRecycleBin(driveRoot, ref info);
        return hr == 0 ? (info.i64Size, info.i64NumItems) : (0, 0);
    }

    /// <summary>Dọn sạch Thùng rác. Trả về true nếu thành công (kể cả khi đã rỗng).</summary>
    public static bool Empty(string? driveRoot)
    {
        int hr = SHEmptyRecycleBin(IntPtr.Zero, driveRoot,
            SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
        // E_UNEXPECTED khi thùng rác đã rỗng — coi là thành công
        return hr is 0 or unchecked((int)0x8000FFFF);
    }

    private const uint SHERB_NOCONFIRMATION = 0x1;
    private const uint SHERB_NOPROGRESSUI = 0x2;
    private const uint SHERB_NOSOUND = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);
}

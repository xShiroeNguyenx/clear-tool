using System.Runtime.InteropServices;

namespace ClearTool.Core.Deletion;

/// <summary>
/// Xóa file/thư mục qua shell IFileOperation với FOF_SILENT | FOF_NOERRORUI —
/// không bao giờ bật dialog của Explorer giữa batch (khác VB FileIO).
/// LƯU Ý: gọi trên thread STA (DeletionService lo việc này).
/// </summary>
internal static class ShellFileOperation
{
    public const int S_OK = 0;
    /// <summary>ERROR_SHARING_VIOLATION — file đang bị process khác giữ.</summary>
    public const int HR_SHARING_VIOLATION = unchecked((int)0x80070020);
    /// <summary>COPYENGINE_E_SHARING_VIOLATION_SRC.</summary>
    public const int HR_COPYENGINE_SHARING_SRC = unchecked((int)0x8027000A);
    /// <summary>Mã tự đặt khi shell báo "operation aborted" mà không có HRESULT lỗi.</summary>
    public const int HR_OPERATION_ABORTED = unchecked((int)0x80270001);

    private const uint FOF_SILENT = 0x0004;
    private const uint FOF_NOCONFIRMATION = 0x0010;
    private const uint FOF_ALLOWUNDO = 0x0040;
    private const uint FOF_NOERRORUI = 0x0400;

    /// <summary>Trả về HRESULT (S_OK = thành công). Ném COMException nếu không tạo được shell object.</summary>
    public static int Delete(string path, bool toRecycleBin)
    {
        var operation = (IFileOperation)new FileOperationClass();
        try
        {
            uint flags = FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOERRORUI;
            if (toRecycleBin)
                flags |= FOF_ALLOWUNDO;
            operation.SetOperationFlags(flags);

            var shellItemIid = typeof(IShellItem).GUID;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref shellItemIid, out var item);
            try
            {
                operation.DeleteItem(item, IntPtr.Zero);
                int hr = operation.PerformOperations();
                operation.GetAnyOperationsAborted(out bool aborted);
                if (hr == S_OK && aborted)
                    hr = HR_OPERATION_ABORTED;
                return hr;
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(operation);
        }
    }

    public static bool IsSharingViolation(int hr) =>
        hr is HR_SHARING_VIOLATION or HR_COPYENGINE_SHARING_SRC;

    public static string DescribeError(int hr) => hr switch
    {
        // Shell với FOF_NOERRORUI thường chỉ báo "aborted" mà không cho HRESULT gốc
        HR_OPERATION_ABORTED => "Shell từ chối xóa (file đang bị khóa hoặc thiếu quyền)",
        _ => Marshal.GetExceptionForHR(hr)?.Message ?? $"HRESULT 0x{hr:X8}",
    };

    // ── COM interop ───────────────────────────────────────────────────────

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string pszPath, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

    [ComImport, Guid("3ad05575-8857-4850-9277-11b85bdb8e09")]
    private class FileOperationClass;

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport, Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        void Advise(IntPtr pfops, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOperationFlags(uint dwOperationFlags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string pszMessage);
        void SetProgressDialog(IntPtr popd);
        void SetProperties(IntPtr pproparray);
        void SetOwnerWindow(IntPtr hwndOwner);
        void ApplyPropertiesToItem(IShellItem psiItem);
        void ApplyPropertiesToItems(IntPtr punkItems);
        void RenameItem(IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, IntPtr pfopsItem);
        void RenameItems(IntPtr punkItems, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
        void MoveItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, IntPtr pfopsItem);
        void MoveItems(IntPtr punkItems, IShellItem psiDestinationFolder);
        void CopyItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszCopyName, IntPtr pfopsItem);
        void CopyItems(IntPtr punkItems, IShellItem psiDestinationFolder);
        void DeleteItem(IShellItem psiItem, IntPtr pfopsItem);
        void DeleteItems(IntPtr punkItems);
        void NewItem(IShellItem psiDestinationFolder, uint dwFileAttributes, [MarshalAs(UnmanagedType.LPWStr)] string pszName, [MarshalAs(UnmanagedType.LPWStr)] string? pszTemplateName, IntPtr pfopsItem);
        [PreserveSig] int PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool fAnyOperationsAborted);
    }
}

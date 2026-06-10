namespace ClearTool.Core.Deletion;

public enum DeleteMode
{
    /// <summary>Mặc định — đưa vào Thùng rác, khôi phục được (KHÔNG giải phóng dung lượng cho tới khi dọn Thùng rác).</summary>
    RecycleBin,

    /// <summary>Xóa vĩnh viễn — giải phóng dung lượng ngay, không khôi phục được.</summary>
    Permanent,
}

public sealed class DeleteOptions
{
    public DeleteMode Mode { get; init; } = DeleteMode.RecycleBin;
}

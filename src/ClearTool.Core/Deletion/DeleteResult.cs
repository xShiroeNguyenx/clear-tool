namespace ClearTool.Core.Deletion;

public sealed class DeleteResult
{
    public required string Path { get; init; }
    public required bool Success { get; init; }

    /// <summary>Best-effort: size file đo trước khi xóa; directory = 0 (caller dùng size từ suggestion).</summary>
    public long BytesFreed { get; init; }

    public string? Error { get; init; }

    /// <summary>File đang bị process khác giữ — batch vẫn tiếp tục.</summary>
    public bool WasLocked { get; init; }
}

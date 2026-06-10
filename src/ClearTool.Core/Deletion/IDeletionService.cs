namespace ClearTool.Core.Deletion;

public interface IDeletionService
{
    /// <summary>
    /// Xóa từng path trong batch; lỗi từng item không phá batch.
    /// Mọi path đi qua hàng rào ProtectedRoots TRƯỚC khi xóa.
    /// </summary>
    Task<IReadOnlyList<DeleteResult>> DeleteAsync(
        IEnumerable<string> paths,
        DeleteOptions options,
        IProgress<DeleteResult>? progress,
        CancellationToken cancellationToken);
}

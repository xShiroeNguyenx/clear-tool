using ClearTool.Core.Model;

namespace ClearTool.Core.Scanning;

public interface IFileSystemScanner
{
    /// <summary>
    /// Quét toàn bộ cây dưới <paramref name="rootPath"/> và trả về root
    /// <see cref="TreeNode"/> đã cộng dồn AggregateSize.
    /// </summary>
    Task<TreeNode> ScanAsync(
        string rootPath,
        ScanOptions options,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken);
}

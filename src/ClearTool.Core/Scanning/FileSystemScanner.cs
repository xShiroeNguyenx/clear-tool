using System.Diagnostics;
using System.IO.Enumeration;
using ClearTool.Core.Model;

namespace ClearTool.Core.Scanning;

public sealed class FileSystemScanner : IFileSystemScanner
{
    /// <summary>
    /// OneDrive Files On-Demand placeholder (FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS):
    /// size logic nhưng không chiếm đĩa thật — không tính vào dung lượng.
    /// </summary>
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;

    public Task<TreeNode> ScanAsync(
        string rootPath,
        ScanOptions options,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
        => Task.Run(() => Scan(rootPath, options, progress, cancellationToken), cancellationToken);

    private static TreeNode Scan(
        string rootPath,
        ScanOptions options,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        // Root ổ đĩa ("C:") cần giữ dấu "\" để Path.Join hoạt động đúng
        if (canonicalRoot.EndsWith(':'))
            canonicalRoot += Path.DirectorySeparatorChar;

        var root = new TreeNode { Name = canonicalRoot, Kind = NodeKind.Directory };

        // Dictionary chỉ chứa THƯ MỤC (file không bao giờ làm parent),
        // vứt bỏ khi hàm kết thúc — không giữ tham chiếu lâu dài.
        var directories = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase)
        {
            [canonicalRoot] = root,
        };

        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = 0, // mặc định bỏ Hidden/System — ta muốn thấy tất cả
        };

        using var enumerator = new SafeEnumerator(canonicalRoot, enumerationOptions);

        long entriesScanned = 0;
        long bytesSeen = 0;
        var stopwatch = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;

        while (enumerator.MoveNext())
        {
            var entry = enumerator.Current;
            entriesScanned++;

            if (entriesScanned % options.CancellationCheckInterval == 0)
                cancellationToken.ThrowIfCancellationRequested();

            var node = AddEntry(directories, root, canonicalRoot, entry);
            bytesSeen += node.OwnSize;

            var elapsed = stopwatch.Elapsed;
            if (progress is not null && elapsed - lastReport >= options.ProgressInterval)
            {
                lastReport = elapsed;
                progress.Report(new ScanProgress(entriesScanned, bytesSeen, entry.FullPath));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        root.ComputeAggregateSizes();
        progress?.Report(new ScanProgress(entriesScanned, root.AggregateSize, canonicalRoot));
        return root;
    }

    private static TreeNode AddEntry(
        Dictionary<string, TreeNode> directories,
        TreeNode root,
        string canonicalRoot,
        in ScanEntry entry)
    {
        var parentPath = Path.GetDirectoryName(entry.FullPath);
        var parent = parentPath is null
            ? root
            : GetOrCreateDirectory(directories, root, canonicalRoot, parentPath);

        if (entry.IsDirectory)
        {
            var dir = new TreeNode
            {
                Name = Path.GetFileName(entry.FullPath),
                Kind = NodeKind.Directory,
                Parent = parent,
                Attributes = entry.Attributes,
            };
            parent.Children.Add(dir);
            directories[entry.FullPath] = dir;
            return dir;
        }

        var isPlaceholder = (entry.Attributes & RecallOnDataAccess) != 0;
        var file = new TreeNode
        {
            Name = Path.GetFileName(entry.FullPath),
            Kind = NodeKind.File,
            Parent = parent,
            Attributes = entry.Attributes,
            OwnSize = isPlaceholder ? 0 : entry.Length,
        };
        parent.Children.Add(file);
        return file;
    }

    /// <summary>
    /// Bình thường parent luôn có sẵn (enumerator duyệt cha trước con); nhánh
    /// tạo-chuỗi chỉ là lưới an toàn khi entry cha bị skip vì lỗi.
    /// </summary>
    private static TreeNode GetOrCreateDirectory(
        Dictionary<string, TreeNode> directories,
        TreeNode root,
        string canonicalRoot,
        string path)
    {
        if (directories.TryGetValue(path, out var existing))
            return existing;

        var parentPath = Path.GetDirectoryName(path);
        var parent = parentPath is null || path.Equals(canonicalRoot, StringComparison.OrdinalIgnoreCase)
            ? root
            : GetOrCreateDirectory(directories, root, canonicalRoot, parentPath);

        if (ReferenceEquals(parent, root) && path.Equals(canonicalRoot, StringComparison.OrdinalIgnoreCase))
            return root;

        var dir = new TreeNode
        {
            Name = Path.GetFileName(path),
            Kind = NodeKind.Directory,
            Parent = parent,
        };
        parent.Children.Add(dir);
        directories[path] = dir;
        return dir;
    }

    private sealed class SafeEnumerator(string root, EnumerationOptions options)
        : FileSystemEnumerator<ScanEntry>(root, options)
    {
        protected override ScanEntry TransformEntry(ref FileSystemEntry entry) =>
            new(entry.ToFullPath(), entry.IsDirectory ? 0 : entry.Length, entry.Attributes, entry.IsDirectory);

        // Bỏ qua reparse point (junction/symlink) — tránh đệ quy vô hạn
        protected override bool ShouldRecurseIntoEntry(ref FileSystemEntry entry) =>
            (entry.Attributes & FileAttributes.ReparsePoint) == 0;

        // Nuốt access-denied/sharing-violation từng entry, quét tiếp
        protected override bool ContinueOnError(int error) => true;
    }
}

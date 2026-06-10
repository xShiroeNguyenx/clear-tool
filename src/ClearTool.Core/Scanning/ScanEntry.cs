namespace ClearTool.Core.Scanning;

public readonly record struct ScanEntry(
    string FullPath,
    long Length,
    FileAttributes Attributes,
    bool IsDirectory);

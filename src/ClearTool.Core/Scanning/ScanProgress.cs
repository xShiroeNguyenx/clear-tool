namespace ClearTool.Core.Scanning;

public readonly record struct ScanProgress(
    long EntriesScanned,
    long BytesSeen,
    string CurrentPath);

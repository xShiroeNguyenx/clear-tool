namespace ClearTool.Core.Scanning;

public sealed class ScanOptions
{
    /// <summary>MVP chạy đơn luồng; giữ chỗ cho fan-out Parallel.ForEach sau này.</summary>
    public int DegreeOfParallelism { get; init; } = 1;

    /// <summary>Số entry giữa hai lần kiểm tra cancellation.</summary>
    public int CancellationCheckInterval { get; init; } = 1024;

    /// <summary>Khoảng cách tối thiểu giữa hai lần báo tiến độ.</summary>
    public TimeSpan ProgressInterval { get; init; } = TimeSpan.FromMilliseconds(100);
}

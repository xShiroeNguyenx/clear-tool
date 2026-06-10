using System.Diagnostics;
using ClearTool.Core.Model;
using ClearTool.Core.Scanning;
using FluentAssertions;
using Xunit;

namespace ClearTool.Core.Tests;

/// <summary>Quét trên sandbox tạm với cấu trúc biết trước.</summary>
public sealed class ScannerTests : IDisposable
{
    private readonly string _sandbox;
    private readonly FileSystemScanner _scanner = new();

    public ScannerTests()
    {
        _sandbox = Path.Join(Path.GetTempPath(), "ClearTool.ScanTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
    }

    public void Dispose()
    {
        try { Directory.Delete(_sandbox, recursive: true); } catch { }
    }

    private void CreateFile(string relPath, int size)
    {
        var path = Path.Join(_sandbox, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
    }

    [Fact]
    public async Task Scan_ComputesAggregateSizes_Correctly()
    {
        CreateFile(@"a\f1.bin", 100);
        CreateFile(@"a\b\f2.bin", 50);
        CreateFile(@"f3.bin", 25);

        var root = await _scanner.ScanAsync(_sandbox, new ScanOptions(), null, CancellationToken.None);

        root.AggregateSize.Should().Be(175);
        var a = root.Children.Single(c => c.Name == "a");
        a.AggregateSize.Should().Be(150);
        a.Children.Single(c => c.Name == "b").AggregateSize.Should().Be(50);
        a.Kind.Should().Be(NodeKind.Directory);
    }

    [Fact]
    public async Task Scan_JunctionPointingUpward_DoesNotLoopForever()
    {
        CreateFile(@"real\data.bin", 10);
        // Junction trỏ NGƯỢC LÊN sandbox root — nếu không skip reparse sẽ lặp vô hạn
        var junction = Path.Join(_sandbox, @"real\loop");
        var mklink = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /J \"{junction}\" \"{_sandbox}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;
        mklink.WaitForExit();
        mklink.ExitCode.Should().Be(0, "tạo junction phải thành công");

        var scanTask = _scanner.ScanAsync(_sandbox, new ScanOptions(), null, CancellationToken.None);
        var completed = await Task.WhenAny(scanTask, Task.Delay(TimeSpan.FromSeconds(30)));

        completed.Should().Be(scanTask, "scan phải kết thúc, không lặp vô hạn");
        var root = await scanTask;
        root.AggregateSize.Should().Be(10); // junction không được tính thêm lần nào
    }

    [Fact]
    public async Task Scan_GetFullPath_ReconstructsCorrectly()
    {
        CreateFile(@"x\y\z.bin", 1);

        var root = await _scanner.ScanAsync(_sandbox, new ScanOptions(), null, CancellationToken.None);

        var z = root.Children.Single(c => c.Name == "x")
            .Children.Single(c => c.Name == "y")
            .Children.Single(c => c.Name == "z.bin");
        z.GetFullPath().Should().BeEquivalentTo(Path.Join(_sandbox, @"x\y\z.bin"));
    }

    [Fact]
    public async Task Scan_CanceledToken_Throws()
    {
        CreateFile("f.bin", 1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => _scanner.ScanAsync(_sandbox, new ScanOptions(), null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Scan_ReportsProgress()
    {
        for (int i = 0; i < 50; i++)
            CreateFile($@"d{i % 5}\f{i}.bin", 10);

        var reports = new List<ScanProgress>();
        var progress = new SynchronousProgress(reports.Add);

        // ProgressInterval = 0 để chắc chắn có report
        await _scanner.ScanAsync(_sandbox,
            new ScanOptions { ProgressInterval = TimeSpan.Zero }, progress, CancellationToken.None);

        reports.Should().NotBeEmpty();
        reports[^1].BytesSeen.Should().Be(500);
    }

    private sealed class SynchronousProgress(Action<ScanProgress> handler) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) => handler(value);
    }
}

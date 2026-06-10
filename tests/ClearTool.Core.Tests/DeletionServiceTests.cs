using ClearTool.Core.Deletion;
using FluentAssertions;
using Xunit;

namespace ClearTool.Core.Tests;

/// <summary>
/// Chỉ chạy trên sandbox tạm — KHÔNG BAO GIỜ nhắm path thật của user/hệ thống.
/// </summary>
public sealed class DeletionServiceTests : IDisposable
{
    private readonly string _sandbox;
    private readonly DeletionService _service = new(new ProtectedRoots());

    public DeletionServiceTests()
    {
        _sandbox = Path.Join(Path.GetTempPath(), "ClearTool.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
    }

    public void Dispose()
    {
        try { Directory.Delete(_sandbox, recursive: true); } catch { }
    }

    private string CreateFile(string name, int size = 16)
    {
        var path = Path.Join(_sandbox, name);
        File.WriteAllBytes(path, new byte[size]);
        return path;
    }

    [Fact]
    public async Task PermanentDelete_RemovesFile_AndReportsBytes()
    {
        var file = CreateFile("victim.bin", 128);

        var results = await _service.DeleteAsync([file],
            new DeleteOptions { Mode = DeleteMode.Permanent }, null, CancellationToken.None);

        var result = results.Should().ContainSingle().Subject;
        result.Success.Should().BeTrue();
        result.BytesFreed.Should().Be(128);
        File.Exists(file).Should().BeFalse();
    }

    [Fact]
    public async Task PermanentDelete_RemovesDirectoryRecursively()
    {
        var dir = Path.Join(_sandbox, "subdir");
        Directory.CreateDirectory(Path.Join(dir, "nested"));
        File.WriteAllText(Path.Join(dir, "nested", "f.txt"), "x");

        var results = await _service.DeleteAsync([dir],
            new DeleteOptions { Mode = DeleteMode.Permanent }, null, CancellationToken.None);

        results.Single().Success.Should().BeTrue();
        Directory.Exists(dir).Should().BeFalse();
    }

    [Fact]
    public async Task RecycleBinDelete_RemovesFileFromOriginalLocation()
    {
        var file = CreateFile("to-recycle.bin");

        var results = await _service.DeleteAsync([file],
            new DeleteOptions { Mode = DeleteMode.RecycleBin }, null, CancellationToken.None);

        results.Single().Success.Should().BeTrue();
        File.Exists(file).Should().BeFalse(); // đã chuyển vào Thùng rác
    }

    [Fact]
    public async Task LockedFile_ReportsWasLocked_AndBatchContinues()
    {
        var locked = CreateFile("locked.bin");
        var normal = CreateFile("normal.bin");

        using (var _ = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var results = await _service.DeleteAsync([locked, normal],
                new DeleteOptions { Mode = DeleteMode.Permanent }, null, CancellationToken.None);

            results.Should().HaveCount(2);
            var lockedResult = results.Single(r => r.Path.EndsWith("locked.bin"));
            lockedResult.Success.Should().BeFalse();
            lockedResult.WasLocked.Should().BeTrue();

            // Batch vẫn tiếp tục: file thường xóa thành công
            results.Single(r => r.Path.EndsWith("normal.bin")).Success.Should().BeTrue();
        }

        File.Exists(locked).Should().BeTrue();
        File.Exists(normal).Should().BeFalse();
    }

    [Theory]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"C:\Program Files")]
    [InlineData(@"C:\Users")]
    [InlineData(@"C:\")]
    [InlineData(@"C:\pagefile.sys")]
    public async Task ProtectedPaths_AreRefused_EvenWhenPassedDirectly(string path)
    {
        var results = await _service.DeleteAsync([path],
            new DeleteOptions { Mode = DeleteMode.Permanent }, null, CancellationToken.None);

        var result = results.Should().ContainSingle().Subject;
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("ProtectedRoots");
    }

    [Fact]
    public async Task NonexistentPath_ReportsError_WithoutThrowing()
    {
        var results = await _service.DeleteAsync([Path.Join(_sandbox, "ghost.bin")],
            new DeleteOptions { Mode = DeleteMode.Permanent }, null, CancellationToken.None);

        results.Single().Success.Should().BeFalse();
    }

    [Fact]
    public async Task Progress_IsReportedPerItem()
    {
        var a = CreateFile("a.bin");
        var b = CreateFile("b.bin");
        var reported = new List<DeleteResult>();
        var progress = new SynchronousProgress(reported.Add);

        await _service.DeleteAsync([a, b],
            new DeleteOptions { Mode = DeleteMode.Permanent }, progress, CancellationToken.None);

        reported.Should().HaveCount(2);
    }

    private sealed class SynchronousProgress(Action<DeleteResult> handler) : IProgress<DeleteResult>
    {
        public void Report(DeleteResult value) => handler(value);
    }
}

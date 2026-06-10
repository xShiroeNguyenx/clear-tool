using ClearTool.Core.Deletion;
using FluentAssertions;
using Xunit;

namespace ClearTool.Core.Tests;

/// <summary>
/// Test an toàn sống còn: DeletionService không bao giờ được xóa
/// thư mục hệ thống — dù path được truyền vào trực tiếp.
/// </summary>
public class ProtectedRootsGuardTests
{
    private readonly ProtectedRoots _guard = new();

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"C:\Windows\System32\drivers\etc\hosts")]
    [InlineData(@"C:\Program Files")]
    [InlineData(@"C:\Program Files\SomeApp")]
    [InlineData(@"C:\ProgramData")]
    [InlineData(@"C:\Users")]
    [InlineData(@"C:\Recovery")]
    [InlineData(@"C:\System Volume Information")]
    [InlineData(@"C:\pagefile.sys")]
    [InlineData(@"C:\hiberfil.sys")]
    [InlineData(@"C:\swapfile.sys")]
    public void SystemPaths_AreProtected(string path)
    {
        _guard.IsProtected(path, out _).Should().BeTrue(path);
    }

    [Fact]
    public void UserProfileRoot_IsProtected_ButContentInsideIsNot()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        _guard.IsProtected(profile, out _).Should().BeTrue();
        _guard.IsProtected(Path.Join(profile, @"AppData\Local\Temp\junk.tmp"), out _)
            .Should().BeFalse();
    }

    [Fact]
    public void CaseAndTrailingSlash_DoNotBypassGuard()
    {
        _guard.IsProtected(@"c:\WINDOWS\", out _).Should().BeTrue();
        _guard.IsProtected(@"C:\PROGRAM FILES\", out _).Should().BeTrue();
    }

    [Fact]
    public void RelativeSegments_DoNotBypassGuard()
    {
        // Canonicalize phải giải "..\" trước khi so
        _guard.IsProtected(@"C:\Temp\..\Windows", out _).Should().BeTrue();
    }

    [Fact]
    public void AllowedSubtrees_InsideWindows_AreDeletable_ButNotThemselves()
    {
        var winDir = Environment.GetEnvironmentVariable("WINDIR")!;

        // Chính thư mục Temp/Download: KHÔNG xóa được
        _guard.IsProtected(Path.Join(winDir, "Temp"), out _).Should().BeTrue();
        _guard.IsProtected(Path.Join(winDir, @"SoftwareDistribution\Download"), out _).Should().BeTrue();

        // Nội dung bên trong: xóa được
        _guard.IsProtected(Path.Join(winDir, @"Temp\stale.tmp"), out _).Should().BeFalse();
        _guard.IsProtected(Path.Join(winDir, @"SoftwareDistribution\Download\update.cab"), out _).Should().BeFalse();

        // Các vùng khác của Windows vẫn bị khóa chặt
        _guard.IsProtected(Path.Join(winDir, @"System32\kernel32.dll"), out _).Should().BeTrue();
    }

    [Fact]
    public void NormalUserPaths_AreNotProtected()
    {
        _guard.IsProtected(@"D:\NGUYENKHANH\some-project\node_modules", out _).Should().BeFalse();
        _guard.IsProtected(Path.Join(Path.GetTempPath(), "whatever"), out _).Should().BeFalse();
    }
}

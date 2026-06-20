using ClearTool.Core.Update;
using FluentAssertions;
using Xunit;

namespace ClearTool.Core.Tests;

/// <summary>
/// So phiên bản là phần "đầu não" của tính năng tự cập nhật — chỉ hiện gợi ý
/// update khi tag GitHub thực sự MỚI HƠN bản đang chạy, nên phải so theo số.
/// </summary>
public class ReleaseVersionTests
{
    [Theory]
    [InlineData("1.0.1", 1, 0, 1)]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("V10.20.30", 10, 20, 30)]
    [InlineData("2", 2, 0, 0)]
    [InlineData("2.5", 2, 5, 0)]
    [InlineData("  v1.0.0  ", 1, 0, 0)]
    [InlineData("1.1.0-beta.2", 1, 1, 0)]   // bỏ pre-release
    [InlineData("1.0.0+build.7", 1, 0, 0)]  // bỏ build metadata
    [InlineData("1.0.1.0", 1, 0, 1)]        // bỏ thành phần thứ 4
    public void TryParse_Valid(string text, int major, int minor, int patch)
    {
        ReleaseVersion.TryParse(text, out var v).Should().BeTrue();
        v.Should().Be(new ReleaseVersion(major, minor, patch));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("v")]
    [InlineData("abc")]
    [InlineData("1.x.0")]
    [InlineData("-1.0.0")]
    public void TryParse_Invalid(string? text)
    {
        ReleaseVersion.TryParse(text, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("1.1.0", "1.0.1", 1)]    // bản mới hơn
    [InlineData("1.0.1", "1.1.0", -1)]
    [InlineData("1.0.1", "1.0.1", 0)]
    [InlineData("2.0.0", "1.9.9", 1)]
    [InlineData("1.10.0", "1.9.0", 1)]   // so theo SỐ, không phải chuỗi ("10" > "9")
    public void CompareTo_OrdersByNumber(string a, string b, int expectedSign)
    {
        ReleaseVersion.TryParse(a, out var va).Should().BeTrue();
        ReleaseVersion.TryParse(b, out var vb).Should().BeTrue();
        Math.Sign(va.CompareTo(vb)).Should().Be(expectedSign);
    }

    [Fact]
    public void Operators_Work()
    {
        ReleaseVersion.TryParse("1.1.0", out var newer);
        ReleaseVersion.TryParse("1.1.0", out var sameAsNewer);
        ReleaseVersion.TryParse("1.0.1", out var older);

        (newer > older).Should().BeTrue();
        (older < newer).Should().BeTrue();
        (newer >= sameAsNewer).Should().BeTrue();
        (older <= newer).Should().BeTrue();
    }

    [Fact]
    public void ToString_IsAlwaysThreePart()
    {
        ReleaseVersion.TryParse("v2.3", out var v);
        v.ToString().Should().Be("2.3.0");
    }
}

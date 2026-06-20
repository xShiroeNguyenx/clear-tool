using System.Globalization;

namespace ClearTool.Core.Update;

/// <summary>
/// Phiên bản kiểu "major.minor.patch" để so bản đang chạy với release mới nhất
/// trên GitHub. Bỏ tiền tố 'v' (vd "v1.2.0") và bỏ phần hậu tố pre-release /
/// build-metadata sau '-' hoặc '+' (vd "1.1.0-beta", "1.0.0+build7").
/// Thuần (pure) — không I/O, unit-test được như phần còn lại của Core.
/// </summary>
public readonly record struct ReleaseVersion(int Major, int Minor, int Patch)
    : IComparable<ReleaseVersion>
{
    /// <summary>
    /// Parse một chuỗi version. Phần thứ tư trở đi (vd "1.0.1.0") bị bỏ qua,
    /// thiếu minor/patch coi như 0. Trả về false nếu rỗng hoặc có thành phần
    /// không phải số nguyên không âm.
    /// </summary>
    public static bool TryParse(string? text, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var s = text.Trim();
        if (s[0] is 'v' or 'V')
            s = s[1..];

        // cắt bỏ "-beta", "+build"...
        int cut = s.IndexOfAny(['-', '+']);
        if (cut >= 0)
            s = s[..cut];

        if (s.Length == 0)
            return false;

        var parts = s.Split('.');
        if (!TryParsePart(parts, 0, out var major)) return false; // major bắt buộc
        if (!TryParsePart(parts, 1, out var minor)) return false;
        if (!TryParsePart(parts, 2, out var patch)) return false;

        version = new ReleaseVersion(major, minor, patch);
        return true;
    }

    // Thiếu (index vượt mảng) → 0 + true; có mặt nhưng không hợp lệ → false.
    private static bool TryParsePart(string[] parts, int index, out int value)
    {
        if (index >= parts.Length)
        {
            value = 0;
            return true;
        }
        return int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    public int CompareTo(ReleaseVersion other)
    {
        int c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        return Patch.CompareTo(other.Patch);
    }

    public static bool operator <(ReleaseVersion a, ReleaseVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(ReleaseVersion a, ReleaseVersion b) => a.CompareTo(b) > 0;
    public static bool operator <=(ReleaseVersion a, ReleaseVersion b) => a.CompareTo(b) <= 0;
    public static bool operator >=(ReleaseVersion a, ReleaseVersion b) => a.CompareTo(b) >= 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}

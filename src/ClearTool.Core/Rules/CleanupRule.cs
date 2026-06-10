namespace ClearTool.Core.Rules;

public sealed class CleanupRule
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required SafetyLevel Level { get; init; }
    public required IPathMatcher Matcher { get; init; }

    /// <summary>Hiện ở panel chi tiết — vì sao xóa được / vì sao phải giữ.</summary>
    public string Reason { get; init; } = "";

    /// <summary>Cần admin để xóa (SoftwareDistribution, %WINDIR%\Temp...).</summary>
    public bool NeedsAdmin { get; init; }

    /// <summary>Cao thắng; KEEP thắng khi hòa.</summary>
    public int Priority { get; init; }

    /// <summary>
    /// Chỉ xóa NỘI DUNG bên trong, giữ lại chính thư mục (vd %TEMP% — xóa
    /// folder Temp sẽ phá app đang chạy).
    /// </summary>
    public bool DeleteContentsOnly { get; init; }
}

using ClearTool.Core.Model;

namespace ClearTool.Core.Rules.Matchers;

/// <summary>
/// Khớp chính xác một vị trí biết trước viết theo biến môi trường,
/// vd "%LOCALAPPDATA%\Temp" — giải biến một lần lúc khởi tạo.
/// </summary>
public sealed class EnvKnownLocationMatcher : IPathMatcher
{
    public EnvKnownLocationMatcher(string template)
    {
        var expanded = Environment.ExpandEnvironmentVariables(template);
        // Biến không tồn tại thì ExpandEnvironmentVariables giữ nguyên "%VAR%"
        ResolvedPath = expanded.Contains('%')
            ? null
            : Path.TrimEndingDirectorySeparator(Path.GetFullPath(expanded));
    }

    /// <summary>
    /// Path tuyệt đối sau khi giải biến (null nếu biến không tồn tại).
    /// RuleEngine dùng để dựng "tunnel" đi xuyên vùng KEEP (vd %WINDIR%\Temp
    /// nằm trong C:\Windows vốn là KEEP).
    /// </summary>
    public string? ResolvedPath { get; }

    public bool Matches(TreeNode node, string fullPath) =>
        ResolvedPath is not null &&
        node.Kind == NodeKind.Directory &&
        string.Equals(Path.TrimEndingDirectorySeparator(fullPath), ResolvedPath, StringComparison.OrdinalIgnoreCase);
}

using ClearTool.Core.Model;

namespace ClearTool.Core.Rules.Matchers;

/// <summary>
/// Khớp chính xác một path tuyệt đối cố định (vd "C:\Windows",
/// "C:\pagefile.sys") — dùng cho nhóm KEEP/SYSTEM.
/// </summary>
public sealed class ExactRootMatcher(string path) : IPathMatcher
{
    private readonly string _path = Path.TrimEndingDirectorySeparator(path);

    public bool Matches(TreeNode node, string fullPath) =>
        string.Equals(Path.TrimEndingDirectorySeparator(fullPath), _path, StringComparison.OrdinalIgnoreCase);
}

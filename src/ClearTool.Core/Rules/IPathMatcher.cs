using ClearTool.Core.Model;

namespace ClearTool.Core.Rules;

public interface IPathMatcher
{
    /// <param name="node">Node đang xét (dùng Kind/Attributes).</param>
    /// <param name="fullPath">Full path đã dựng sẵn ở vòng duyệt — tránh gọi lại GetFullPath().</param>
    bool Matches(TreeNode node, string fullPath);
}

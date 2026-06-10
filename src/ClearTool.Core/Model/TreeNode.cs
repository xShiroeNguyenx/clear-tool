using ClearTool.Core.Rules;

namespace ClearTool.Core.Model;

/// <summary>
/// Một node trong cây kết quả quét. KHÔNG lưu FullPath per-node để tiết kiệm
/// bộ nhớ với cây 1–2 triệu node — dựng lại từ chuỗi Parent khi cần.
/// </summary>
public sealed class TreeNode
{
    public required string Name { get; init; }
    public required NodeKind Kind { get; init; }

    /// <summary>Kích thước của chính file này (directory = 0).</summary>
    public long OwnSize { get; set; }

    /// <summary>OwnSize + tổng toàn bộ con (gán ở bước post-order sau khi quét).</summary>
    public long AggregateSize { get; set; }

    public TreeNode? Parent { get; set; }
    public List<TreeNode> Children { get; } = [];

    public FileAttributes Attributes { get; set; }

    /// <summary>Do RuleEngine gán sau khi quét.</summary>
    public SafetyLevel Safety { get; set; } = SafetyLevel.Unknown;

    public string GetFullPath()
    {
        if (Parent is null)
            return Name; // root lưu path đầy đủ, vd "C:\"

        var segments = new Stack<string>();
        var node = this;
        while (node.Parent is not null)
        {
            segments.Push(node.Name);
            node = node.Parent;
        }

        var path = node.Name;
        foreach (var segment in segments)
            path = Path.Join(path, segment);
        return path;
    }

    /// <summary>Cộng dồn AggregateSize từ lá lên (gọi một lần trên root sau khi quét).</summary>
    public void ComputeAggregateSizes()
    {
        // Post-order tường minh bằng stack — cây thật sâu, đệ quy dễ StackOverflow.
        var stack = new Stack<(TreeNode Node, bool ChildrenDone)>();
        stack.Push((this, false));
        while (stack.Count > 0)
        {
            var (node, childrenDone) = stack.Pop();
            if (childrenDone)
            {
                long total = node.OwnSize;
                foreach (var child in node.Children)
                    total += child.AggregateSize;
                node.AggregateSize = total;
            }
            else
            {
                stack.Push((node, true));
                foreach (var child in node.Children)
                    stack.Push((child, false));
            }
        }
    }
}

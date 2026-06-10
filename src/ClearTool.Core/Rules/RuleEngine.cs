using ClearTool.Core.Model;
using ClearTool.Core.Rules.Matchers;

namespace ClearTool.Core.Rules;

/// <summary>
/// Duyệt cây pre-order, gán SafetyLevel và phát CleanupSuggestion.
/// Engine THUẦN (chỉ dùng path + attributes) — unit test không cần ổ đĩa thật.
///
/// Ngữ nghĩa:
/// - Nhiều rule khớp → Priority cao thắng; KEEP thắng khi hòa.
/// - KEEP: gán Keep cho node, KHÔNG duyệt con — TRỪ các "tunnel": chuỗi thư mục
///   tổ tiên của những vị trí dọn được nằm trong vùng KEEP (vd %WINDIR%\Temp
///   trong C:\Windows). Node trên tunnel vẫn được duyệt để chạm tới target.
/// - SAFE/CAUTION khớp một thư mục → phát MỘT suggestion cho thư mục đó
///   (reclaimable = AggregateSize), không duyệt tiếp vào trong.
/// - Không khớp gì trong vùng KEEP → kế thừa Keep.
/// </summary>
public sealed class RuleEngine
{
    private readonly IReadOnlyList<CleanupRule> _rules;
    private readonly HashSet<string> _tunnelPaths;

    public RuleEngine(IReadOnlyList<CleanupRule> rules)
    {
        _rules = rules;
        _tunnelPaths = BuildTunnelPaths(rules);
    }

    public IReadOnlyList<SuggestionGroup> Evaluate(TreeNode root)
    {
        var suggestions = new List<CleanupSuggestion>();
        // (node, fullPath, inKeepZone) — duyệt bằng stack, cây thật rất sâu
        var stack = new Stack<(TreeNode Node, string FullPath, bool InKeepZone)>();
        stack.Push((root, NormalizePath(root.Name), false));

        while (stack.Count > 0)
        {
            var (node, fullPath, inKeepZone) = stack.Pop();
            var rule = FindBestRule(node, fullPath);

            if (rule is { Level: SafetyLevel.Keep })
            {
                node.Safety = SafetyLevel.Keep;
                PushChildrenIfTunnel(stack, node, fullPath, inKeepZone: true);
            }
            else if (rule is not null) // Safe / Caution
            {
                node.Safety = rule.Level;
                suggestions.Add(new CleanupSuggestion
                {
                    FullPath = fullPath,
                    Rule = rule,
                    Node = node,
                    ReclaimableBytes = node.AggregateSize,
                });
                // Không duyệt vào trong — cả subtree đã thuộc suggestion này
            }
            else if (inKeepZone)
            {
                node.Safety = SafetyLevel.Keep; // kế thừa
                PushChildrenIfTunnel(stack, node, fullPath, inKeepZone: true);
            }
            else
            {
                node.Safety = SafetyLevel.Unknown;
                PushChildren(stack, node, fullPath, inKeepZone: false);
            }
        }

        return suggestions
            .GroupBy(s => s.Level)
            .OrderBy(g => g.Key)
            .Select(g => new SuggestionGroup
            {
                Level = g.Key,
                Suggestions = g.OrderByDescending(s => s.ReclaimableBytes).ToList(),
            })
            .ToList();
    }

    private CleanupRule? FindBestRule(TreeNode node, string fullPath)
    {
        CleanupRule? best = null;
        foreach (var rule in _rules)
        {
            if (!rule.Matcher.Matches(node, fullPath))
                continue;
            if (best is null
                || rule.Priority > best.Priority
                || (rule.Priority == best.Priority && rule.Level == SafetyLevel.Keep))
            {
                best = rule;
            }
        }
        return best;
    }

    private void PushChildren(
        Stack<(TreeNode, string, bool)> stack, TreeNode node, string fullPath, bool inKeepZone)
    {
        foreach (var child in node.Children)
            stack.Push((child, Path.Join(fullPath, child.Name), inKeepZone));
    }

    /// <summary>Trong vùng KEEP chỉ duyệt tiếp các con nằm trên tunnel.</summary>
    private void PushChildrenIfTunnel(
        Stack<(TreeNode, string, bool)> stack, TreeNode node, string fullPath, bool inKeepZone)
    {
        if (!_tunnelPaths.Contains(NormalizePath(fullPath)))
            return;
        foreach (var child in node.Children)
            stack.Push((child, Path.Join(fullPath, child.Name), inKeepZone));
    }

    /// <summary>
    /// Tập mọi thư mục tổ tiên (kể cả chính nó) của các vị trí known-location
    /// dọn được — cho phép xuyên qua vùng KEEP để chạm tới target.
    /// </summary>
    private static HashSet<string> BuildTunnelPaths(IReadOnlyList<CleanupRule> rules)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            if (rule.Level == SafetyLevel.Keep)
                continue;
            if (rule.Matcher is not EnvKnownLocationMatcher { ResolvedPath: { } target })
                continue;

            // Thêm chính target và mọi tổ tiên — node target sẽ match rule
            // trước khi cần tunnel tiếp, nhưng thêm cả nó vô hại.
            for (string? p = target; p is not null; p = Path.GetDirectoryName(p))
                set.Add(NormalizePath(p));
        }
        return set;
    }

    private static string NormalizePath(string path) =>
        Path.TrimEndingDirectorySeparator(path);
}

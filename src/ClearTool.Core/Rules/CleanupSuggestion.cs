using ClearTool.Core.Model;

namespace ClearTool.Core.Rules;

public sealed class CleanupSuggestion
{
    public required string FullPath { get; init; }
    public required CleanupRule Rule { get; init; }
    public required TreeNode Node { get; init; }

    /// <summary>Dung lượng thu hồi được = AggregateSize của node tại thời điểm đánh giá.</summary>
    public required long ReclaimableBytes { get; init; }

    public SafetyLevel Level => Rule.Level;
    public string Reason => Rule.Reason;
    public bool NeedsAdmin => Rule.NeedsAdmin;
    public bool DeleteContentsOnly => Rule.DeleteContentsOnly;
}

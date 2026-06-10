namespace ClearTool.Core.Rules;

public sealed class SuggestionGroup
{
    public required SafetyLevel Level { get; init; }
    public required IReadOnlyList<CleanupSuggestion> Suggestions { get; init; }

    public long TotalReclaimableBytes => Suggestions.Sum(s => s.ReclaimableBytes);
}

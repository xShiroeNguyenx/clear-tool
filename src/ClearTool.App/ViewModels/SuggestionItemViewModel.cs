using CommunityToolkit.Mvvm.ComponentModel;
using ClearTool.Core.Rules;

namespace ClearTool.App.ViewModels;

public sealed partial class SuggestionItemViewModel(
    CleanupSuggestion suggestion,
    Action onSelectionChanged) : ObservableObject
{
    public CleanupSuggestion Suggestion { get; } = suggestion;

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => onSelectionChanged();

    public string FullPath => Suggestion.FullPath;
    public string DisplayName => Suggestion.Rule.DisplayName;
    public long ReclaimableBytes => Suggestion.ReclaimableBytes;
    public string Reason => Suggestion.Reason;
    public bool NeedsAdmin => Suggestion.NeedsAdmin;
    public SafetyLevel Level => Suggestion.Level;
}

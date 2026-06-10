using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ClearTool.App.Converters;
using ClearTool.Core.Rules;
using Wpf.Ui.Controls;

namespace ClearTool.App.ViewModels;

public sealed partial class SuggestionGroupViewModel : ObservableObject
{
    public SuggestionGroupViewModel(SuggestionGroup group, Action onSelectionChanged)
    {
        Level = group.Level;
        Items = new ObservableCollection<SuggestionItemViewModel>(
            group.Suggestions.Select(s => new SuggestionItemViewModel(s, onSelectionChanged)));
    }

    public SafetyLevel Level { get; }
    public ObservableCollection<SuggestionItemViewModel> Items { get; }

    public string Header => Level switch
    {
        SafetyLevel.Safe => $"An toàn — {TotalText}",
        SafetyLevel.Caution => $"Cân nhắc — {TotalText}",
        SafetyLevel.Keep => $"Hệ thống (chỉ đọc) — {TotalText}",
        _ => $"Khác — {TotalText}",
    };

    /// <summary>Icon Fluent cho header nhóm (thay emoji 🟢🟡🔴).</summary>
    public SymbolRegular HeaderSymbol => Level switch
    {
        SafetyLevel.Safe => SymbolRegular.CheckmarkCircle24,
        SafetyLevel.Caution => SymbolRegular.Warning24,
        SafetyLevel.Keep => SymbolRegular.ShieldError24,
        _ => SymbolRegular.QuestionCircle24,
    };

    private string TotalText =>
        $"{Items.Count} mục · {BytesToHumanReadableConverter.Format(Items.Sum(i => i.ReclaimableBytes))}";

    /// <summary>Checkbox cả nhóm.</summary>
    [ObservableProperty]
    private bool _isGroupSelected;

    partial void OnIsGroupSelectedChanged(bool value)
    {
        foreach (var item in Items)
            item.IsSelected = value;
    }
}

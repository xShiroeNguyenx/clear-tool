using System.Windows;
using System.Windows.Input;
using ClearTool.App.ViewModels;
using ClearTool.Core.Model;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace ClearTool.App.Views;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        // Theme theo hệ thống + tự đổi khi user đổi Windows light/dark
        SystemThemeWatcher.Watch(this);
        _viewModel = viewModel;
        DataContext = viewModel;
        Title = viewModel.WindowTitle; // taskbar/Alt-Tab vẫn hiện "(Administrator)"
    }

    private void OnTreemapNodeActivated(object? sender, TreeNode node) =>
        _viewModel.DrillIntoCommand.Execute(node);

    private void OnSuggestionRowClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SuggestionItemViewModel item })
            _viewModel.SelectedSuggestion = item;
    }
}

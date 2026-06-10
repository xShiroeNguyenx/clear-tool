using System.Windows;
using ClearTool.App.Converters;
using ClearTool.App.Services;
using ClearTool.Core.Deletion;
using Wpf.Ui.Controls;

namespace ClearTool.App.Views;

public partial class ConfirmDeleteDialog : FluentWindow
{
    public ConfirmDeleteDialog(ConfirmDeleteRequest request)
    {
        InitializeComponent();
        SummaryText.Text =
            $"Bạn sắp xóa {request.ItemCount} mục, tổng cộng {BytesToHumanReadableConverter.Format(request.TotalBytes)}.";
        AdminWarning.Visibility = request.HasAdminItems ? Visibility.Visible : Visibility.Collapsed;
    }

    public DeleteMode SelectedMode =>
        PermanentRadio.IsChecked == true ? DeleteMode.Permanent : DeleteMode.RecycleBin;

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}

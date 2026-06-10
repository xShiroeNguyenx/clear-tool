using System.Windows;
using System.Windows.Controls;
using ClearTool.App.Views;
using ClearTool.Core.Deletion;

namespace ClearTool.App.Services;

public sealed record ConfirmDeleteRequest(int ItemCount, long TotalBytes, bool HasAdminItems);

public sealed record ConfirmDeleteResponse(bool Confirmed, DeleteMode Mode);

public interface IDialogService
{
    Task<ConfirmDeleteResponse> ConfirmDeleteAsync(ConfirmDeleteRequest request);
    Task<bool> ConfirmAsync(string title, string message);
    Task ShowInfoAsync(string title, string message);
    Task ShowErrorAsync(string title, string message);
}

public sealed class DialogService : IDialogService
{
    public Task<ConfirmDeleteResponse> ConfirmDeleteAsync(ConfirmDeleteRequest request)
    {
        var dialog = new ConfirmDeleteDialog(request)
        {
            Owner = Application.Current.MainWindow,
        };
        var confirmed = dialog.ShowDialog() == true;
        return Task.FromResult(new ConfirmDeleteResponse(confirmed, dialog.SelectedMode));
    }

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = title,
            Content = WrapText(message),
            PrimaryButtonText = "Có",
            CloseButtonText = "Không",
        };
        return await box.ShowDialogAsync() == Wpf.Ui.Controls.MessageBoxResult.Primary;
    }

    public async Task ShowInfoAsync(string title, string message) =>
        await new Wpf.Ui.Controls.MessageBox
        {
            Title = title,
            Content = WrapText(message),
            CloseButtonText = "OK",
        }.ShowDialogAsync();

    public async Task ShowErrorAsync(string title, string message) =>
        await new Wpf.Ui.Controls.MessageBox
        {
            Title = title,
            Content = WrapText(message),
            CloseButtonText = "Đóng",
        }.ShowDialogAsync();

    private static TextBlock WrapText(string message) => new()
    {
        Text = message,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 400,
    };
}

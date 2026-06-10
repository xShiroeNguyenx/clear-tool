using System.Windows;
using ClearTool.App.Services;
using ClearTool.App.ViewModels;
using ClearTool.App.Views;
using ClearTool.Core.Admin;
using ClearTool.Core.Deletion;
using ClearTool.Core.Rules;
using ClearTool.Core.Scanning;
using Microsoft.Extensions.DependencyInjection;

namespace ClearTool.App;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Lưới an toàn cuối: log + báo lỗi thay vì sập im lặng
        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Error("DispatcherUnhandledException", args.Exception);
            MessageBox.Show(
                $"Lỗi không mong đợi:\n{args.Exception.Message}\n\nChi tiết: {AppLog.LogFile}",
                "ClearTool", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Error("UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                AppLog.Error("UnhandledException", ex);
        };

        // Instance nâng quyền: chạy đúng MỘT tác vụ admin rồi thoát, không mở UI
        if (ElevatedTask.TryParse(e.Args, out var elevatedTask))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            int exitCode = await ElevatedTaskRunner.RunAsync(elevatedTask);
            // WPF Main là void nên Shutdown(code) không propagate ra process —
            // thoát thẳng để main instance đọc đúng exit code (không có UI nào cần dọn)
            Environment.Exit(exitCode);
        }

        AppLog.Info("OnStartup: bắt đầu composition root");
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemScanner, FileSystemScanner>();
        services.AddSingleton(RuleCatalog.CreateDefault());
        services.AddSingleton<RuleEngine>();
        services.AddSingleton<ProtectedRoots>();
        services.AddSingleton<IDeletionService, DeletionService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        _services = services.BuildServiceProvider();

        AppLog.Info("OnStartup: DI xong, tạo MainWindow");
        var window = _services.GetRequiredService<MainWindow>();

        // Palette Safe/Caution/Keep đổi theo theme (SystemThemeWatcher.Watch
        // trong MainWindow ctor đã apply system theme trước khi tới đây)
        Wpf.Ui.Appearance.ApplicationThemeManager.Changed += (theme, _) => SafetyPalette.Apply(theme);
        SafetyPalette.Apply(Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme());

        AppLog.Info("OnStartup: MainWindow tạo xong, Show()");
        window.Show();
        AppLog.Info("OnStartup: Show() xong");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}

using System.Windows;
using System.Windows.Threading;
using ImageOrganiser.Helpers;
using ImageOrganiser.Services;
using ImageOrganiser.ViewModels;

namespace ImageOrganiser;

public partial class App : Application
{
    private static int _uiErrorCount;
    private LogService? _log;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                _log?.Error("Fatal: " + ex.Message);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _log?.Error("Background task: " + args.Exception.GetBaseException().Message);
            args.SetObserved();
        };

        var config = new ConfigService();
        ThemeManager.Apply(config.Config.General.Theme);

        _log = new LogService();
        if (ElevationHelper.IsElevated())
            _log.Info("Running with administrator privileges.");
        else
            _log.Info("Running as standard user. Use File → Relaunch as administrator to scan protected folders.");

        var cache = new FingerprintCache(config.AppDataDirectory);
        var scanner = new DuplicateScanService(_log, cache);
        var deleter = new PhotoDeleteService(_log, config);
        var mainVm = new MainViewModel(scanner, deleter, config, _log);
        var window = new MainWindow(mainVm, config);
        MainWindow = window;
        window.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs args)
    {
        var message = args.Exception.GetBaseException().Message;
        _log?.Error("UI: " + message);

        if (Interlocked.Increment(ref _uiErrorCount) <= 1)
        {
            MessageBox.Show(
                message + "\n\nFurther errors will be written to the activity log only.",
                AppInfo.ProductName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        args.Handled = true;
    }
}

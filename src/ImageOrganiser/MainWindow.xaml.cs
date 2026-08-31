using System.Diagnostics;
using System.Windows;
using ImageOrganiser.Helpers;
using ImageOrganiser.Services;
using ImageOrganiser.ViewModels;
using ImageOrganiser.Views;

namespace ImageOrganiser;

public partial class MainWindow : Window
{
    private readonly ConfigService _config;
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel, ConfigService config)
    {
        InitializeComponent();
        Title = AppInfo.ProductName;
        AppIcon.ApplyTo(this);
        DataContext = viewModel;
        _viewModel = viewModel;
        _config = config;
        _viewModel.Confirm = (message, caption) =>
            MessageBox.Show(this, message, caption, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        _viewModel.OpenLocations = ShowLocations;
        _viewModel.PickImageFile = PickImageFile;

        SourceInitialized += (_, _) =>
            WindowChromeHelper.ApplyTheme(this, ThemeManager.IsDarkEffective);

        Loaded += (_, _) =>
        {
            ThemeManager.RefreshWindow(this);
            _viewModel.RefreshScope();
        };
    }

    private bool ShowLocations()
    {
        var vm = new LocationsViewModel(_config);
        var window = new LocationsWindow(vm) { Owner = this };
        return window.ShowDialog() == true;
    }

    private static string? PickImageFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a photo to fingerprint",
            Filter = "Photos|*.jpg;*.jpeg;*.jpe;*.jfif;*.png;*.gif;*.bmp;*.tif;*.tiff;*.webp|All files|*.*",
            CheckFileExists = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private void Options_Click(object sender, RoutedEventArgs e)
    {
        var vm = new SettingsViewModel(_config);
        var window = new SettingsWindow(vm) { Owner = this };
        window.ShowDialog();
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var about = new AboutWindow { Owner = this };
        about.ShowDialog();
    }

    private void Feedback_Click(object sender, RoutedEventArgs e) =>
        OpenUrl(AppInfo.GitHubIssuesUrl);

    private void GitHub_Click(object sender, RoutedEventArgs e) =>
        OpenUrl(AppInfo.GitHubUrl);

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            MessageBox.Show(url, "Open in browser", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}

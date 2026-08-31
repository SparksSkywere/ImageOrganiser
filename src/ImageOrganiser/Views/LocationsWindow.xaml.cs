using System.Windows;
using ImageOrganiser.Helpers;
using ImageOrganiser.ViewModels;

namespace ImageOrganiser.Views;

public partial class LocationsWindow : Window
{
    private readonly LocationsViewModel _viewModel;

    public LocationsWindow(LocationsViewModel viewModel)
    {
        InitializeComponent();
        AppIcon.ApplyTo(this);
        _viewModel = viewModel;
        DataContext = viewModel;
        SourceInitialized += (_, _) =>
            WindowChromeHelper.ApplyTheme(this, ThemeManager.IsDarkEffective);
        ThemeManager.RefreshWindow(this);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SaveCommand.Execute(null);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageOrganiser.Models;
using ImageOrganiser.Services;

namespace ImageOrganiser.ViewModels;

public partial class LocationsViewModel : ObservableObject
{
    private readonly ConfigService _config;

    public LocationsViewModel(ConfigService config)
    {
        _config = config;
        LoadFromConfig();
    }

    public ObservableCollection<DriveOptionViewModel> Drives { get; } = [];
    public ObservableCollection<string> Folders { get; } = [];

    [ObservableProperty] private bool _scanEntirePc = true;
    [ObservableProperty] private bool _includeRemovable = true;
    [ObservableProperty] private bool _includeNetwork;
    [ObservableProperty] private string? _selectedFolder;
    [ObservableProperty] private string _statusText = "";

    public bool IsSelectedMode
    {
        get => !ScanEntirePc;
        set => ScanEntirePc = !value;
    }

    partial void OnScanEntirePcChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSelectedMode));
        RefreshStatus();
    }

    public void LoadFromConfig()
    {
        var s = _config.Config.Scan;
        ScanEntirePc = s.ScanEntirePc;
        IncludeRemovable = s.IncludeRemovable;
        IncludeNetwork = s.IncludeNetwork;

        Drives.Clear();
        foreach (var drive in DriveInfo.GetDrives().Where(Ready))
        {
            var included = s.IncludedDrives.Count == 0
                ? s.ScanEntirePc
                : s.IncludedDrives.Any(x =>
                    string.Equals(x.TrimEnd('\\', '/'), drive.Name.TrimEnd('\\', '/'),
                        StringComparison.OrdinalIgnoreCase));
            Drives.Add(new DriveOptionViewModel(drive, included));
        }

        Folders.Clear();
        foreach (var folder in s.IncludedFolders.Where(Directory.Exists))
            Folders.Add(folder);

        RefreshStatus();
    }

    [RelayCommand]
    private void Save()
    {
        var s = _config.Config.Scan;
        s.ScanEntirePc = ScanEntirePc;
        s.IncludeRemovable = IncludeRemovable;
        s.IncludeNetwork = IncludeNetwork;
        s.IncludedFolders = Folders.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var selected = Drives.Where(d => d.IsIncluded).Select(d => d.Name).ToList();
        if (ScanEntirePc)
            s.IncludedDrives = selected.Count == Drives.Count ? [] : selected;
        else
            s.IncludedDrives = selected;

        _config.Save();
    }

    [RelayCommand]
    private void AddFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose a folder of photos to scan",
            Multiselect = true
        };
        if (dialog.ShowDialog() != true)
            return;

        foreach (var path in dialog.FolderNames)
            AddFolderPath(path);
    }

    [RelayCommand]
    private void AddPictures() => AddCommon("Pictures");

    [RelayCommand]
    private void AddDesktop() => AddCommon("Desktop");

    [RelayCommand]
    private void AddDownloads() => AddCommon("Downloads");

    [RelayCommand]
    private void AddDocuments() => AddCommon("Documents");

    [RelayCommand]
    private void AddOneDrive() => AddCommon("OneDrive");

    [RelayCommand]
    private void RemoveFolder()
    {
        if (SelectedFolder is null)
            return;
        Folders.Remove(SelectedFolder);
        SelectedFolder = Folders.FirstOrDefault();
        RefreshStatus();
    }

    [RelayCommand]
    private void SelectAllDrives()
    {
        foreach (var drive in Drives)
            drive.IsIncluded = true;
        RefreshStatus();
    }

    [RelayCommand]
    private void SelectNoDrives()
    {
        foreach (var drive in Drives)
            drive.IsIncluded = false;
        RefreshStatus();
    }

    public void AddFolderPath(string path)
    {
        try
        {
            var full = Path.GetFullPath(path.Trim());
            if (!Directory.Exists(full))
                return;
            if (Folders.Any(f => string.Equals(f, full, StringComparison.OrdinalIgnoreCase)))
                return;
            Folders.Add(full);
            ScanEntirePc = false;
            SelectedFolder = full;
            RefreshStatus();
        }
        catch
        {
            // ignore
        }
    }

    private void AddCommon(string label)
    {
        var match = ScanScope.CommonFolders().FirstOrDefault(x => x.Label == label);
        if (!string.IsNullOrEmpty(match.Path))
            AddFolderPath(match.Path);
    }

    private void RefreshStatus()
    {
        var snapshot = new ScanSettings
        {
            ScanEntirePc = ScanEntirePc,
            IncludedDrives = Drives.Where(d => d.IsIncluded).Select(d => d.Name).ToList(),
            IncludedFolders = Folders.ToList(),
            IncludeRemovable = IncludeRemovable,
            IncludeNetwork = IncludeNetwork
        };
        StatusText = ScanScope.Describe(snapshot);
    }

    private static bool Ready(DriveInfo drive)
    {
        try { return drive.IsReady; }
        catch { return false; }
    }
}

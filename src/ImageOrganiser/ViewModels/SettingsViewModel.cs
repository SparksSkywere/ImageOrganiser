using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageOrganiser.Helpers;
using ImageOrganiser.Services;
using ImageOrganiser.Themes;

namespace ImageOrganiser.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ConfigService _config;
    private bool _loading;

    public SettingsViewModel(ConfigService config)
    {
        _config = config;
        LoadFromConfig();
    }

    public ObservableCollection<ThemeOption> ThemeOptions { get; } = new(ThemeCatalog.PickerOptions);
    public string[] SimilarityOptions { get; } = ["Strict", "Normal", "Loose"];

    [ObservableProperty] private ThemeOption? _selectedTheme;
    [ObservableProperty] private bool _includeRemovable;
    [ObservableProperty] private bool _includeNetwork;
    [ObservableProperty] private bool _skipSystemFolders;
    [ObservableProperty] private bool _skipAppData;
    [ObservableProperty] private bool _skipHiddenFolders;
    [ObservableProperty] private bool _includeJpeg;
    [ObservableProperty] private bool _includePng;
    [ObservableProperty] private bool _includeGif;
    [ObservableProperty] private bool _includeBmp;
    [ObservableProperty] private bool _includeTiff;
    [ObservableProperty] private bool _includeWebp;
    [ObservableProperty] private string _similarity = "Normal";
    [ObservableProperty] private int _minFileSizeKb = 8;
    [ObservableProperty] private int _maxFileSizeMb = 200;
    [ObservableProperty] private string _extraExcludeFoldersText = string.Empty;
    [ObservableProperty] private bool _sendToRecycleBin = true;
    [ObservableProperty] private bool _confirmDeletes = true;
    [ObservableProperty] private string _configPath = string.Empty;
    [ObservableProperty] private string _shortcutStatus = string.Empty;

    partial void OnSelectedThemeChanged(ThemeOption? value)
    {
        if (_loading || value is null) return;
        ThemeManager.Apply(value.Id);
    }

    public void RevertThemePreview() =>
        ThemeManager.Apply(_config.Config.General.Theme);

    public void LoadFromConfig()
    {
        _loading = true;
        var c = _config.Config;
        var themeId = ThemeCatalog.NormalizeId(c.General.Theme);
        SelectedTheme = ThemeOptions.FirstOrDefault(t => t.Id == themeId)
                        ?? ThemeOptions.First();

        IncludeRemovable = c.Scan.IncludeRemovable;
        IncludeNetwork = c.Scan.IncludeNetwork;
        SkipSystemFolders = c.Scan.SkipSystemFolders;
        SkipAppData = c.Scan.SkipAppData;
        SkipHiddenFolders = c.Scan.SkipHiddenFolders;
        IncludeJpeg = c.Scan.IncludeJpeg;
        IncludePng = c.Scan.IncludePng;
        IncludeGif = c.Scan.IncludeGif;
        IncludeBmp = c.Scan.IncludeBmp;
        IncludeTiff = c.Scan.IncludeTiff;
        IncludeWebp = c.Scan.IncludeWebp;
        Similarity = string.IsNullOrWhiteSpace(c.Scan.Similarity) ? "Normal" : c.Scan.Similarity;
        MinFileSizeKb = c.Scan.MinFileSizeKb;
        MaxFileSizeMb = c.Scan.MaxFileSizeMb;
        ExtraExcludeFoldersText = string.Join(Environment.NewLine, c.Scan.ExtraExcludeFolders);
        SendToRecycleBin = c.Delete.SendToRecycleBin;
        ConfirmDeletes = c.Delete.ConfirmDeletes;
        ConfigPath = _config.ConfigPath;
        RefreshShortcutStatus();
        _loading = false;
    }

    [RelayCommand]
    private void Save()
    {
        var c = _config.Config;
        var themeId = SelectedTheme?.Id ?? ThemeCatalog.SystemId;
        c.General.Theme = ThemeManager.ToConfigValue(themeId);
        ThemeManager.Apply(themeId);

        c.Scan.IncludeRemovable = IncludeRemovable;
        c.Scan.IncludeNetwork = IncludeNetwork;
        c.Scan.SkipSystemFolders = SkipSystemFolders;
        c.Scan.SkipAppData = SkipAppData;
        c.Scan.SkipHiddenFolders = SkipHiddenFolders;
        c.Scan.IncludeJpeg = IncludeJpeg;
        c.Scan.IncludePng = IncludePng;
        c.Scan.IncludeGif = IncludeGif;
        c.Scan.IncludeBmp = IncludeBmp;
        c.Scan.IncludeTiff = IncludeTiff;
        c.Scan.IncludeWebp = IncludeWebp;
        c.Scan.Similarity = Similarity;
        c.Scan.MinFileSizeKb = Math.Clamp(MinFileSizeKb, 0, 1024 * 50);
        c.Scan.MaxFileSizeMb = Math.Clamp(MaxFileSizeMb, 1, 4096);
        c.Scan.ExtraExcludeFolders = ExtraExcludeFoldersText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        c.Delete.SendToRecycleBin = SendToRecycleBin;
        c.Delete.ConfirmDeletes = ConfirmDeletes;
        _config.Save();
    }

    [RelayCommand]
    private void OpenFeedback() => OpenUrl(AppInfo.GitHubIssuesUrl);

    [RelayCommand]
    private void CreateDesktopShortcut()
    {
        try
        {
            DesktopShortcutHelper.CreateShortcut();
        }
        catch (Exception ex)
        {
            ShortcutStatus = "Could not create shortcut: " + ex.Message;
            return;
        }

        RefreshShortcutStatus();
    }

    [RelayCommand]
    private void RemoveDesktopShortcut()
    {
        try
        {
            DesktopShortcutHelper.RemoveShortcut();
        }
        catch
        {
            // ignore
        }

        RefreshShortcutStatus();
    }

    private void RefreshShortcutStatus() =>
        ShortcutStatus = DesktopShortcutHelper.ShortcutExists()
            ? "Desktop shortcut is present."
            : "No desktop shortcut.";

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            // ignore
        }
    }
}

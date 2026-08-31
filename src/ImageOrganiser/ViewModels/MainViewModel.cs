using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageOrganiser.Helpers;
using ImageOrganiser.Models;
using ImageOrganiser.Services;

namespace ImageOrganiser.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DuplicateScanService _scanner;
    private readonly PhotoDeleteService _deleter;
    private readonly ConfigService _config;
    private readonly LogService _log;
    private readonly ThumbnailService _thumbnails = new();
    private readonly List<DuplicateGroupViewModel> _allGroups = [];
    private CancellationTokenSource? _cts;

    public MainViewModel(
        DuplicateScanService scanner,
        PhotoDeleteService deleter,
        ConfigService config,
        LogService log)
    {
        _scanner = scanner;
        _deleter = deleter;
        _config = config;
        _log = log;
        GroupsView = CollectionViewSource.GetDefaultView(Groups);
        GroupsView.Filter = FilterGroup;
        _log.MessageLogged += (_, line) => UiThread.Post(() =>
        {
            LogLines.Insert(0, line);
            while (LogLines.Count > 400)
                LogLines.RemoveAt(LogLines.Count - 1);
        });
        StatusText = "Ready — choose locations, then scan for matching photos.";
        EmptyStateTitle = "No matching photos yet";
        EmptyStateDetail = "Scan the whole PC, or pick drives and folders. Fingerprints catch the same picture under a different name, size, or resolution — and close look-alikes.";
        MatchFilterOptions = ["All matches", "Exact copies", "Same photo", "Similar (close)"];
        SelectedMatchFilter = MatchFilterOptions[0];
        RefreshScope();
    }

    public ObservableCollection<DuplicateGroupViewModel> Groups { get; } = [];
    public ICollectionView GroupsView { get; }
    public ObservableCollection<string> LogLines { get; } = [];
    public string[] MatchFilterOptions { get; }

    /// <summary>Set by the window to show Yes/No confirmations on the UI thread.</summary>
    public Func<string, string, bool>? Confirm { get; set; }

    /// <summary>Opens the locations dialog. Returns true if the user saved.</summary>
    public Func<bool>? OpenLocations { get; set; }

    /// <summary>Pick an image file to fingerprint and find close matches.</summary>
    public Func<string?>? PickImageFile { get; set; }

    [ObservableProperty] private DuplicateGroupViewModel? _selectedGroup;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private bool _isProgressIndeterminate;
    [ObservableProperty] private string _progressPercentText = "";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _selectedMatchFilter = "All matches";
    [ObservableProperty] private string _emptyStateTitle = "";
    [ObservableProperty] private string _emptyStateDetail = "";
    [ObservableProperty] private bool _showEmptyState = true;
    [ObservableProperty] private int _filesScanned;
    [ObservableProperty] private int _resolvedCount;
    [ObservableProperty] private string _scopeText = "Entire PC";
    [ObservableProperty] private string _scanButtonText = "Scan PC";

    public bool CanActOnGroup => SelectedGroup is { IsResolved: false } && !IsBusy;
    public string KeepHint =>
        SelectedGroup is { Photos.Count: >= 2 }
            ? "Compare the copies, then keep one and delete the rest."
            : "Select a group to compare photos.";

    partial void OnSearchTextChanged(string value) => GroupsView.Refresh();
    partial void OnSelectedMatchFilterChanged(string value) => GroupsView.Refresh();

    partial void OnSelectedGroupChanged(DuplicateGroupViewModel? value)
    {
        NotifyGroupCommands();
        if (value is null)
            return;

        UiThread.Post(() =>
        {
            foreach (var photo in value.Photos)
                photo.LoadPreview();
        });
    }

    partial void OnIsBusyChanged(bool value)
    {
        NotifyGroupCommands();
        ScanCommand.NotifyCanExecuteChanged();
        ScanEntirePcCommand.NotifyCanExecuteChanged();
        ScanSelectedCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        FindSimilarCommand.NotifyCanExecuteChanged();
        FindSimilarToFileCommand.NotifyCanExecuteChanged();
    }

    private void NotifyGroupCommands()
    {
        OnPropertyChanged(nameof(CanActOnGroup));
        OnPropertyChanged(nameof(KeepHint));
        KeepThisDeleteOthersCommand.NotifyCanExecuteChanged();
        KeepLeftDeleteRightCommand.NotifyCanExecuteChanged();
        KeepRightDeleteLeftCommand.NotifyCanExecuteChanged();
        ApplyDeletesCommand.NotifyCanExecuteChanged();
        SkipGroupCommand.NotifyCanExecuteChanged();
        NextGroupCommand.NotifyCanExecuteChanged();
        PreviousGroupCommand.NotifyCanExecuteChanged();
        MarkForDeleteCommand.NotifyCanExecuteChanged();
        KeepPhotoCommand.NotifyCanExecuteChanged();
    }

    private bool FilterGroup(object obj)
    {
        if (obj is not DuplicateGroupViewModel g)
            return false;
        if (g.IsResolved)
            return false;

        if (SelectedMatchFilter.StartsWith("Exact", StringComparison.OrdinalIgnoreCase) &&
            g.Kind != MatchKind.Exact)
            return false;
        if (SelectedMatchFilter.StartsWith("Same photo", StringComparison.OrdinalIgnoreCase) &&
            g.Kind != MatchKind.SamePhoto)
            return false;
        if (SelectedMatchFilter.StartsWith("Similar", StringComparison.OrdinalIgnoreCase) &&
            g.Kind != MatchKind.Similar)
            return false;

        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        var q = SearchText.Trim();
        return g.Photos.Any(p =>
            p.FileName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            p.Directory.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            p.Path.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            p.FingerprintText.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            p.Model.Fingerprint.Token.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    public void RefreshScope()
    {
        var scan = _config.Config.Scan;
        ScopeText = ScanScope.Describe(scan);
        ScanButtonText = scan.ScanEntirePc ? "Scan PC" : "Scan selected";
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync() => await RunScanAsync(entirePc: null);

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanEntirePcAsync() => await RunScanAsync(entirePc: true);

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanSelectedAsync()
    {
        if (_config.Config.Scan.ScanEntirePc)
        {
            var saved = OpenLocations?.Invoke() ?? false;
            RefreshScope();
            if (!saved && _config.Config.Scan.ScanEntirePc)
                return;
        }

        if (ScanScope.ResolveRoots(_config.Config.Scan).Count == 0)
        {
            OpenLocations?.Invoke();
            RefreshScope();
            if (ScanScope.ResolveRoots(_config.Config.Scan).Count == 0)
            {
                StatusText = "Choose at least one drive or folder.";
                return;
            }
        }

        await RunScanAsync(entirePc: false);
    }

    [RelayCommand]
    private void ChooseLocations()
    {
        OpenLocations?.Invoke();
        RefreshScope();
    }

    private async Task RunScanAsync(bool? entirePc)
    {
        var settings = _config.Config.Scan.Clone();
        if (entirePc is true)
            settings.ScanEntirePc = true;
        else if (entirePc is false)
            settings.ScanEntirePc = false;

        if (ScanScope.ResolveRoots(settings).Count == 0)
        {
            StatusText = "Choose at least one drive or folder under Locations.";
            OpenLocations?.Invoke();
            RefreshScope();
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        IsBusy = true;
        IsProgressIndeterminate = true;
        ProgressValue = 0;
        ProgressPercentText = "";
        StatusText = settings.ScanEntirePc
            ? "Scanning the whole PC…"
            : "Scanning selected locations…";
        EmptyStateTitle = "Scanning…";
        EmptyStateDetail = "Fingerprinting photos. This can take a while on large disks.";
        ShowEmptyState = Groups.Count == 0;
        _thumbnails.Clear();
        _allGroups.Clear();
        Groups.Clear();
        ResolvedCount = 0;

        var progress = new Progress<ScanProgress>(p =>
        {
            StatusText = p.Message;
            FilesScanned = p.FilesFound;
            if (p.IsIndeterminate)
            {
                IsProgressIndeterminate = true;
                ProgressPercentText = "";
            }
            else
            {
                IsProgressIndeterminate = false;
                ProgressValue = p.Percent;
                ProgressPercentText = $"{p.Percent:0}%";
            }

            SummaryText = p.FilesHashed > 0
                ? $"{p.FilesHashed:N0} fingerprinted · {p.GroupsFound} groups"
                : $"{p.FilesFound:N0} photos found";
        });

        try
        {
            var result = await _scanner.ScanAsync(settings, progress, ct).ConfigureAwait(true);
            foreach (var group in result.Groups)
            {
                var vm = new DuplicateGroupViewModel(group, _thumbnails);
                _allGroups.Add(vm);
                Groups.Add(vm);
            }

            FilesScanned = result.FilesScanned;
            var wasted = _allGroups.Sum(g => g.WastedBytes);
            SummaryText = Groups.Count == 0
                ? $"{result.FilesHashed:N0} photos fingerprinted · no matches"
                : $"{Groups.Count} groups · {result.FilesHashed:N0} photos · {FileSizeFormatter.Format(wasted)} recoverable";
            StatusText = Groups.Count == 0
                ? "Scan complete — no matching photos found."
                : $"Scan complete — {Groups.Count} group(s). Compare copies and choose which to keep.";
            ShowEmptyState = Groups.Count == 0;
            EmptyStateTitle = "No matching photos";
            EmptyStateDetail = "Nothing in the selected locations looked like the same picture, or a close look-alike.";
            if (Groups.Count > 0)
                SelectedGroup = Groups[0];
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled.";
            _log.Info("Scan cancelled.");
        }
        catch (Exception ex)
        {
            StatusText = "Scan failed: " + ex.Message;
            _log.Error(ex.ToString());
        }
        finally
        {
            IsBusy = false;
            IsProgressIndeterminate = false;
            ProgressValue = 0;
            ProgressPercentText = "";
            GroupsView.Refresh();
            OnPropertyChanged(nameof(CanActOnGroup));
        }
    }

    private bool CanScan() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cts?.Cancel();
        StatusText = "Cancelling…";
    }

    private bool CanCancel() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanFindSimilar))]
    private void FindSimilar(PhotoItemViewModel? photo)
    {
        if (photo is null)
            return;
        ShowSimilarTo(photo.Model);
    }

    [RelayCommand(CanExecute = nameof(CanFindSimilar))]
    private void FindSimilarToFile()
    {
        var path = PickImageFile?.Invoke();
        if (string.IsNullOrWhiteSpace(path))
            return;

        var query = _scanner.TryDescribeFile(path);
        if (query is null)
        {
            StatusText = "Could not read that image.";
            return;
        }

        ShowSimilarTo(query);
    }

    private bool CanFindSimilar() => !IsBusy && _scanner.LastIndex.Count > 0;

    private void ShowSimilarTo(PhotoFile query)
    {
        if (_scanner.LastIndex.Count == 0)
        {
            StatusText = "Scan first so there is a fingerprint index to search.";
            return;
        }

        var matches = _scanner.FindSimilar(query, _config.Config.Scan.Similarity);
        if (matches.Count < 2)
        {
            StatusText = $"No close matches for {query.FileName} in the last scan.";
            return;
        }

        var kind = matches.Skip(1).All(m => m.SimilarityPercent >= 98)
            ? MatchKind.SamePhoto
            : MatchKind.Similar;
        var worst = matches.Skip(1).Select(m => m.SimilarityPercent).DefaultIfEmpty(100).Min();
        var group = new DuplicateGroup
        {
            Files = matches,
            Kind = matches.All(m =>
                !string.IsNullOrEmpty(query.Sha256) &&
                string.Equals(m.Sha256, query.Sha256, StringComparison.OrdinalIgnoreCase))
                ? MatchKind.Exact
                : kind,
            SimilarityPercent = Math.Round(worst, 1),
            QueryPath = query.Path
        };

        var vm = new DuplicateGroupViewModel(group, _thumbnails);
        Groups.Insert(0, vm);
        _allGroups.Insert(0, vm);
        SelectedGroup = vm;
        ShowEmptyState = false;
        StatusText = $"Found {matches.Count} photo(s) with a fingerprint close to {query.FileName}.";
        GroupsView.Refresh();
    }

    [RelayCommand(CanExecute = nameof(CanActOnGroup))]
    private void KeepThisDeleteOthers(PhotoItemViewModel? keep)
    {
        if (SelectedGroup is null || keep is null)
            return;

        foreach (var photo in SelectedGroup.Photos)
            photo.IsMarkedForDelete = !ReferenceEquals(photo, keep);

        ApplyDeletes();
    }

    [RelayCommand(CanExecute = nameof(CanActOnGroup))]
    private void KeepLeftDeleteRight()
    {
        var group = SelectedGroup;
        if (group?.Left is null || group.Right is null)
            return;
        KeepThisDeleteOthers(group.Left);
    }

    [RelayCommand(CanExecute = nameof(CanActOnGroup))]
    private void KeepRightDeleteLeft()
    {
        var group = SelectedGroup;
        if (group?.Left is null || group.Right is null)
            return;
        KeepThisDeleteOthers(group.Right);
    }

    [RelayCommand]
    private void MarkForDelete(PhotoItemViewModel? photo)
    {
        if (photo is null)
            return;
        photo.IsMarkedForDelete = true;
    }

    [RelayCommand]
    private void KeepPhoto(PhotoItemViewModel? photo)
    {
        if (photo is null)
            return;
        photo.IsMarkedForDelete = false;
    }

    [RelayCommand(CanExecute = nameof(CanActOnGroup))]
    private void ApplyDeletes()
    {
        var group = SelectedGroup;
        if (group is null)
            return;

        var marked = group.Photos.Where(p => p.IsMarkedForDelete).ToList();
        if (marked.Count == 0)
        {
            StatusText = "Mark the copies you want to delete first.";
            return;
        }

        if (marked.Count >= group.Photos.Count)
        {
            StatusText = "Keep at least one copy of the photo.";
            return;
        }

        var recycle = _config.Config.Delete.SendToRecycleBin;
        var verb = recycle ? "move to the Recycle Bin" : "permanently delete";
        var shown = marked.Take(12).Select(p => p.Path).ToList();
        var names = string.Join(Environment.NewLine, shown);
        if (marked.Count > shown.Count)
            names += Environment.NewLine + $"… and {marked.Count - shown.Count} more";
        if (_config.Config.Delete.ConfirmDeletes)
        {
            var ok = Confirm?.Invoke(
                $"This will {verb}:{Environment.NewLine}{Environment.NewLine}{names}",
                "Delete copies") ?? false;
            if (!ok)
                return;
        }

        var deleted = _deleter.Delete(marked.Select(p => p.Path));
        foreach (var photo in marked.ToList())
            group.Photos.Remove(photo);

        if (group.Photos.Count < 2)
        {
            group.IsResolved = true;
            ResolvedCount++;
            var next = Groups.FirstOrDefault(g => !g.IsResolved && !ReferenceEquals(g, group));
            GroupsView.Refresh();
            SelectedGroup = next;
        }

        var remaining = Groups.Count(g => !g.IsResolved);
        StatusText = deleted == 1
            ? $"Removed 1 copy. {remaining} group(s) left."
            : $"Removed {deleted} copies. {remaining} group(s) left.";
        UpdateSummary();
        OnPropertyChanged(nameof(CanActOnGroup));
        OnPropertyChanged(nameof(KeepHint));
        ShowEmptyState = remaining == 0;
        if (remaining == 0)
        {
            EmptyStateTitle = "All groups reviewed";
            EmptyStateDetail = "There are no more matching photo groups to compare.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanActOnGroup))]
    private void SkipGroup()
    {
        var group = SelectedGroup;
        if (group is null)
            return;
        var remaining = Groups.Where(g => !g.IsResolved && !ReferenceEquals(g, group)).ToList();
        SelectedGroup = remaining.FirstOrDefault();
    }

    [RelayCommand]
    private void NextGroup()
    {
        var list = Groups.Where(g => !g.IsResolved).ToList();
        if (list.Count == 0) return;
        var i = SelectedGroup is null ? -1 : list.IndexOf(SelectedGroup);
        SelectedGroup = list[(i + 1) % list.Count];
    }

    [RelayCommand]
    private void PreviousGroup()
    {
        var list = Groups.Where(g => !g.IsResolved).ToList();
        if (list.Count == 0) return;
        var i = SelectedGroup is null ? 0 : list.IndexOf(SelectedGroup);
        SelectedGroup = list[(i - 1 + list.Count) % list.Count];
    }

    [RelayCommand]
    private void ClearLog() => LogLines.Clear();

    [RelayCommand]
    private void OpenConfigFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _config.AppDataDirectory,
                UseShellExecute = true
            });
        }
        catch
        {
            // ignore
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export duplicate list",
            Filter = "Text files (*.txt)|*.txt|CSV (*.csv)|*.csv",
            FileName = "image-duplicates.txt"
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            await using var writer = new StreamWriter(dialog.FileName);
            foreach (var group in _allGroups.Where(g => !g.IsResolved))
            {
                await writer.WriteLineAsync($"# {group.Header}");
                foreach (var photo in group.Photos)
                    await writer.WriteLineAsync(photo.Path);
                await writer.WriteLineAsync();
            }

            StatusText = "Exported " + dialog.FileName;
        }
        catch (Exception ex)
        {
            StatusText = "Export failed: " + ex.Message;
        }
    }

    [RelayCommand]
    private void RelaunchAsAdmin()
    {
        if (ElevationHelper.IsElevated())
        {
            StatusText = "Already running as administrator.";
            return;
        }

        if (ElevationHelper.TryRelaunchElevated([]))
            System.Windows.Application.Current?.Shutdown(0);
        else
            StatusText = "Administrator restart was declined.";
    }

    private void UpdateSummary()
    {
        var remaining = Groups.Where(g => !g.IsResolved).ToList();
        var wasted = remaining.Sum(g => g.WastedBytes);
        SummaryText = remaining.Count == 0
            ? $"{ResolvedCount} group(s) resolved"
            : $"{remaining.Count} groups · {FileSizeFormatter.Format(wasted)} recoverable";
    }
}

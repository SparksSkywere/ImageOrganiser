using System.Diagnostics;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageOrganiser.Helpers;
using ImageOrganiser.Models;
using ImageOrganiser.Services;

namespace ImageOrganiser.ViewModels;

public partial class PhotoItemViewModel : ObservableObject
{
    private readonly ThumbnailService _thumbnails;

    public PhotoItemViewModel(PhotoFile model, bool isRecommended, ThumbnailService thumbnails)
    {
        Model = model;
        _thumbnails = thumbnails;
        IsRecommendedKeep = isRecommended;
        FileName = model.FileName;
        Directory = model.Directory;
        Path = model.Path;
        SizeText = FileSizeFormatter.Format(model.SizeBytes);
        DimensionsText = model.DimensionsText;
        ModifiedText = model.LastWriteUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        DriveText = System.IO.Path.GetPathRoot(model.Path) ?? string.Empty;
        FingerprintText = "Fingerprint  " + model.Fingerprint.ShortToken;
        SimilarityText = isRecommended
            ? "Reference copy"
            : $"{model.SimilarityPercent:0}% match";
    }

    public PhotoFile Model { get; }
    public string Path { get; }
    public string FileName { get; }
    public string Directory { get; }
    public string SizeText { get; }
    public string DimensionsText { get; }
    public string ModifiedText { get; }
    public string DriveText { get; }
    public string FingerprintText { get; }
    public string SimilarityText { get; }
    public bool IsRecommendedKeep { get; }
    public long SizeBytes => Model.SizeBytes;
    public long PixelCount => Model.PixelCount;

    [ObservableProperty] private ImageSource? _preview;
    [ObservableProperty] private bool _isMarkedForDelete;
    [ObservableProperty] private bool _previewFailed;

    public string RecommendationText => IsRecommendedKeep ? "Suggested keep" : string.Empty;

    public string MetaSummary =>
        $"{DimensionsText}  ·  {SizeText}  ·  {ModifiedText}";

    public void LoadPreview()
    {
        if (Preview is not null || PreviewFailed)
            return;

        var image = _thumbnails.Load(Path);
        if (image is null)
            PreviewFailed = true;
        else
            Preview = image;
    }

    [RelayCommand]
    private void OpenFile()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = Path, UseShellExecute = true });
        }
        catch
        {
            // ignore
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{Path}\"",
                UseShellExecute = true
            });
        }
        catch
        {
            // ignore
        }
    }
}

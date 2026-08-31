using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ImageOrganiser.Helpers;
using ImageOrganiser.Models;
using ImageOrganiser.Services;

namespace ImageOrganiser.ViewModels;

public partial class DuplicateGroupViewModel : ObservableObject
{
    public DuplicateGroupViewModel(DuplicateGroup model, ThumbnailService thumbnails)
    {
        Model = model;
        Kind = model.Kind;
        KindText = model.Kind switch
        {
            MatchKind.Exact => "Exact copy",
            MatchKind.SamePhoto => "Same photo",
            _ => "Similar"
        };
        Count = model.Count;
        WastedBytes = model.WastedBytes;
        WastedText = FileSizeFormatter.Format(model.WastedBytes);
        SimilarityText = model.Kind == MatchKind.Exact
            ? "100%"
            : $"{model.SimilarityPercent:0}%";
        var recommended = model.RecommendedKeep;
        foreach (var file in model.Files)
            Photos.Add(new PhotoItemViewModel(file, file.Path == recommended.Path, thumbnails));
        Header = $"{Count} photos · {KindText} · {SimilarityText} · waste {WastedText}";
        SampleName = Photos[0].FileName;
    }

    public DuplicateGroup Model { get; }
    public MatchKind Kind { get; }
    public string KindText { get; }
    public string SimilarityText { get; }
    public int Count { get; }
    public long WastedBytes { get; }
    public string WastedText { get; }
    public string Header { get; }
    public string SampleName { get; }
    public ObservableCollection<PhotoItemViewModel> Photos { get; } = [];

    [ObservableProperty] private bool _isResolved;

    public IEnumerable<PhotoItemViewModel> MarkedForDelete =>
        Photos.Where(p => p.IsMarkedForDelete);

    public PhotoItemViewModel? Left => Photos.ElementAtOrDefault(0);
    public PhotoItemViewModel? Right => Photos.ElementAtOrDefault(1);
}

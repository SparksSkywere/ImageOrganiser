using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using ImageOrganiser.Helpers;

namespace ImageOrganiser.ViewModels;

public partial class DriveOptionViewModel : ObservableObject
{
    public DriveOptionViewModel(DriveInfo drive, bool isIncluded)
    {
        Name = drive.Name.TrimEnd('\\');
        Root = drive.RootDirectory.FullName;
        try
        {
            Label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                ? drive.DriveType.ToString()
                : drive.VolumeLabel;
            TotalText = FileSizeFormatter.Format(drive.TotalSize);
            FreeText = FileSizeFormatter.Format(drive.AvailableFreeSpace);
        }
        catch
        {
            Label = drive.DriveType.ToString();
            TotalText = "—";
            FreeText = "—";
        }

        Display = $"{Name}  {Label}  ({drive.DriveType}, {FreeText} free of {TotalText})";
        IsIncluded = isIncluded;
    }

    public string Name { get; }
    public string Root { get; }
    public string Label { get; }
    public string TotalText { get; }
    public string FreeText { get; }
    public string Display { get; }

    [ObservableProperty] private bool _isIncluded;
}

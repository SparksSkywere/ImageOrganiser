namespace ImageOrganiser.Models;

public sealed class AppConfig
{
    public GeneralSettings General { get; set; } = new();
    public ScanSettings Scan { get; set; } = new();
    public DeleteSettings Delete { get; set; } = new();
}

public sealed class GeneralSettings
{
    public string Theme { get; set; } = "system";
}

public sealed class ScanSettings
{
    /// <summary>When true, scan every eligible drive. When false, only selected drives and folders.</summary>
    public bool ScanEntirePc { get; set; } = true;

    /// <summary>Empty means every eligible drive when ScanEntirePc is true.</summary>
    public List<string> IncludedDrives { get; set; } = [];

    /// <summary>Extra folder trees to scan when not doing an entire-PC run.</summary>
    public List<string> IncludedFolders { get; set; } = [];

    public bool IncludeRemovable { get; set; } = true;
    public bool IncludeNetwork { get; set; }
    public bool SkipSystemFolders { get; set; } = true;
    public bool SkipAppData { get; set; } = true;
    public bool SkipHiddenFolders { get; set; } = true;
    public bool IncludeJpeg { get; set; } = true;
    public bool IncludePng { get; set; } = true;
    public bool IncludeGif { get; set; } = true;
    public bool IncludeBmp { get; set; } = true;
    public bool IncludeTiff { get; set; } = true;
    public bool IncludeWebp { get; set; } = true;

    /// <summary>Strict = 4, Normal = 10, Loose = 16 Hamming distance on a 64-bit dHash.</summary>
    public string Similarity { get; set; } = "Normal";

    public int MinFileSizeKb { get; set; } = 8;
    public int MaxFileSizeMb { get; set; } = 200;
    public List<string> ExtraExcludeFolders { get; set; } = [];

    public ScanSettings Clone() => new()
    {
        ScanEntirePc = ScanEntirePc,
        IncludedDrives = [.. IncludedDrives],
        IncludedFolders = [.. IncludedFolders],
        IncludeRemovable = IncludeRemovable,
        IncludeNetwork = IncludeNetwork,
        SkipSystemFolders = SkipSystemFolders,
        SkipAppData = SkipAppData,
        SkipHiddenFolders = SkipHiddenFolders,
        IncludeJpeg = IncludeJpeg,
        IncludePng = IncludePng,
        IncludeGif = IncludeGif,
        IncludeBmp = IncludeBmp,
        IncludeTiff = IncludeTiff,
        IncludeWebp = IncludeWebp,
        Similarity = Similarity,
        MinFileSizeKb = MinFileSizeKb,
        MaxFileSizeMb = MaxFileSizeMb,
        ExtraExcludeFolders = [.. ExtraExcludeFolders]
    };
}

public sealed class DeleteSettings
{
    public bool SendToRecycleBin { get; set; } = true;
    public bool ConfirmDeletes { get; set; } = true;
}

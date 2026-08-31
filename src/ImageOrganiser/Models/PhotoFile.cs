namespace ImageOrganiser.Models;

public enum MatchKind
{
    Exact,
    SamePhoto,
    Similar
}

public readonly struct PhotoFingerprint
{
    public ulong DHash { get; init; }
    public ulong PHash { get; init; }
    public ulong AHash { get; init; }
    public bool HasValue { get; init; }

    public string Token => HasValue
        ? $"{PHash:X16}-{DHash:X16}-{AHash:X16}"
        : "—";

    public string ShortToken => HasValue
        ? $"{PHash:X4} {DHash:X4} {AHash:X4}"
        : "—";
}

public sealed class PhotoFile
{
    public required string Path { get; init; }
    public string FileName => System.IO.Path.GetFileName(Path);
    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
    public long SizeBytes { get; init; }
    public DateTime LastWriteUtc { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string Sha256 { get; init; } = string.Empty;
    public PhotoFingerprint Fingerprint { get; init; }
    public double SimilarityPercent { get; set; } = 100;

    public ulong DHash => Fingerprint.DHash;
    public bool HasPerceptualHash => Fingerprint.HasValue;

    public long PixelCount => (long)Math.Max(0, Width) * Math.Max(0, Height);

    public string DimensionsText =>
        Width > 0 && Height > 0 ? $"{Width} × {Height}" : "Unknown";
}

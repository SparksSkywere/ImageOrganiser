namespace ImageOrganiser.Models;

public sealed class DuplicateGroup
{
    public required IReadOnlyList<PhotoFile> Files { get; init; }
    public MatchKind Kind { get; init; }
    public double SimilarityPercent { get; init; } = 100;
    public string? QueryPath { get; init; }

    public int Count => Files.Count;

    public long WastedBytes
    {
        get
        {
            if (Files.Count < 2)
                return 0;
            var keep = Files.Max(f => f.SizeBytes);
            return Files.Sum(f => f.SizeBytes) - keep;
        }
    }

    public PhotoFile RecommendedKeep
    {
        get
        {
            return Files
                .OrderByDescending(f => f.PixelCount)
                .ThenByDescending(f => f.SizeBytes)
                .ThenByDescending(f => f.LastWriteUtc)
                .ThenBy(f => f.Path.Length)
                .First();
        }
    }
}

public sealed class ScanProgress
{
    public string Message { get; init; } = string.Empty;
    public double Percent { get; init; } = -1;
    public bool IsIndeterminate => Percent < 0;
    public int FilesFound { get; init; }
    public int FilesHashed { get; init; }
    public int GroupsFound { get; init; }
}

public sealed class ScanResult
{
    public required IReadOnlyList<DuplicateGroup> Groups { get; init; }
    public int FilesScanned { get; init; }
    public int FilesHashed { get; init; }
    public TimeSpan Duration { get; init; }
}

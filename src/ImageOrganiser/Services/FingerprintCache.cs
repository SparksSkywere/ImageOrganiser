using System.Text.Json;
using ImageOrganiser.Models;

namespace ImageOrganiser.Services;

/// <summary>
/// Remembers visual fingerprints so a second scan of the same files is much faster.
/// Entries are keyed by path and discarded when size or timestamp change.
/// </summary>
public sealed class FingerprintCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;
    private readonly Dictionary<string, CacheEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private bool _dirty;

    public FingerprintCache(string appDataDirectory)
    {
        _path = Path.Combine(appDataDirectory, "fingerprint-cache.json");
        Load();
    }

    public bool TryGet(string path, long size, DateTime lastWriteUtc, out CacheHit hit)
    {
        hit = default;
        lock (_sync)
        {
            if (!_entries.TryGetValue(path, out var entry))
                return false;
            if (entry.Size != size || entry.LastWriteUtcTicks != lastWriteUtc.Ticks)
                return false;

            hit = new CacheHit(
                entry.Sha256,
                new PhotoFingerprint
                {
                    DHash = entry.DHash,
                    PHash = entry.PHash,
                    AHash = entry.AHash,
                    HasValue = entry.HasFingerprint
                },
                entry.Width,
                entry.Height);
            return true;
        }
    }

    public void Put(PhotoFile file)
    {
        lock (_sync)
        {
            _entries[file.Path] = new CacheEntry
            {
                Path = file.Path,
                Size = file.SizeBytes,
                LastWriteUtcTicks = file.LastWriteUtc.Ticks,
                Sha256 = file.Sha256,
                DHash = file.Fingerprint.DHash,
                PHash = file.Fingerprint.PHash,
                AHash = file.Fingerprint.AHash,
                HasFingerprint = file.Fingerprint.HasValue,
                Width = file.Width,
                Height = file.Height
            };
            _dirty = true;
        }
    }

    public void Save()
    {
        lock (_sync)
        {
            if (!_dirty)
                return;
            try
            {
                var json = JsonSerializer.Serialize(_entries.Values.ToList(), JsonOptions);
                File.WriteAllText(_path, json);
                _dirty = false;
            }
            catch
            {
                // ignore disk errors
            }
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
                return;
            var json = File.ReadAllText(_path);
            var list = JsonSerializer.Deserialize<List<CacheEntry>>(json, JsonOptions);
            if (list is null)
                return;
            foreach (var entry in list)
            {
                if (!string.IsNullOrWhiteSpace(entry.Path))
                    _entries[entry.Path] = entry;
            }
        }
        catch
        {
            _entries.Clear();
        }
    }

    public readonly record struct CacheHit(string Sha256, PhotoFingerprint Fingerprint, int Width, int Height);

    private sealed class CacheEntry
    {
        public string Path { get; set; } = "";
        public long Size { get; set; }
        public long LastWriteUtcTicks { get; set; }
        public string Sha256 { get; set; } = "";
        public ulong DHash { get; set; }
        public ulong PHash { get; set; }
        public ulong AHash { get; set; }
        public bool HasFingerprint { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using ImageOrganiser.Models;

namespace ImageOrganiser.Services;

public sealed class DuplicateScanService
{
    private readonly LogService _log;
    private readonly FingerprintCache _cache;

    public DuplicateScanService(LogService log, FingerprintCache cache)
    {
        _log = log;
        _cache = cache;
    }

    public IReadOnlyList<PhotoFile> LastIndex { get; private set; } = [];

    public Task<ScanResult> ScanAsync(
        ScanSettings settings,
        IProgress<ScanProgress>? progress,
        CancellationToken ct) =>
        Task.Run(() => Scan(settings, progress, ct), ct);

    public IReadOnlyList<PhotoFile> FindSimilar(PhotoFile query, string similarity, double minPercent = 70)
    {
        var matches = new List<PhotoFile>();
        foreach (var photo in LastIndex)
        {
            if (string.Equals(photo.Path, query.Path, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(CloneWithScore(photo, 100));
                continue;
            }

            if (!PerceptualHash.IsMatch(query, photo, similarity))
                continue;

            var score = PerceptualHash.SimilarityPercent(query, photo);
            if (score >= minPercent)
                matches.Add(CloneWithScore(photo, score));
        }

        return matches
            .OrderByDescending(p => p.SimilarityPercent)
            .ThenByDescending(p => p.PixelCount)
            .ToList();
    }

    public PhotoFile? TryDescribeFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                return null;
            return HashOne(path, info, persist: false);
        }
        catch
        {
            return null;
        }
    }

    private ScanResult Scan(
        ScanSettings settings,
        IProgress<ScanProgress>? progress,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var extensions = DriveScanEnumerator.BuildExtensionSet(settings);
        if (extensions.Count == 0)
        {
            _log.Warn("No image types enabled.");
            return EmptyResult(sw.Elapsed);
        }

        var roots = ScanScope.ResolveRoots(settings);
        if (roots.Count == 0)
        {
            _log.Warn("No scan locations selected.");
            progress?.Report(new ScanProgress
            {
                Message = "No drives or folders selected.",
                Percent = 100
            });
            return EmptyResult(sw.Elapsed);
        }

        _log.Info($"Scanning {roots.Count} location(s): {string.Join(", ", roots)}");
        progress?.Report(new ScanProgress
        {
            Message = settings.ScanEntirePc
                ? "Enumerating photos on the whole PC…"
                : "Enumerating photos in selected locations…",
            Percent = -1
        });

        var minBytes = Math.Max(0, settings.MinFileSizeKb) * 1024L;
        var maxBytes = Math.Max(1, settings.MaxFileSizeMb) * 1024L * 1024L;
        var paths = new List<string>(capacity: 16_384);
        var found = 0;

        foreach (var file in DriveScanEnumerator.EnumerateImageFiles(
                     roots, settings, extensions,
                     n =>
                     {
                         found = n;
                         progress?.Report(new ScanProgress
                         {
                             Message = $"Found {n:N0} photos…",
                             Percent = -1,
                             FilesFound = n
                         });
                     },
                     ct))
        {
            paths.Add(file);
        }

        found = paths.Count;
        _log.Info($"Enumerated {found:N0} image file(s).");
        progress?.Report(new ScanProgress
        {
            Message = $"Fingerprinting {found:N0} photos…",
            Percent = 0,
            FilesFound = found
        });

        var photos = new ConcurrentBag<PhotoFile>();
        var hashed = 0;
        var po = new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount)
        };

        Parallel.ForEach(paths, po, path =>
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                    return;
                if (info.Length < minBytes || info.Length > maxBytes)
                    return;

                var file = HashOne(path, info, persist: true);
                if (file is null)
                    return;
                photos.Add(file);
            }
            catch (Exception ex)
            {
                _log.Warn($"Skipped {path}: {ex.Message}");
            }

            var n = Interlocked.Increment(ref hashed);
            if (n % 25 == 0 || n == found)
            {
                var pct = found == 0 ? 100 : n * 100.0 / found;
                progress?.Report(new ScanProgress
                {
                    Message = $"Fingerprinting photos {n:N0} / {found:N0}",
                    Percent = pct,
                    FilesFound = found,
                    FilesHashed = n
                });
            }
        });

        _cache.Save();

        var list = photos.ToList();
        LastIndex = list;
        progress?.Report(new ScanProgress
        {
            Message = "Grouping matching fingerprints…",
            Percent = 95,
            FilesFound = found,
            FilesHashed = list.Count
        });

        var groups = Cluster(list, settings.Similarity);
        groups = groups
            .OrderByDescending(g => g.Kind == MatchKind.Exact)
            .ThenByDescending(g => g.WastedBytes)
            .ThenByDescending(g => g.Count)
            .ToList();

        sw.Stop();
        _log.Success(
            $"Scan complete: {groups.Count} group(s) from {list.Count:N0} fingerprinted photos in {sw.Elapsed:mm\\:ss}.");

        progress?.Report(new ScanProgress
        {
            Message = $"Found {groups.Count} matching group(s).",
            Percent = 100,
            FilesFound = found,
            FilesHashed = list.Count,
            GroupsFound = groups.Count
        });

        return new ScanResult
        {
            Groups = groups,
            FilesScanned = found,
            FilesHashed = list.Count,
            Duration = sw.Elapsed
        };
    }

    private PhotoFile? HashOne(string path, FileInfo info, bool persist)
    {
        if (_cache.TryGet(path, info.Length, info.LastWriteTimeUtc, out var cached))
        {
            return new PhotoFile
            {
                Path = path,
                SizeBytes = info.Length,
                LastWriteUtc = info.LastWriteTimeUtc,
                Width = cached.Width,
                Height = cached.Height,
                Sha256 = cached.Sha256,
                Fingerprint = cached.Fingerprint
            };
        }

        var sha = HashFile(path);
        PerceptualHash.TryCompute(path, out var fingerprint, out var width, out var height);
        var file = new PhotoFile
        {
            Path = path,
            SizeBytes = info.Length,
            LastWriteUtc = info.LastWriteTimeUtc,
            Width = width,
            Height = height,
            Sha256 = sha,
            Fingerprint = fingerprint
        };
        if (persist)
            _cache.Put(file);
        return file;
    }

    private static PhotoFile CloneWithScore(PhotoFile photo, double score) =>
        new()
        {
            Path = photo.Path,
            SizeBytes = photo.SizeBytes,
            LastWriteUtc = photo.LastWriteUtc,
            Width = photo.Width,
            Height = photo.Height,
            Sha256 = photo.Sha256,
            Fingerprint = photo.Fingerprint,
            SimilarityPercent = score
        };

    private static List<DuplicateGroup> Cluster(List<PhotoFile> files, string similarity)
    {
        var n = files.Count;
        if (n == 0)
            return [];

        var uf = new UnionFind(n);
        var threshold = PerceptualHash.ClusterThreshold(similarity);

        var bySha = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < n; i++)
        {
            var sha = files[i].Sha256;
            if (string.IsNullOrEmpty(sha))
                continue;
            if (!bySha.TryGetValue(sha, out var list))
            {
                list = [];
                bySha[sha] = list;
            }

            list.Add(i);
        }

        foreach (var members in bySha.Values)
        {
            for (var i = 1; i < members.Count; i++)
                uf.Union(members[0], members[i]);
        }

        var pTree = new BkTree();
        var dTree = new BkTree();
        for (var i = 0; i < n; i++)
        {
            if (!files[i].HasPerceptualHash)
                continue;

            foreach (var j in pTree.Search(files[i].Fingerprint.PHash, threshold))
            {
                if (PerceptualHash.IsMatch(files[i], files[j], similarity))
                    uf.Union(i, j);
            }

            foreach (var j in dTree.Search(files[i].DHash, threshold))
            {
                if (PerceptualHash.IsMatch(files[i], files[j], similarity))
                    uf.Union(i, j);
            }

            pTree.Add(files[i].Fingerprint.PHash, i);
            dTree.Add(files[i].DHash, i);
        }

        var clustered = new Dictionary<int, List<PhotoFile>>();
        for (var i = 0; i < n; i++)
        {
            var root = uf.Find(i);
            if (!clustered.TryGetValue(root, out var list))
            {
                list = [];
                clustered[root] = list;
            }

            list.Add(files[i]);
        }

        var groups = new List<DuplicateGroup>();
        foreach (var members in clustered.Values)
        {
            if (members.Count < 2)
                continue;

            var ordered = members
                .OrderByDescending(f => f.PixelCount)
                .ThenByDescending(f => f.SizeBytes)
                .ToList();
            var keep = ordered[0];
            var kind = MatchKind.Exact;
            double worst = 100;
            for (var i = 0; i < ordered.Count; i++)
            {
                var score = i == 0 ? 100 : PerceptualHash.SimilarityPercent(keep, ordered[i]);
                ordered[i].SimilarityPercent = score;
                if (i > 0)
                    worst = Math.Min(worst, score);
                if (i == 0)
                    continue;
                var link = PerceptualHash.Classify(keep, ordered[i], similarity);
                if (link == MatchKind.Similar)
                    kind = MatchKind.Similar;
                else if (link == MatchKind.SamePhoto && kind == MatchKind.Exact)
                    kind = MatchKind.SamePhoto;
            }

            var allExact = ordered.Select(m => m.Sha256).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
                           && !string.IsNullOrEmpty(ordered[0].Sha256);
            if (allExact)
                kind = MatchKind.Exact;

            groups.Add(new DuplicateGroup
            {
                Files = ordered,
                Kind = kind,
                SimilarityPercent = allExact ? 100 : Math.Round(worst, 1)
            });
        }

        return groups;
    }

    private static ScanResult EmptyResult(TimeSpan duration) =>
        new()
        {
            Groups = [],
            FilesScanned = 0,
            FilesHashed = 0,
            Duration = duration
        };

    private static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1024 * 64, FileOptions.SequentialScan);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }

    private sealed class UnionFind
    {
        private readonly int[] _parent;
        private readonly int[] _rank;

        public UnionFind(int n)
        {
            _parent = new int[n];
            _rank = new int[n];
            for (var i = 0; i < n; i++)
                _parent[i] = i;
        }

        public int Find(int x)
        {
            while (_parent[x] != x)
            {
                _parent[x] = _parent[_parent[x]];
                x = _parent[_parent[x]];
            }

            return x;
        }

        public void Union(int a, int b)
        {
            a = Find(a);
            b = Find(b);
            if (a == b)
                return;
            if (_rank[a] < _rank[b])
                (a, b) = (b, a);
            _parent[b] = a;
            if (_rank[a] == _rank[b])
                _rank[a]++;
        }
    }

    private sealed class BkTree
    {
        private BkNode? _root;

        public void Add(ulong hash, int index)
        {
            if (_root is null)
            {
                _root = new BkNode(hash, index);
                return;
            }

            var node = _root;
            while (true)
            {
                var d = PerceptualHash.HammingDistance(node.Hash, hash);
                if (d == 0)
                {
                    node.Indices.Add(index);
                    return;
                }

                if (!node.Children.TryGetValue(d, out var child))
                {
                    node.Children[d] = new BkNode(hash, index);
                    return;
                }

                node = child;
            }
        }

        public List<int> Search(ulong hash, int maxDistance)
        {
            var hits = new List<int>();
            if (_root is null)
                return hits;

            var stack = new Stack<BkNode>();
            stack.Push(_root);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                var d = PerceptualHash.HammingDistance(node.Hash, hash);
                if (d <= maxDistance)
                    hits.AddRange(node.Indices);

                var lo = d - maxDistance;
                var hi = d + maxDistance;
                foreach (var (key, child) in node.Children)
                {
                    if (key >= lo && key <= hi)
                        stack.Push(child);
                }
            }

            return hits;
        }

        private sealed class BkNode
        {
            public ulong Hash { get; }
            public List<int> Indices { get; } = [];
            public Dictionary<int, BkNode> Children { get; } = [];

            public BkNode(ulong hash, int index)
            {
                Hash = hash;
                Indices.Add(index);
            }
        }
    }
}

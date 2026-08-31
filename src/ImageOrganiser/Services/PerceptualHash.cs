using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ImageOrganiser.Models;

namespace ImageOrganiser.Services;

/// <summary>
/// Visual fingerprint for a photo: difference hash (edges), DCT pHash (structure),
/// and average hash (overall light/dark). Together they catch the same picture
/// after a rename, resize, recompress, or a close crop/filter.
/// </summary>
public static class PerceptualHash
{
    public static int HammingDistance(ulong a, ulong b) =>
        System.Numerics.BitOperations.PopCount(a ^ b);

    public static bool TryCompute(string path, out PhotoFingerprint fingerprint, out int width, out int height)
    {
        fingerprint = default;
        width = 0;
        height = 0;

        try
        {
            var identify = Image.Identify(path);
            if (identify is null)
                return false;

            width = identify.Width;
            height = identify.Height;
            if (width < 8 || height < 8)
                return false;

            var options = new DecoderOptions
            {
                TargetSize = new Size(64, 64)
            };

            using var image = Image.Load<L8>(options, path);
            image.Mutate(ctx => ctx.AutoOrient());

            fingerprint = new PhotoFingerprint
            {
                DHash = ComputeDHash(image),
                AHash = ComputeAHash(image),
                PHash = ComputePHash(image),
                HasValue = true
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static MatchKind Classify(PhotoFile a, PhotoFile b, string similarity)
    {
        if (!string.IsNullOrEmpty(a.Sha256) &&
            string.Equals(a.Sha256, b.Sha256, StringComparison.OrdinalIgnoreCase))
            return MatchKind.Exact;

        if (!a.Fingerprint.HasValue || !b.Fingerprint.HasValue)
            return MatchKind.Similar;

        var t = ThresholdsFor(similarity);
        var p = HammingDistance(a.Fingerprint.PHash, b.Fingerprint.PHash);
        var d = HammingDistance(a.Fingerprint.DHash, b.Fingerprint.DHash);
        var avg = HammingDistance(a.Fingerprint.AHash, b.Fingerprint.AHash);

        if (p <= t.SamePHash && (d <= t.SameDHash || avg <= t.SameAHash))
            return MatchKind.SamePhoto;
        if (p <= t.SimilarPHash || d <= t.SimilarDHash)
            return MatchKind.Similar;

        return MatchKind.Similar;
    }

    public static bool IsMatch(PhotoFile a, PhotoFile b, string similarity)
    {
        if (!string.IsNullOrEmpty(a.Sha256) &&
            string.Equals(a.Sha256, b.Sha256, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!a.Fingerprint.HasValue || !b.Fingerprint.HasValue)
            return false;

        var t = ThresholdsFor(similarity);
        var p = HammingDistance(a.Fingerprint.PHash, b.Fingerprint.PHash);
        var d = HammingDistance(a.Fingerprint.DHash, b.Fingerprint.DHash);
        var avg = HammingDistance(a.Fingerprint.AHash, b.Fingerprint.AHash);
        return p <= t.SimilarPHash || d <= t.SimilarDHash || avg <= t.SimilarAHash;
    }

    public static double SimilarityPercent(PhotoFile a, PhotoFile b)
    {
        if (!string.IsNullOrEmpty(a.Sha256) &&
            string.Equals(a.Sha256, b.Sha256, StringComparison.OrdinalIgnoreCase))
            return 100;

        if (!a.Fingerprint.HasValue || !b.Fingerprint.HasValue)
            return 0;

        var p = 1.0 - HammingDistance(a.Fingerprint.PHash, b.Fingerprint.PHash) / 64.0;
        var d = 1.0 - HammingDistance(a.Fingerprint.DHash, b.Fingerprint.DHash) / 64.0;
        var avg = 1.0 - HammingDistance(a.Fingerprint.AHash, b.Fingerprint.AHash) / 64.0;
        return Math.Clamp((0.5 * p + 0.3 * d + 0.2 * avg) * 100.0, 0, 100);
    }

    public static int ClusterThreshold(string similarity) =>
        ThresholdsFor(similarity).SimilarPHash;

    public static HashThresholds ThresholdsFor(string similarity) =>
        similarity.Trim() switch
        {
            "Strict" => new HashThresholds(4, 4, 6, 10, 10, 12),
            "Loose" => new HashThresholds(12, 14, 14, 20, 22, 22),
            _ => new HashThresholds(8, 10, 10, 14, 16, 16)
        };

    public readonly record struct HashThresholds(
        int SameDHash, int SamePHash, int SameAHash,
        int SimilarDHash, int SimilarPHash, int SimilarAHash);

    private static ulong ComputeDHash(Image<L8> source)
    {
        using var image = source.Clone();
        image.Mutate(ctx => ctx.Resize(9, 8, KnownResamplers.Triangle));
        ulong hash = 0;
        var bit = 0;
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                if (image[x, y].PackedValue < image[x + 1, y].PackedValue)
                    hash |= 1UL << bit;
                bit++;
            }
        }

        return hash;
    }

    private static ulong ComputeAHash(Image<L8> source)
    {
        using var image = source.Clone();
        image.Mutate(ctx => ctx.Resize(8, 8, KnownResamplers.Triangle));
        var sum = 0;
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
                sum += image[x, y].PackedValue;
        }

        var mean = sum / 64.0;
        ulong hash = 0;
        var bit = 0;
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                if (image[x, y].PackedValue >= mean)
                    hash |= 1UL << bit;
                bit++;
            }
        }

        return hash;
    }

    private static ulong ComputePHash(Image<L8> source)
    {
        using var image = source.Clone();
        image.Mutate(ctx => ctx.Resize(32, 32, KnownResamplers.Triangle));

        var pixels = new double[32];
        var tmp = new double[32, 32];
        var dct = new double[32, 32];

        for (var y = 0; y < 32; y++)
        {
            for (var x = 0; x < 32; x++)
                pixels[x] = image[x, y].PackedValue;
            Dct1D(pixels, tmp, y, row: true);
        }

        for (var x = 0; x < 32; x++)
        {
            for (var y = 0; y < 32; y++)
                pixels[y] = tmp[y, x];
            Dct1D(pixels, dct, x, row: false);
        }

        Span<double> low = stackalloc double[64];
        var n = 0;
        for (var u = 0; u < 8; u++)
        {
            for (var v = 0; v < 8; v++)
                low[n++] = dct[u, v];
        }

        var median = MedianSkipDc(low);
        ulong hash = 0;
        for (var i = 1; i < 64; i++)
        {
            if (low[i] > median)
                hash |= 1UL << (i - 1);
        }

        return hash;
    }

    private static void Dct1D(double[] input, double[,] output, int index, bool row)
    {
        const int n = 32;

        for (var k = 0; k < n; k++)
        {
            var sum = 0.0;
            for (var i = 0; i < n; i++)
                sum += input[i] * Math.Cos(((2 * i + 1) * k * Math.PI) / (2.0 * n));

            var scale = k == 0 ? 1.0 / Math.Sqrt(n) : Math.Sqrt(2.0 / n);
            var value = scale * sum;
            if (row)
                output[index, k] = value;
            else
                output[k, index] = value;
        }
    }

    private static double MedianSkipDc(Span<double> values)
    {
        var copy = new double[63];
        values.Slice(1, 63).CopyTo(copy);
        Array.Sort(copy);
        return copy[31];
    }
}

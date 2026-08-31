using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImageOrganiser.Services;

public sealed class ThumbnailService
{
    private readonly ConcurrentDictionary<string, ImageSource> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ImageSource? Load(string path, int decodeWidth = 480)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        var key = path + "|" + decodeWidth;
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var ms = new MemoryStream();
            fs.CopyTo(ms);
            ms.Position = 0;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.PreservePixelFormat;
            bmp.DecodePixelWidth = decodeWidth;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            _cache[key] = bmp;
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public void Clear() => _cache.Clear();
}

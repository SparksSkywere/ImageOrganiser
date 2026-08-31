using System.IO;
using ImageOrganiser.Models;

namespace ImageOrganiser.Services;

public static class DriveScanEnumerator
{
    private static readonly HashSet<string> DefaultSkipFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "$recycle.bin",
        "system volume information",
        "recovery",
        "windows",
        "windows.old",
        "windowsapps",
        "winsxs",
        "program files",
        "program files (x86)",
        "programdata",
        "$windows.~bt",
        "$windows.~ws",
        "config.msi",
        "documents and settings",
        "msocache",
        "perflogs",
        "intel",
        "amd",
        "nvidia",
        "boot",
        "efi",
        "winre",
        "system restore",
        "temp",
        "tmp",
        "node_modules",
        ".git",
        ".svn"
    };

    public static IReadOnlyList<DriveInfo> GetEligibleDrives(ScanSettings settings)
    {
        var drives = new List<DriveInfo>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady)
                    continue;

                var letter = drive.Name.TrimEnd('\\', '/');
                var listed = settings.IncludedDrives.Any(d =>
                    string.Equals(d.TrimEnd('\\', '/'), letter, StringComparison.OrdinalIgnoreCase));

                if (listed)
                {
                    drives.Add(drive);
                    continue;
                }

                if (!settings.ScanEntirePc)
                    continue;

                if (settings.IncludedDrives.Count > 0)
                    continue;

                var include = drive.DriveType switch
                {
                    DriveType.Fixed => true,
                    DriveType.Removable => settings.IncludeRemovable,
                    DriveType.Network => settings.IncludeNetwork,
                    _ => false
                };

                if (!include)
                    continue;

                drives.Add(drive);
            }
            catch
            {
                // skip unreadable drives
            }
        }

        return drives;
    }

    public static IEnumerable<string> EnumerateImageFiles(
        IEnumerable<string> roots,
        ScanSettings settings,
        HashSet<string> extensions,
        Action<int>? onFound,
        CancellationToken ct)
    {
        var found = 0;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var extraSkip = settings.ExtraExcludeFolders
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().TrimEnd('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var stack = new Stack<string>();
        foreach (var root in roots)
        {
            if (Directory.Exists(root))
                stack.Push(root);
        }

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();

            string full;
            try
            {
                full = Path.GetFullPath(dir);
            }
            catch
            {
                continue;
            }

            if (!visited.Add(full))
                continue;

            if (ShouldSkipDirectory(full, settings, extraSkip))
                continue;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(full);
            }
            catch
            {
                files = [];
            }

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var ext = Path.GetExtension(file);
                if (ext.Length == 0 || !extensions.Contains(ext))
                    continue;

                found++;
                if (found % 250 == 0)
                    onFound?.Invoke(found);

                yield return file;
            }

            IEnumerable<string> subdirs;
            try
            {
                subdirs = Directory.EnumerateDirectories(full);
            }
            catch
            {
                continue;
            }

            foreach (var sub in subdirs)
            {
                try
                {
                    var info = new DirectoryInfo(sub);
                    if (settings.SkipHiddenFolders &&
                        (info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                        continue;
                }
                catch
                {
                    continue;
                }

                stack.Push(sub);
            }
        }

        onFound?.Invoke(found);
    }

    public static HashSet<string> BuildExtensionSet(ScanSettings settings)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (settings.IncludeJpeg)
        {
            set.Add(".jpg");
            set.Add(".jpeg");
            set.Add(".jpe");
            set.Add(".jfif");
        }

        if (settings.IncludePng)
            set.Add(".png");
        if (settings.IncludeGif)
            set.Add(".gif");
        if (settings.IncludeBmp)
        {
            set.Add(".bmp");
            set.Add(".dib");
        }

        if (settings.IncludeTiff)
        {
            set.Add(".tif");
            set.Add(".tiff");
        }

        if (settings.IncludeWebp)
            set.Add(".webp");

        return set;
    }

    private static bool ShouldSkipDirectory(string fullPath, ScanSettings settings, HashSet<string> extraSkip)
    {
        if (extraSkip.Count > 0)
        {
            foreach (var skip in extraSkip)
            {
                if (fullPath.StartsWith(skip, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        var name = Path.GetFileName(fullPath.TrimEnd('\\', '/'));
        if (settings.SkipSystemFolders && DefaultSkipFolders.Contains(name))
            return true;

        if (settings.SkipAppData &&
            fullPath.Contains(@"\AppData\", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}

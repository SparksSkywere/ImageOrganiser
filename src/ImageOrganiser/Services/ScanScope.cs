using System.IO;
using ImageOrganiser.Helpers;
using ImageOrganiser.Models;

namespace ImageOrganiser.Services;

public static class ScanScope
{
    public static IReadOnlyList<string> ResolveRoots(ScanSettings settings)
    {
        var roots = new List<string>();

        if (settings.ScanEntirePc)
        {
            foreach (var drive in DriveScanEnumerator.GetEligibleDrives(settings))
                roots.Add(drive.RootDirectory.FullName);
            return roots;
        }

        foreach (var drive in DriveScanEnumerator.GetEligibleDrives(settings))
        {
            if (settings.IncludedDrives.Count == 0)
                continue;
            roots.Add(drive.RootDirectory.FullName);
        }

        foreach (var folder in settings.IncludedFolders)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folder))
                    continue;
                var full = Path.GetFullPath(folder.Trim());
                if (Directory.Exists(full))
                    roots.Add(full);
            }
            catch
            {
                // skip bad paths
            }
        }

        return DeduplicateRoots(roots);
    }

    public static string Describe(ScanSettings settings)
    {
        if (settings.ScanEntirePc)
        {
            var drives = DriveScanEnumerator.GetEligibleDrives(settings);
            if (drives.Count == 0)
                return "Entire PC — no ready drives";
            var names = string.Join(", ", drives.Select(d => d.Name.TrimEnd('\\')));
            return $"Entire PC ({names})";
        }

        var parts = new List<string>();
        if (settings.IncludedDrives.Count > 0)
            parts.Add(settings.IncludedDrives.Count == 1
                ? $"drive {settings.IncludedDrives[0]}"
                : $"{settings.IncludedDrives.Count} drives");
        if (settings.IncludedFolders.Count > 0)
            parts.Add(settings.IncludedFolders.Count == 1
                ? ShortFolder(settings.IncludedFolders[0])
                : $"{settings.IncludedFolders.Count} folders");

        return parts.Count == 0
            ? "Selected locations — none chosen yet"
            : "Selected: " + string.Join(" · ", parts);
    }

    public static IReadOnlyList<(string Label, string Path)> CommonFolders()
    {
        var list = new List<(string, string)>();
        void add(string label, Environment.SpecialFolder folder)
        {
            try
            {
                var path = Environment.GetFolderPath(folder);
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                    list.Add((label, path));
            }
            catch
            {
                // skip
            }
        }

        add("Pictures", Environment.SpecialFolder.MyPictures);
        add("Desktop", Environment.SpecialFolder.DesktopDirectory);
        add("Downloads", Environment.SpecialFolder.UserProfile);
        add("Documents", Environment.SpecialFolder.MyDocuments);
        add("Videos", Environment.SpecialFolder.MyVideos);

        // Downloads is under the profile; prefer the real Downloads folder when present.
        try
        {
            var downloads = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (Directory.Exists(downloads))
            {
                list.RemoveAll(x => x.Item1 == "Downloads");
                list.Add(("Downloads", downloads));
            }
        }
        catch
        {
            // keep profile fallback
        }

        try
        {
            var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
            if (!string.IsNullOrWhiteSpace(oneDrive) && Directory.Exists(oneDrive))
                list.Add(("OneDrive", oneDrive));
        }
        catch
        {
            // skip
        }

        return list;
    }

    public static string ShortFolder(string path)
    {
        try
        {
            var full = Path.GetFullPath(path.Trim());
            var name = Path.GetFileName(full.TrimEnd('\\', '/'));
            var root = Path.GetPathRoot(full)?.TrimEnd('\\') ?? "";
            return string.IsNullOrEmpty(name) ? full : $"{name} ({root})";
        }
        catch
        {
            return path;
        }
    }

    public static string DriveSummary(DriveInfo drive)
    {
        string label;
        string free;
        string total;
        try
        {
            label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.DriveType.ToString() : drive.VolumeLabel;
            free = FileSizeFormatter.Format(drive.AvailableFreeSpace);
            total = FileSizeFormatter.Format(drive.TotalSize);
        }
        catch
        {
            label = drive.DriveType.ToString();
            free = "—";
            total = "—";
        }

        return $"{drive.Name.TrimEnd('\\')}  {label}  ({drive.DriveType}, {free} free of {total})";
    }

    private static List<string> DeduplicateRoots(List<string> roots)
    {
        var normalized = new List<string>();
        foreach (var root in roots)
        {
            string full;
            try { full = Path.GetFullPath(root).TrimEnd('\\', '/'); }
            catch { continue; }

            if (normalized.Any(existing =>
                    string.Equals(existing, full, StringComparison.OrdinalIgnoreCase)))
                continue;

            // Skip a folder that is already covered by a parent root.
            if (normalized.Any(existing =>
                    full.StartsWith(existing + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                continue;

            // If this root is a parent of an existing more-specific folder, replace it later via visited-set.
            normalized.Add(full);
        }

        return normalized;
    }
}

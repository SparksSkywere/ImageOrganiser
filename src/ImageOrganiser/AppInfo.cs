using System.Reflection;

namespace ImageOrganiser;

/// <summary>Product branding and version metadata for Image Organiser.</summary>
public static class AppInfo
{
    public const string ProductName = "Image Organiser";
    public const string ProductNameShort = "Image Organiser";
    public const string Company = "Skywere Industries";
    public const string Copyright = "Copyright © Skywere Industries";
    public const string Description =
        "Scan every drive for the same photo even when the file name, size, or " +
        "pixel dimensions differ. Compare copies side by side and choose which to keep or delete.";

    public const string ExeFileName = "ImageOrganiser.exe";
    public const string InstallFolderName = "ImageOrganiser";
    public const string AppDataFolderName = "ImageOrganiser";
    public const string CompanyFolderName = "Skywere Industries";

    public const string GitHubOwner = "SparksSkywere";
    public const string GitHubRepo = "ImageOrganiser";
    public const string GitHubUrl = "https://github.com/SparksSkywere/ImageOrganiser";
    public const string GitHubIssuesUrl = "https://github.com/SparksSkywere/ImageOrganiser/issues";
    public const string GitHubReleasesUrl = "https://github.com/SparksSkywere/ImageOrganiser/releases";

    public static string Version
    {
        get
        {
            var asm = Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                var plus = info.IndexOf('+');
                return plus > 0 ? info[..plus] : info;
            }

            return asm.GetName().Version?.ToString(3) ?? "1.0.0";
        }
    }

    public static string AboutText =>
        $"{ProductName}\n" +
        $"Version {Version}\n\n" +
        $"{Description}\n\n" +
        $"{Copyright}\n" +
        $"Created by {Company}";
}

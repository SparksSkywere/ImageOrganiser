namespace ImageOrganiser.Helpers;

public static class FileSizeFormatter
{
    public static string Format(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        double value = bytes;
        string[] units = ["KB", "MB", "GB", "TB"];
        foreach (var unit in units)
        {
            value /= 1024.0;
            if (value < 1024)
                return $"{value:0.##} {unit}";
        }

        return $"{value:0.##} PB";
    }
}

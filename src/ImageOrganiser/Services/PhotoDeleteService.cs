using System.IO;
using ImageOrganiser.Helpers;

namespace ImageOrganiser.Services;

public sealed class PhotoDeleteService
{
    private readonly LogService _log;
    private readonly ConfigService _config;

    public PhotoDeleteService(LogService log, ConfigService config)
    {
        _log = log;
        _config = config;
    }

    public int Delete(IEnumerable<string> paths)
    {
        var recycle = _config.Config.Delete.SendToRecycleBin;
        var deleted = 0;
        foreach (var path in paths)
        {
            try
            {
                if (!File.Exists(path))
                    continue;
                RecycleBin.Delete(path, recycle);
                deleted++;
                _log.Info($"{(recycle ? "Recycled" : "Deleted")} {path}");
            }
            catch (Exception ex)
            {
                _log.Warn($"Could not delete {path}: {ex.Message}");
            }
        }

        return deleted;
    }
}

using Serilog;

namespace DumpSQL.Services;

public class LogCleanupService
{
    private const int RetentionDays = 30;
    private readonly string _logDirectory;

    public LogCleanupService(string logDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        _logDirectory = logDirectory;
    }

    public void Cleanup()
    {
        if (!Directory.Exists(_logDirectory))
            return;

        var cutoff = DateTime.Now.AddDays(-RetentionDays);
        var deleted = 0;

        foreach (var file in Directory.GetFiles(_logDirectory, "*.log"))
        {
            if (File.GetCreationTime(file) < cutoff)
            {
                File.Delete(file);
                deleted++;
            }
        }

        if (deleted > 0)
            Log.Information("LogCleanup: eliminati {Count} file log più vecchi di {Days} giorni", deleted, RetentionDays);
    }
}

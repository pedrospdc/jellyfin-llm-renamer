namespace Jellyfin.Plugin.LLMRenamer.Services;

/// <summary>
/// Simple file-based logger for plugin-specific events.
/// Writes daily log files to Jellyfin's log directory so they appear in the admin log viewer.
/// </summary>
public static class PluginLog
{
    private static readonly object Lock = new();

    /// <summary>
    /// Log an informational message.
    /// </summary>
    public static void Info(string message)
    {
        Write("INF", message);
    }

    /// <summary>
    /// Log a warning message.
    /// </summary>
    public static void Warn(string message)
    {
        Write("WRN", message);
    }

    /// <summary>
    /// Log an error message.
    /// </summary>
    public static void Error(string message)
    {
        Write("ERR", message);
    }

    private static void Write(string level, string message)
    {
        var logFile = GetLogPath();
        if (logFile == null)
        {
            return;
        }

        try
        {
            lock (Lock)
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                File.AppendAllText(logFile, $"[{timestamp}] [{level}] {message}\n");
            }
        }
        catch
        {
            // Don't let logging failures crash the plugin
        }
    }

    private static string? GetLogPath()
    {
        var logDir = Plugin.Instance?.LogDirectoryPath;
        if (string.IsNullOrEmpty(logDir))
        {
            return null;
        }

        var date = DateTime.Now.ToString("yyyy-MM-dd");
        return Path.Combine(logDir, $"LLMRenamer_{date}.log");
    }
}

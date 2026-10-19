namespace BTSSimpleBackup;

/// <summary>Plain-text log, one file per day, kept in %AppData%\BTSSimpleBackup\logs. Old logs are pruned.</summary>
internal sealed class Logger
{
    private readonly object _lock = new();
    private readonly string _path = Path.Combine(AppPaths.LogDir, $"backup-{DateTime.Now:yyyy-MM-dd}.log");

    public string FilePath => _path;

    public Logger() => Prune();

    public void Info(string message) => Write("INFO ", message);
    public void Warn(string message) => Write("WARN ", message);
    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        try
        {
            lock (_lock)
                File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}");
        }
        catch { /* logging must never crash a backup */ }
    }

    private static void Prune()
    {
        try
        {
            foreach (var f in new DirectoryInfo(AppPaths.LogDir).GetFiles("backup-*.log"))
                if (f.LastWriteTime < DateTime.Now.AddDays(-90)) f.Delete();
        }
        catch { }
    }
}

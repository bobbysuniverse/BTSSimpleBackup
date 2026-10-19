namespace BTSSimpleBackup;

/// <summary>Where the program keeps its own small files (settings, status, logs).</summary>
internal static class AppPaths
{
    public const string AppName = "BTS SimpleBackup";
    public const string TaskName = "BTS SimpleBackup (weekly)";

    public static string DataDir { get; } = Directory.CreateDirectory(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BTSSimpleBackup")).FullName;

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string StatusFile => Path.Combine(DataDir, "status.json");
    public static string LogDir => Directory.CreateDirectory(Path.Combine(DataDir, "logs")).FullName;

    /// <summary>Permanent home for the program, so it keeps working if the download is deleted.</summary>
    public static string InstallDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "BTSSimpleBackup");

    public static string InstalledExe => Path.Combine(InstallDir, "BTSSimpleBackup.exe");
}

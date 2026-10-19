using System.Text.Json;

namespace BTSSimpleBackup;

internal sealed class Settings
{
    /// <summary>Volume serial number of the chosen drive (stays the same when the drive letter changes).</summary>
    public uint DriveSerial { get; set; }
    public string DriveLabel { get; set; } = "";
    public List<string> Folders { get; set; } = new();
    public DayOfWeek Day { get; set; } = DayOfWeek.Sunday;
    public int Hour { get; set; } = 12;
    public int Minute { get; set; } = 0;
    public bool ScheduleEnabled { get; set; }
    /// <summary>True once the desktop and Start menu shortcuts have been created (so they aren't re-added every time).</summary>
    public bool ShortcutsCreated { get; set; }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(AppPaths.SettingsFile), Json) ?? new Settings();
        }
        catch { /* corrupt file: start fresh */ }
        return new Settings();
    }

    public void Save()
    {
        // Write to a temporary file first, so a crash or power cut can never leave a half-written settings file.
        var temp = AppPaths.SettingsFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
        File.Move(temp, AppPaths.SettingsFile, overwrite: true);
    }
}

internal sealed class Status
{
    public DateTime? LastSuccessLocal { get; set; }
    public DateTime? LastAttemptLocal { get; set; }
    public bool LastAttemptOk { get; set; }
    public string Message { get; set; } = "";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Status Load()
    {
        try
        {
            if (File.Exists(AppPaths.StatusFile))
                return JsonSerializer.Deserialize<Status>(File.ReadAllText(AppPaths.StatusFile), Json) ?? new Status();
        }
        catch { }
        return new Status();
    }

    public void Save()
    {
        try { File.WriteAllText(AppPaths.StatusFile, JsonSerializer.Serialize(this, Json)); } catch { }
    }
}

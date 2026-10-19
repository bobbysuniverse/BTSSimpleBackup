using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BTSSimpleBackup;

/// <summary>Start menu and desktop shortcuts, so the user can always find the program again.</summary>
internal static class ShortcutHelper
{
    private const string LinkName = "BTS SimpleBackup.lnk";

    public static string StartMenuLink =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), LinkName);

    public static string DesktopLink =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), LinkName);

    public static void CreateAll(string exePath, Logger log)
    {
        Create(StartMenuLink, exePath, log);
        Create(DesktopLink, exePath, log);
    }

    public static void RemoveAll()
    {
        TryDelete(StartMenuLink);
        TryDelete(DesktopLink);
    }

    private static void Create(string linkPath, string exePath, Logger log)
    {
        object? shell = null;
        object? link = null;
        try
        {
            // Windows' built-in scripting component creates .lnk files; no extra package needed.
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) { log.Warn("Could not create shortcuts: Windows scripting is not available."); return; }

            shell = Activator.CreateInstance(type);
            dynamic sh = shell!;
            link = sh.CreateShortcut(linkPath);
            dynamic l = link!;
            l.TargetPath = exePath;
            l.WorkingDirectory = Path.GetDirectoryName(exePath) ?? "";
            l.IconLocation = exePath + ",0";
            l.Description = "Back up your files to a USB drive";
            l.Save();
            log.Info($"Created shortcut {linkPath}");
        }
        catch (Exception ex)
        {
            log.Warn($"Could not create the shortcut {linkPath}: {ex.Message}");
        }
        finally
        {
            try { if (link != null) Marshal.FinalReleaseComObject(link); } catch { }
            try { if (shell != null) Marshal.FinalReleaseComObject(shell); } catch { }
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

/// <summary>Removes the program from this computer. Never touches the backup drive or the user's own files.</summary>
internal static class Uninstaller
{
    /// <summary>Returns false if the weekly schedule could not be removed (the caller tells the user how to finish it by hand).</summary>
    public static bool Run(Logger log)
    {
        var (scheduleRemoved, scheduleError) = ScheduleHelper.Remove();
        ShortcutHelper.RemoveAll();
        if (scheduleRemoved) log.Info("Uninstalling: schedule and shortcuts removed.");
        else log.Warn("Uninstalling: shortcuts removed, but the weekly schedule could not be removed: " + scheduleError);

        var install = AppPaths.InstallDir;
        var data = AppPaths.DataDir;

        // Safety: only ever delete folders that are clearly this program's own.
        if (!install.TrimEnd('\\').EndsWith("BTSSimpleBackup", StringComparison.OrdinalIgnoreCase) ||
            !data.TrimEnd('\\').EndsWith("BTSSimpleBackup", StringComparison.OrdinalIgnoreCase))
            return scheduleRemoved;

        // Windows can't delete a program while it is running, so a tiny helper removes the folders a few seconds after we close.
        var args = $"/c ping 127.0.0.1 -n 4 >nul & rmdir /s /q \"{install}\" & rmdir /s /q \"{data}\"";
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch { }
        return scheduleRemoved;
    }
}

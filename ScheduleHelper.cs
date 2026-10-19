using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace BTSSimpleBackup;

/// <summary>
/// Creates the weekly Windows Task Scheduler entry so the user never has to open Task Scheduler.
/// The task runs as the current user (no administrator rights, no stored password), and
/// "start when available" makes Windows run a missed backup as soon as the PC is next on.
/// </summary>
internal static class ScheduleHelper
{
    /// <summary>Copies the program to a permanent folder (once) and returns the path the task should run.</summary>
    public static string EnsureInstalledCopy(Logger log)
    {
        var current = Environment.ProcessPath ?? "";
        if (string.IsNullOrEmpty(current)) return AppPaths.InstalledExe;

        if (string.Equals(Path.GetFullPath(current), Path.GetFullPath(AppPaths.InstalledExe), StringComparison.OrdinalIgnoreCase))
            return current;

        // A development build (an .exe with its .dll beside it) can't run from another folder, so use it where it is.
        // The real download is a single self-contained .exe and is copied below.
        if (File.Exists(Path.ChangeExtension(current, ".dll"))) return current;

        try
        {
            Directory.CreateDirectory(AppPaths.InstallDir);
            File.Copy(current, AppPaths.InstalledExe, overwrite: true);
            log.Info($"Installed a copy of the program to {AppPaths.InstalledExe}");
            return AppPaths.InstalledExe;
        }
        catch (Exception ex)
        {
            // Copy may fail if the installed copy is running right now; use it if it exists.
            log.Warn($"Could not update the installed copy: {ex.Message}");
            return File.Exists(AppPaths.InstalledExe) ? AppPaths.InstalledExe : current;
        }
    }

    public static (bool ok, string error) Create(Settings s, string exePath)
    {
        // The "also run at sign-in" trigger needs administrator rights on many PCs ("Access is denied").
        // Try with it first; if Windows refuses, use the weekly trigger alone (missed runs still catch up).
        var result = CreateCore(s, exePath, includeLogon: true);
        if (!result.ok) result = CreateCore(s, exePath, includeLogon: false);
        return result;
    }

    private static (bool ok, string error) CreateCore(Settings s, string exePath, bool includeLogon)
    {
        var user = WindowsIdentity.GetCurrent().Name;
        var start = DateTime.Today.AddHours(s.Hour).AddMinutes(s.Minute).ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture); // fixed format, whatever the PC's language/calendar
        var day = s.Day.ToString(); // Sunday, Monday, ...
        var logonTrigger = includeLogon
            ? $@"    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>{SecurityElement.Escape(user)}</UserId>
      <Delay>PT5M</Delay>
    </LogonTrigger>
"
            : "";

        var xml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.4"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Weekly backup to your USB drive (BTS SimpleBackup).</Description>
  </RegistrationInfo>
  <Triggers>
    <CalendarTrigger>
      <StartBoundary>{start}</StartBoundary>
      <Enabled>true</Enabled>
      <ScheduleByWeek>
        <DaysOfWeek><{day} /></DaysOfWeek>
        <WeeksInterval>1</WeeksInterval>
      </ScheduleByWeek>
    </CalendarTrigger>
{logonTrigger}  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>{SecurityElement.Escape(user)}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <StartWhenAvailable>true</StartWhenAvailable>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <ExecutionTimeLimit>PT24H</ExecutionTimeLimit>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{SecurityElement.Escape(exePath)}</Command>
      <Arguments>--scheduled</Arguments>
    </Exec>
  </Actions>
</Task>";

        var tmp = Path.Combine(Path.GetTempPath(), $"BTSSimpleBackup-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(tmp, xml, Encoding.Unicode);
            var (code, output) = RunSchtasks($"/Create /TN \"{AppPaths.TaskName}\" /XML \"{tmp}\" /F");
            return code == 0 ? (true, "") : (false, output.Trim());
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }

    public static (bool ok, string error) Remove()
    {
        // Nothing to remove is fine (checked without relying on Windows' English wording).
        if (!Exists()) return (true, "");
        var (code, output) = RunSchtasks($"/Delete /TN \"{AppPaths.TaskName}\" /F");
        return code == 0 ? (true, "") : (false, output.Trim());
    }

    /// <summary>
    /// Deletes the weekly task with a Windows permission prompt. Only used when an older task (made by a copy run as
    /// administrator) can't be changed normally. Returns true if the task is gone afterwards; false if the user said No.
    /// </summary>
    public static bool RemoveWithPermissionPrompt()
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", $"/Delete /TN \"{AppPaths.TaskName}\" /F")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(30000);
        }
        catch
        {
            // The user chose No on the permission prompt, or Windows refused: fall through and report what is really there.
        }
        return !Exists();
    }

    public static bool Exists() => RunSchtasks($"/Query /TN \"{AppPaths.TaskName}\"").code == 0;

    private static (int code, string output) RunSchtasks(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi)!;
            var outText = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(30000);
            return (p.ExitCode, outText);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}

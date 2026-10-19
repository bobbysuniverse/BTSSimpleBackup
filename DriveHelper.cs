using System.Runtime.InteropServices;
using System.Text;

namespace BTSSimpleBackup;

internal sealed record DriveEntry(string Root, string Label, uint Serial, long TotalBytes, long FreeBytes, DriveType Type)
{
    public override string ToString()
    {
        var name = string.IsNullOrWhiteSpace(Label) ? "Removable drive" : Label;
        return $"{name} ({Root.TrimEnd('\\')})  -  {FileSize.Bytes(FreeBytes)} free of {FileSize.Bytes(TotalBytes)}";
    }
}

internal static class DriveHelper
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeInformationW(string root, StringBuilder? volName, int volNameSize,
        out uint serial, out uint maxComponent, out uint flags, StringBuilder? fsName, int fsNameSize);

    public static uint GetSerial(string root)
    {
        return GetVolumeInformationW(root, null, 0, out var serial, out _, out _, null, 0) ? serial : 0;
    }

    /// <summary>
    /// Drives that make sense as a backup target: USB sticks and external drives.
    /// The drive Windows is installed on is never offered, so nobody "backs up" onto the same disk.
    /// </summary>
    public static List<DriveEntry> GetCandidateDrives()
    {
        var result = new List<DriveEntry>();
        var systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";

        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                if (d.DriveType is not (DriveType.Removable or DriveType.Fixed)) continue;
                if (string.Equals(d.Name, systemRoot, StringComparison.OrdinalIgnoreCase)) continue;
                result.Add(new DriveEntry(d.Name, d.VolumeLabel, GetSerial(d.Name), d.TotalSize, d.AvailableFreeSpace, d.DriveType));
            }
            catch { /* drive vanished while we looked */ }
        }
        return result;
    }

    /// <summary>
    /// Finds the saved drive by its serial number. The label is used only when no serial number could be read,
    /// so a different stick that happens to be called "BACKUP" is never picked by mistake.
    /// </summary>
    public static DriveEntry? FindSavedDrive(Settings s)
    {
        var drives = GetCandidateDrives();
        if (s.DriveSerial != 0)
            return drives.FirstOrDefault(d => d.Serial == s.DriveSerial);
        if (!string.IsNullOrWhiteSpace(s.DriveLabel))
            return drives.FirstOrDefault(d => string.Equals(d.Label, s.DriveLabel, StringComparison.OrdinalIgnoreCase));
        return null;
    }
}

internal static class FileSize
{
    public static string Bytes(long b)
    {
        string[] units = { "bytes", "KB", "MB", "GB", "TB" };
        double v = b; int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{v:0} {units[i]}" : $"{v:0.#} {units[i]}";
    }
}

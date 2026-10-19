using System.Security.Cryptography;
using System.Text;

namespace BTSSimpleBackup;

internal sealed record ProgressInfo(string File, long FileLength, bool OnlineOnly, int FilesDone, int FilesTotal, long BytesDone, long BytesTotal);

internal sealed record BackupResult(bool Ok, string Message, int Copied = 0, int UpToDate = 0, int Failed = 0, long BytesCopied = 0);

/// <summary>
/// Copies new and changed files to the backup drive.
/// Safety rules: it never deletes or modifies anything on the PC, and it never deletes anything
/// on the backup drive. If a file changed, the one older copy is kept in "_Previous versions".
/// </summary>
internal sealed class BackupEngine
{
    public const string RootFolderName = "BTS SimpleBackup";

    /// <summary>Folders that are never worth backing up: temporary caches that are rebuilt automatically.</summary>
    public static readonly HashSet<string> SkippedFolderNames = new(StringComparer.OrdinalIgnoreCase) { "$RECYCLE.BIN", ".vs" };
    private const string PreviousFolderName = "_Previous versions";
    private const long FatFileLimit = 4L * 1024 * 1024 * 1024 - 1;

    private readonly Settings _settings;
    private readonly Logger _log;

    /// <summary>Reports which file is being copied and how many bytes of the whole job are done (updates inside big files too).</summary>
    public event Action<ProgressInfo>? Progress;

    private readonly byte[] _buffer = new byte[4 * 1024 * 1024];

    public BackupEngine(Settings settings, Logger log)
    {
        _settings = settings;
        _log = log;
    }

    private sealed record Job(string Source, string Dest, string Previous, long Length, DateTime LastWriteUtc);

    public BackupResult Run(CancellationToken ct)
    {
        try
        {
            return RunCore(ct);
        }
        catch (OperationCanceledException)
        {
            _log.Warn("Backup cancelled by user.");
            return new BackupResult(false, "Backup cancelled. Files already copied are safe; the next backup will continue.");
        }
    }

    private BackupResult RunCore(CancellationToken ct)
    {
        _log.Info("---- Backup started ----");

        var drive = DriveHelper.FindSavedDrive(_settings);
        if (drive == null)
        {
            var name = string.IsNullOrWhiteSpace(_settings.DriveLabel) ? "your backup drive" : $"\"{_settings.DriveLabel}\"";
            _log.Warn("Backup drive not found.");
            return new BackupResult(false, $"Please plug in {name} and try again.");
        }

        var folders = _settings.Folders.Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (folders.Count == 0)
            return new BackupResult(false, "No folders are selected to back up.");

        string driveFormat;
        try { driveFormat = new DriveInfo(drive.Root).DriveFormat; } catch { driveFormat = ""; }
        bool isFat32 = driveFormat.Equals("FAT32", StringComparison.OrdinalIgnoreCase);

        var machine = SanitizeName(Environment.MachineName);
        var backupRoot = Path.Combine(drive.Root, RootFolderName, machine);
        var previousRoot = Path.Combine(backupRoot, PreviousFolderName);
        _log.Info($"Backing up to {backupRoot} ({driveFormat})");

        // Pick a destination name for each source folder (add a short code if two share a name).
        var labels = BuildLabels(folders);

        // Step 1: work out what needs copying.
        var jobs = new List<Job>();
        int upToDate = 0, failed = 0, onlineOnly = 0;
        long skippedHuge = 0;

        foreach (var src in folders)
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(src))
            {
                _log.Warn($"Folder not found, skipped: {src}");
                continue;
            }
            if (src.StartsWith(drive.Root, StringComparison.OrdinalIgnoreCase))
            {
                _log.Warn($"Folder is on the backup drive itself, skipped: {src}");
                continue;
            }

            var label = labels[src];
            foreach (var file in EnumerateFiles(src, ct))
            {
                try
                {
                    var info = new FileInfo(file);
                    var rel = Path.GetRelativePath(src, file);
                    var dest = Path.Combine(backupRoot, label, rel);

                    if (isFat32 && info.Length > FatFileLimit)
                    {
                        _log.Warn($"Too big for a FAT32 drive (4 GB limit), skipped: {file}");
                        skippedHuge++;
                        failed++;
                        continue;
                    }

                    var destInfo = new FileInfo(dest);
                    // FAT drives store local time, so when daylight saving changes every file can look exactly one hour
                    // different. Treat that as "unchanged" on FAT32 instead of copying the whole drive again.
                    double secondsApart = Math.Abs((destInfo.LastWriteTimeUtc - info.LastWriteTimeUtc).TotalSeconds);
                    bool timeMatches = secondsApart <= 2 || (isFat32 && Math.Abs(secondsApart - 3600) <= 2);
                    bool same = destInfo.Exists && destInfo.Length == info.Length && timeMatches;
                    if (same) { upToDate++; continue; }

                    if (IsOnlineOnly(info.Attributes)) onlineOnly++;
                    jobs.Add(new Job(file, dest, Path.Combine(previousRoot, label, rel), info.Length, info.LastWriteTimeUtc));
                }
                catch (Exception ex)
                {
                    _log.Warn($"Could not read {file}: {ex.Message}");
                    failed++;
                }
            }
        }

        // Step 2: make sure there is room.
        long needed = jobs.Sum(j => j.Length);
        long free;
        try { free = new DriveInfo(drive.Root).AvailableFreeSpace; } catch { free = drive.FreeBytes; }
        _log.Info($"{jobs.Count} file(s) to copy ({FileSize.Bytes(needed)}); {upToDate} already up to date; {FileSize.Bytes(free)} free on drive.");

        if (onlineOnly > 0)
            _log.Info($"{onlineOnly} of those file(s) are online-only (OneDrive) and must be downloaded before they can be copied. This can be slow.");

        if (needed + 64L * 1024 * 1024 > free)
        {
            var msg = $"The backup drive is too full. This backup needs about {FileSize.Bytes(needed)} but only {FileSize.Bytes(free)} is free. " +
                      "Use a bigger drive, or choose fewer folders.";
            _log.Error(msg);
            return new BackupResult(false, msg);
        }

        // Step 3: copy.
        int copied = 0, done = 0;
        long bytes = 0;          // bytes successfully copied
        long handled = 0;        // bytes of finished files, copied or skipped (keeps the progress bar moving forward)
        try
        {
            Directory.CreateDirectory(backupRoot);
            WriteRestoreNote(Path.Combine(drive.Root, RootFolderName));

            foreach (var job in jobs)
            {
                ct.ThrowIfCancellationRequested();
                bool cloud = false;
                try { cloud = IsOnlineOnly(File.GetAttributes(job.Source)); } catch { }
                var baseBytes = handled;
                Progress?.Invoke(new ProgressInfo(job.Source, job.Length, cloud, done, jobs.Count, baseBytes, needed));
                try
                {
                    CopyOne(job, ct, inFile =>
                        Progress?.Invoke(new ProgressInfo(job.Source, job.Length, cloud, done, jobs.Count, baseBytes + Math.Min(inFile, job.Length), needed)));
                    copied++;
                    bytes += job.Length;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One problem file must never stop the rest of the backup.
                    if (!Directory.Exists(drive.Root))
                    {
                        _log.Error("The backup drive was removed during the backup.");
                        return new BackupResult(false, "The backup drive was unplugged before the backup finished. Plug it back in and run the backup again; it will pick up where it left off.", copied, upToDate, failed, bytes);
                    }
                    _log.Warn($"Could not copy (in use, no permission or unreadable): {job.Source} - {ex.Message}");
                    failed++;
                }
                handled += job.Length;
                done++;
            }
            Progress?.Invoke(new ProgressInfo("", 0, false, done, jobs.Count, needed, needed));
        }
        catch (OperationCanceledException)
        {
            _log.Warn("Backup cancelled by user.");
            return new BackupResult(false, "Backup cancelled. Files already copied are safe; the next backup will continue.", copied, upToDate, failed, bytes);
        }

        _log.Info($"---- Backup finished: {copied} copied ({FileSize.Bytes(bytes)}), {upToDate} already up to date, {failed} skipped ----");

        string summary = copied == 0 && failed == 0
            ? "Everything was already backed up."
            : $"Backed up {copied:N0} file(s) ({FileSize.Bytes(bytes)}).";
        if (skippedHuge > 0)
            summary += $" {skippedHuge:N0} file(s) are too big for this FAT32 drive (4 GB limit). Reformat the drive as exFAT or NTFS to back them up.";
        long otherFailed = failed - skippedHuge;
        if (otherFailed > 0)
            summary += $" {otherFailed:N0} file(s) could not be copied (usually because a program had them locked). They will be tried again next time.";

        return new BackupResult(true, summary, copied, upToDate, failed, bytes);
    }

    // OneDrive "online-only" files carry these Windows flags. .NET has no names for the last two, so use their values.
    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    private static bool IsOnlineOnly(FileAttributes a) =>
        (a & (FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess)) != 0;

    private void CopyOne(Job job, CancellationToken ct, Action<long> onBytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(job.Dest)!);

        // Copy to a temporary name first, so a half-finished copy is never mistaken for a good file.
        // The existing backup copy is only touched AFTER the new copy has been written completely.
        var temp = job.Dest + ".partial";
        try
        {
            // Copy in 4 MB pieces so progress is shown inside big files and Cancel works at once.
            // The source is opened in shared mode so files that another program has open (but not locked) can still be read.
            using (var src = new FileStream(job.Source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan))
            using (var dst = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            {
                long total = 0;
                int read;
                while ((read = src.Read(_buffer, 0, _buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    dst.Write(_buffer, 0, read);
                    total += read;
                    onBytes(total);
                }
            }
            // If the drive can't hold this timestamp (for example a date before 1980 on FAT), the file is still good.
            try { File.SetLastWriteTimeUtc(temp, job.LastWriteUtc); } catch { }

            // Keep the one older copy of a changed file, then put the new one in place.
            if (File.Exists(job.Dest))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(job.Previous)!);
                File.Move(job.Dest, job.Previous, overwrite: true);
            }
            File.Move(temp, job.Dest, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            throw;
        }
    }

    private IEnumerable<string> EnumerateFiles(string root, CancellationToken ct)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = pending.Pop();

            string[] files = Array.Empty<string>();
            string[] subs = Array.Empty<string>();
            try { files = Directory.GetFiles(dir); }
            catch (Exception ex) { _log.Warn($"Could not read the files in folder {dir}: {ex.Message}"); }
            try { subs = Directory.GetDirectories(dir); }
            catch (Exception ex) { _log.Warn($"Could not read the subfolders of {dir}: {ex.Message}"); }

            foreach (var f in files)
            {
                var name = Path.GetFileName(f);
                if (name.StartsWith("~$", StringComparison.Ordinal)) continue;          // Office lock files
                if (name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                yield return f;
            }

            foreach (var s in subs)
            {
                try
                {
                    var attr = File.GetAttributes(s);
                    if ((attr & FileAttributes.ReparsePoint) != 0)
                    {
                        // Skip real symlinks and junctions (they can loop back on themselves). OneDrive and other
                        // cloud folders also carry the "reparse point" flag, but they are NOT links, so keep those.
                        string? linkTarget = null;
                        try { linkTarget = new DirectoryInfo(s).LinkTarget; } catch { }
                        if (linkTarget != null)
                        {
                            _log.Info($"Skipped shortcut folder (a link to {linkTarget}): {s}");
                            continue;
                        }
                    }
                }
                catch { continue; }
                var n = Path.GetFileName(s);
                if (SkippedFolderNames.Contains(n)) continue;
                pending.Push(s);
            }
        }
    }

    private static Dictionary<string, string> BuildLabels(List<string> folders)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var leafOf = folders.ToDictionary(f => f, f => SanitizeName(LeafName(f)), StringComparer.OrdinalIgnoreCase);
        var counts = leafOf.Values.GroupBy(v => v, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var f in folders)
        {
            var leaf = leafOf[f];
            if (counts[leaf] > 1)
            {
                var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(f.ToLowerInvariant())))[..6];
                leaf = $"{leaf} ({hash})";
            }
            map[f] = leaf;
        }
        return map;
    }

    private static string LeafName(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var leaf = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(leaf) ? trimmed.Replace(":", "").Replace("\\", "") : leaf;
    }

    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (var c in name) sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        var s = sb.ToString().Trim().TrimEnd('.');
        return s.Length == 0 ? "Folder" : s;
    }

    private static void WriteRestoreNote(string root)
    {
        try
        {
            Directory.CreateDirectory(root);
            var note = Path.Combine(root, "READ ME - how to get your files back.txt");
            if (File.Exists(note)) return;
            File.WriteAllText(note,
@"This drive holds backups made by BTS SimpleBackup.

HOW TO GET YOUR FILES BACK
  1. Open this drive in File Explorer.
  2. Open the 'BTS SimpleBackup' folder, then the folder named after your computer.
  3. Inside you will find a folder for each thing that was backed up (Documents, Pictures, ...).
  4. Copy the files or folders you want back to your computer. They are normal files - no special program is needed.

THE '_Previous versions' FOLDER
  When a file changes, the older copy is kept there (one older copy per file), in case you need to go back.

Backups only ever ADD or UPDATE files here. Files you delete from your computer are NOT removed from this drive.

TIP: Unplug the drive and keep it somewhere safe when you are not backing up. A drive that is not plugged in
cannot be hit by a virus or ransomware.
");
        }
        catch { }
    }
}

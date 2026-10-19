using System.Diagnostics;

namespace BTSSimpleBackup;

/// <summary>Runs one backup at a time (even if a scheduled run and a manual run overlap) and records the outcome.</summary>
internal static class BackupRunner
{
    public static BackupResult Run(Settings settings, Logger log, CancellationToken ct, Action<ProgressInfo>? progress)
    {
        using var mutex = new Mutex(false, @"Local\BTSSimpleBackup.Engine");
        bool got;
        try { got = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { got = true; }
        if (!got)
            return new BackupResult(false, "A backup is already running. Please wait for it to finish.");

        try
        {
            var status = Status.Load();
            status.LastAttemptLocal = DateTime.Now;

            BackupResult result;
            try
            {
                var engine = new BackupEngine(settings, log);
                if (progress != null)
                {
                    var sw = Stopwatch.StartNew();
                    engine.Progress += p =>
                    {
                        // Don't flood the window with updates when there are thousands of tiny files.
                        if (sw.ElapsedMilliseconds < 200 && p.FilesDone != p.FilesTotal) return;
                        sw.Restart();
                        progress(p);
                    };
                }
                result = engine.Run(ct);
            }
            catch (Exception ex)
            {
                log.Error(ex.ToString());
                result = new BackupResult(false, "Something unexpected went wrong: " + ex.Message + " (Details were saved to the log.)");
            }

            status.LastAttemptOk = result.Ok;
            status.Message = result.Message;
            if (result.Ok) status.LastSuccessLocal = DateTime.Now;
            status.Save();
            return result;
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}

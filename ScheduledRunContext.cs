namespace BTSSimpleBackup;

/// <summary>
/// The no-window mode used by the weekly schedule: shows a tray notification when the backup
/// starts and when it finishes (including a friendly reminder if the drive isn't plugged in).
/// </summary>
internal sealed class ScheduledRunContext : ApplicationContext
{
    // The schedule also fires at sign-in as a safety net. If a backup succeeded recently, do nothing.
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromDays(5);

    private readonly NotifyIcon? _icon;
    private readonly System.Windows.Forms.Timer? _pollTimer;
    private readonly Task<BackupResult>? _task;
    private readonly Logger _log = new();

    public ScheduledRunContext()
    {
        var settings = Settings.Load();

        bool configured = settings.Folders.Count > 0 &&
                          (settings.DriveSerial != 0 || !string.IsNullOrWhiteSpace(settings.DriveLabel));
        if (!configured)
        {
            _log.Info("Scheduled run: not set up yet, nothing to do.");
            ExitSoon();
            return;
        }

        var status = Status.Load();
        if (status.LastSuccessLocal is { } last && DateTime.Now - last < QuietPeriod)
        {
            _log.Info($"Scheduled run: last good backup was {last:g}, not due yet.");
            ExitSoon();
            return;
        }

        _icon = new NotifyIcon
        {
            Icon = Branding.AppIcon,
            Text = AppPaths.AppName,
            Visible = true
        };
        _icon.ShowBalloonTip(5000, AppPaths.AppName,
            "Your weekly backup is starting. You can keep using your computer.", ToolTipIcon.Info);

        _task = Task.Run(() => BackupRunner.Run(settings, _log, CancellationToken.None, null));

        _pollTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _pollTimer.Tick += OnPoll;
        _pollTimer.Start();
    }

    private void OnPoll(object? sender, EventArgs e)
    {
        if (_task is null || !_task.IsCompleted) return;
        _pollTimer!.Stop();

        BackupResult result = _task.IsCompletedSuccessfully
            ? _task.Result
            : new BackupResult(false, "Something unexpected went wrong. Open BTS SimpleBackup for details.");

        var text = result.Message;
        if (!result.Ok)
            text += " Your backup will try again the next time you sign in to Windows, or you can open BTS SimpleBackup and click \"Back up now\".";

        _icon!.ShowBalloonTip(15000, result.Ok ? "Backup complete" : "Backup needs attention", text,
            result.Ok ? ToolTipIcon.Info : ToolTipIcon.Warning);

        // Keep the icon around long enough for the message to be read.
        var closeTimer = new System.Windows.Forms.Timer { Interval = 20000 };
        closeTimer.Tick += (_, _) => { closeTimer.Stop(); Exit(); };
        closeTimer.Start();
    }

    // ExitThread can't be called from the constructor (the message loop hasn't started yet).
    private void ExitSoon()
    {
        var t = new System.Windows.Forms.Timer { Interval = 100 };
        t.Tick += (_, _) => { t.Stop(); t.Dispose(); Exit(); };
        t.Start();
    }

    private void Exit()
    {
        if (_icon != null) { _icon.Visible = false; _icon.Dispose(); }
        _pollTimer?.Dispose();
        ExitThread();
    }
}

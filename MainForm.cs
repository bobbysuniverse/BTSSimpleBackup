using System.Diagnostics;

namespace BTSSimpleBackup;

/// <summary>The one window: pick a drive, pick folders, pick a day, press Save.</summary>
internal sealed class MainForm : Form
{
    private sealed record FolderItem(string Name, string Path, bool Custom)
    {
        public override string ToString() => Custom ? $"{Name}   ({Path})" : Name;
    }

    private readonly Settings _settings = Settings.Load();
    private readonly Logger _log = new();

    private readonly Label _statusLabel = new() { AutoSize = true, MaximumSize = new Size(640, 0), Font = new Font("Segoe UI", 11f, FontStyle.Bold) };
    private readonly Label _scheduleLabel = new() { AutoSize = true, MaximumSize = new Size(640, 0) };
    private readonly ComboBox _driveCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 480 };
    private readonly Label _driveHint = new() { AutoSize = true, MaximumSize = new Size(640, 0) };
    private readonly CheckedListBox _folderList = new() { CheckOnClick = true, Height = 150, Width = 560, IntegralHeight = false };
    private readonly Label _sizeLabel = new() { AutoSize = true, MaximumSize = new Size(640, 0) };
    private readonly CheckBox _autoCheck = new() { Text = "Back up automatically every week", AutoSize = true, Checked = true };
    private readonly ComboBox _dayCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly DateTimePicker _timePicker = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "h:mm tt", ShowUpDown = true, Width = 100 };
    private readonly Button _saveButton = new() { Text = "Save and turn on weekly backup", AutoSize = true, Padding = new Padding(10, 6, 10, 6) };
    private readonly Button _nowButton = new() { Text = "Back up now", AutoSize = true, Padding = new Padding(10, 6, 10, 6) };
    private readonly Button _openDriveButton = new() { Text = "Open backup drive", AutoSize = true, Padding = new Padding(6, 6, 6, 6) };
    private readonly Button _logButton = new() { Text = "View log", AutoSize = true, Padding = new Padding(6, 6, 6, 6) };
    private readonly Button _aboutButton = new() { Text = "About", AutoSize = true, Padding = new Padding(6, 6, 6, 6) };
    private readonly Button _uninstallButton = new() { Text = "Uninstall...", AutoSize = true, Padding = new Padding(6, 6, 6, 6) };
    private readonly Button _cancelButton = new() { Text = "Cancel backup", AutoSize = true, Visible = false };
    private readonly ProgressBar _progress = new() { Width = 560, Height = 18, Visible = false };
    private readonly Label _progressLabel = new() { AutoSize = true, MaximumSize = new Size(640, 0), Visible = false };

    private CancellationTokenSource? _backupCts;
    private CancellationTokenSource? _sizeCts;
    private long _selectedBytes = -1;
    private bool _busy;
    private bool _closeWhenDone;
    private bool _copyStarted;
    private readonly Stopwatch _busyWatch = new();

    public MainForm()
    {
        Text = AppPaths.AppName;
        Font = new Font("Segoe UI", 10f);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Icon = Branding.AppIcon;

        BuildLayout();
        LoadIntoControls();
        WireEvents();

        RefreshDrives();
        RefreshStatus();
        // The space estimate needs the window to exist (it updates the screen when it finishes), so start it once the window is shown.
        Shown += (_, _) => UpdateSizeEstimate();
    }

    // ---------- layout ----------

    private void BuildLayout()
    {
        var root = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(16),
            Dock = DockStyle.Fill
        };

        var title = new Label
        {
            Text = "Back up your computer to a USB drive",
            Font = new Font("Segoe UI", 15f, FontStyle.Bold),
            AutoSize = true
        };
        var privacy = new Label
        {
            Text = "Your files go only to the drive you plug in. Nothing is sent over the internet.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText
        };

        // Header: logo on the left, title and privacy line on the right.
        var header = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Margin = Padding.Empty };
        if (Branding.Logo is { } logo)
        {
            const int logoHeight = 76;
            var logoBox = new PictureBox
            {
                Image = logo,
                SizeMode = PictureBoxSizeMode.Zoom,
                Height = logoHeight,
                Width = (int)Math.Round(logo.Width * (double)logoHeight / logo.Height),
                Margin = new Padding(0, 0, 14, 0)
            };
            header.Controls.Add(logoBox);
        }
        var headerText = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0, 6, 0, 0) };
        headerText.Controls.Add(title);
        headerText.Controls.Add(privacy);
        privacy.MaximumSize = new Size(520, 0);
        header.Controls.Add(headerText);

        root.Controls.Add(header);
        root.Controls.Add(Spacer(6));
        root.Controls.Add(_statusLabel);
        root.Controls.Add(_scheduleLabel);
        root.Controls.Add(Spacer(8));

        // 1. Drive
        var driveBox = Group("1.  Choose your backup drive");
        var refresh = new Button { Text = "Refresh", AutoSize = true };
        refresh.Click += (_, _) => RefreshDrives();
        var driveRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        driveRow.Controls.Add(_driveCombo);
        driveRow.Controls.Add(refresh);
        driveBox.Controls.Add(driveRow);
        driveBox.Controls.Add(_driveHint);
        root.Controls.Add(driveBox);

        // 2. Folders
        var folderBox = Group("2.  Choose what to back up");
        var addBtn = new Button { Text = "Add another folder...", AutoSize = true };
        var removeBtn = new Button { Text = "Remove selected folder", AutoSize = true };
        addBtn.Click += (_, _) => AddFolder();
        removeBtn.Click += (_, _) => RemoveSelectedFolder();
        var folderButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        folderButtons.Controls.Add(addBtn);
        folderButtons.Controls.Add(removeBtn);
        folderBox.Controls.Add(_folderList);
        folderBox.Controls.Add(folderButtons);
        folderBox.Controls.Add(_sizeLabel);
        root.Controls.Add(folderBox);

        // 3. Schedule
        var schedBox = Group("3.  Choose when");
        var when = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        when.Controls.Add(new Label { Text = "Every", AutoSize = true, Margin = new Padding(3, 6, 3, 0) });
        when.Controls.Add(_dayCombo);
        when.Controls.Add(new Label { Text = "at", AutoSize = true, Margin = new Padding(3, 6, 3, 0) });
        when.Controls.Add(_timePicker);
        schedBox.Controls.Add(_autoCheck);
        schedBox.Controls.Add(when);
        schedBox.Controls.Add(new Label
        {
            Text = "If your computer is off at that time, the backup runs the next time it is on. Keep the drive plugged in.",
            AutoSize = true,
            MaximumSize = new Size(600, 0),
            ForeColor = SystemColors.GrayText
        });
        root.Controls.Add(schedBox);

        root.Controls.Add(Spacer(6));

        // Buttons
        var main = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        _saveButton.Font = new Font(Font, FontStyle.Bold);
        main.Controls.Add(_saveButton);
        main.Controls.Add(_nowButton);
        root.Controls.Add(main);

        root.Controls.Add(_progress);
        root.Controls.Add(_progressLabel);
        root.Controls.Add(_cancelButton);

        root.Controls.Add(Spacer(4));
        var extras = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        extras.Controls.Add(_openDriveButton);
        extras.Controls.Add(_logButton);
        extras.Controls.Add(_aboutButton);
        extras.Controls.Add(_uninstallButton);
        root.Controls.Add(extras);

        Controls.Add(root);
    }

    private static Control Spacer(int h) => new Panel { Height = h, Width = 1, Margin = Padding.Empty };

    private static FlowLayoutPanel Group(string heading)
    {
        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 6, 0, 6)
        };
        panel.Controls.Add(new Label { Text = heading, AutoSize = true, Font = new Font("Segoe UI", 11f, FontStyle.Bold) });
        return panel;
    }

    // ---------- settings <-> controls ----------

    private static List<FolderItem> KnownFolders()
    {
        var list = new List<FolderItem>();
        void Add(string name, string? path)
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                list.Add(new FolderItem(name, path, false));
        }
        Add("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Add("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        Add("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        Add("Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
        Add("Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
        Add("Downloads", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
        return list;
    }

    private void LoadIntoControls()
    {
        var known = KnownFolders();
        bool firstRun = _settings.Folders.Count == 0;
        var defaultOn = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Documents", "Pictures", "Desktop" };

        foreach (var k in known)
        {
            bool on = firstRun
                ? defaultOn.Contains(k.Name)
                : _settings.Folders.Contains(k.Path, StringComparer.OrdinalIgnoreCase);
            _folderList.Items.Add(k, on);
        }
        foreach (var path in _settings.Folders)
        {
            if (known.Any(k => string.Equals(k.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
            _folderList.Items.Add(new FolderItem(Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } n ? n : path, path, true), true);
        }

        foreach (DayOfWeek d in new[] { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday })
            _dayCombo.Items.Add(d);
        _dayCombo.SelectedItem = _settings.Day;
        _timePicker.Value = DateTime.Today.AddHours(_settings.Hour).AddMinutes(_settings.Minute);
        _autoCheck.Checked = firstRun || _settings.ScheduleEnabled;
    }

    private bool ReadControlsIntoSettings(bool requireDrive)
    {
        var drive = _driveCombo.SelectedItem as DriveEntry;
        if (drive != null)
        {
            _settings.DriveSerial = drive.Serial;
            _settings.DriveLabel = drive.Label;
        }
        else if (requireDrive)
        {
            Warn("Please plug in your USB drive, click \"Refresh\", and choose it from the list in step 1.");
            return false;
        }

        _settings.Folders = _folderList.CheckedItems.Cast<FolderItem>().Select(f => f.Path).ToList();
        if (_settings.Folders.Count == 0)
        {
            Warn("Please tick at least one folder to back up in step 2.");
            return false;
        }

        if (drive != null && _settings.Folders.Any(f => f.StartsWith(drive.Root, StringComparison.OrdinalIgnoreCase)))
        {
            Warn("One of the chosen folders is on the backup drive itself. Please remove it from step 2.");
            return false;
        }

        _settings.Day = _dayCombo.SelectedItem is DayOfWeek d ? d : DayOfWeek.Sunday;
        _settings.Hour = _timePicker.Value.Hour;
        _settings.Minute = _timePicker.Value.Minute;
        _settings.ScheduleEnabled = _autoCheck.Checked;
        return true;
    }

    // ---------- events ----------

    private void WireEvents()
    {
        _saveButton.Click += (_, _) => SaveAndSchedule();
        _uninstallButton.Click += (_, _) => Uninstall();
        _nowButton.Click += async (_, _) => await BackUpNowAsync();
        _cancelButton.Click += (_, _) => { _backupCts?.Cancel(); _cancelButton.Enabled = false; };
        _folderList.ItemCheck += (_, _) => BeginInvoke(UpdateSizeEstimate);
        _driveCombo.SelectedIndexChanged += (_, _) => UpdateDriveHint();

        _openDriveButton.Click += (_, _) =>
        {
            var d = _driveCombo.SelectedItem as DriveEntry ?? DriveHelper.FindSavedDrive(_settings);
            if (d == null) { Warn("The backup drive isn't plugged in."); return; }
            var folder = Path.Combine(d.Root, BackupEngine.RootFolderName);
            OpenInExplorer(Directory.Exists(folder) ? folder : d.Root);
        };
        _logButton.Click += (_, _) =>
        {
            var latest = new DirectoryInfo(AppPaths.LogDir).GetFiles("backup-*.log").OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
            if (latest == null) { Info("There is no log yet. One is created the first time a backup runs."); return; }
            try { Process.Start(new ProcessStartInfo(latest.FullName) { UseShellExecute = true }); }
            catch { OpenInExplorer(AppPaths.LogDir); }
        };
        _aboutButton.Click += (_, _) => Info(
            "BTS SimpleBackup 1.0\n\n" +
            "A free, open-source gift to the community from Bishop Technical Services LLC.\n\n" +
            "• Copies your files to a drive you own. No cloud, no account, no internet connection.\n" +
            "• Never deletes anything from your computer, and never deletes anything from the backup drive.\n" +
            "• Keeps one older copy of any file that changes.\n" +
            "• Use the Uninstall button to remove it completely. Your backup drive is never touched.\n" +
            "• To get files back, just open the drive and copy them. They are normal files.\n\n" +
            "Released under the MIT License.");

        FormClosing += (_, e) =>
        {
            if (_busy)
            {
                if (_closeWhenDone) { e.Cancel = true; return; }   // already stopping
                var answer = MessageBox.Show(this, "A backup is running. Stop it and close the window?\n\nFiles already copied are safe; the next backup continues where this one stopped.",
                    AppPaths.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes) { e.Cancel = true; return; }

                // Don't close yet: let the current file finish stopping cleanly, then the window closes by itself.
                e.Cancel = true;
                _closeWhenDone = true;
                _cancelButton.Enabled = false;
                _progressLabel.Text = "Stopping the backup...";
                _backupCts?.Cancel();
                return;
            }
            _sizeCts?.Cancel();
        };
    }

    private void SaveAndSchedule()
    {
        if (!ReadControlsIntoSettings(requireDrive: true)) return;

        // Keep a permanent copy of the program and add the shortcuts (once), so the user can always find it again.
        var exe = ScheduleHelper.EnsureInstalledCopy(_log);
        bool addedShortcuts = false;
        if (!_settings.ShortcutsCreated || !File.Exists(ShortcutHelper.StartMenuLink))
        {
            ShortcutHelper.CreateAll(exe, _log);
            _settings.ShortcutsCreated = true;
            addedShortcuts = File.Exists(ShortcutHelper.DesktopLink);
        }
        _settings.Save();
        var shortcutNote = addedShortcuts
            ? "\n\nA \"BTS SimpleBackup\" icon was added to your desktop and Start menu, so you can open it again any time."
            : "";

        if (_settings.ScheduleEnabled)
        {
            var (ok, error) = ScheduleHelper.Create(_settings, exe);
            // An older schedule that this copy can't change (usually made by a copy run as administrator) is the most common cause.
            if (!ok && ScheduleHelper.Exists() && AskToClearOldSchedule())
                (ok, error) = ScheduleHelper.Create(_settings, exe);
            if (!ok)
            {
                _log.Error("Could not create the weekly schedule: " + error);
                Warn("Your choices were saved, but Windows would not let us set up the weekly schedule.\n\n" + error +
                     "\n\nYou can still use \"Back up now\". If this keeps happening, restart your computer and click Save again.");
                RefreshStatus();
                return;
            }
            Info($"All set!\n\nYour computer will back up every {_settings.Day} at {_timePicker.Value:h:mm tt} to \"{DriveName()}\".\n\n" +
                 "Just keep the drive plugged in. If it isn't, you'll get a reminder.\n\n" +
                 "Tip: click \"Back up now\" to make your first backup right away." + shortcutNote);
        }
        else
        {
            var (removed, removeError) = ScheduleHelper.Remove();
            if (!removed && ScheduleHelper.Exists() && AskToClearOldSchedule()) removed = true;
            if (!removed) _log.Error("Could not remove the weekly schedule: " + removeError);
            var removeNote = removed ? "" :
                "\n\nWindows would not let us turn off the existing weekly schedule, so it may still run. Restart your computer and try again if you want it off.";
            Info("Your choices were saved. Automatic weekly backup is turned off; use \"Back up now\" whenever you like." + removeNote + shortcutNote);
        }
        RefreshStatus();
    }

    private void Uninstall()
    {
        var answer = MessageBox.Show(this,
            "This removes BTS SimpleBackup from this computer:\n\n" +
            "• the weekly backup schedule\n" +
            "• the desktop and Start menu icons\n" +
            "• the program's own settings and logs\n\n" +
            "Your backup drive and the files on it are NOT touched, and nothing of yours on this computer is deleted.\n\n" +
            "Remove BTS SimpleBackup?",
            AppPaths.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;

        var scheduleRemoved = Uninstaller.Run(_log);
        if (!scheduleRemoved && ScheduleHelper.Exists() && AskToClearOldSchedule()) scheduleRemoved = true;
        var scheduleNote = scheduleRemoved ? "" :
            "\n\nOne thing could not be removed: the weekly backup entry in Windows. It does no harm without the program, and Windows will simply fail to start it.";
        Info("BTS SimpleBackup has been removed. If you downloaded the program file, you can delete that download too." + scheduleNote + "\n\nIf an icon is still showing on your desktop or Start menu, it is only left over on screen and will disappear when you refresh the desktop (press F5) or restart your computer.\n\nThe program will now close.");
        Close();
    }

    /// <summary>Asks (in plain words) before using a Windows permission prompt to clear an older weekly schedule.</summary>
    private bool AskToClearOldSchedule()
    {
        var answer = MessageBox.Show(this,
            "An older weekly backup schedule on this computer is in the way, and Windows needs your permission to remove it.\n\n" +
            "Click Yes, then click Yes again when Windows asks for permission.\n\n" +
            "Nothing of yours is deleted.",
            AppPaths.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        return answer == DialogResult.Yes && ScheduleHelper.RemoveWithPermissionPrompt();
    }

    private async Task BackUpNowAsync()
    {
        // "Back up now" must not quietly change the weekly schedule; only the Save button does that.
        var (keepEnabled, keepDay, keepHour, keepMinute) = (_settings.ScheduleEnabled, _settings.Day, _settings.Hour, _settings.Minute);
        bool inputsOk = ReadControlsIntoSettings(requireDrive: true);
        _settings.ScheduleEnabled = keepEnabled;
        _settings.Day = keepDay;
        _settings.Hour = keepHour;
        _settings.Minute = keepMinute;
        if (!inputsOk) return;
        _settings.Save();

        SetBusy(true);
        _backupCts = new CancellationTokenSource();
        var token = _backupCts.Token;

        var result = await Task.Run(() => BackupRunner.Run(_settings, _log, token, p =>
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(() => ShowProgress(p));
        }));

        SetBusy(false);
        RefreshStatus();
        if (_closeWhenDone) { Close(); return; }   // the user asked to close while the backup was running
        if (result.Ok) Info(result.Message); else Warn(result.Message);
    }

    // ---------- helpers ----------

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _saveButton.Enabled = !busy;
        _nowButton.Enabled = !busy;
        _uninstallButton.Enabled = !busy;
        _driveCombo.Enabled = !busy;
        _folderList.Enabled = !busy;
        _progress.Visible = busy;
        _progressLabel.Visible = busy;
        _cancelButton.Visible = busy;
        _cancelButton.Enabled = busy;
        if (busy)
        {
            _copyStarted = false;
            _busyWatch.Restart();
            _progress.Style = ProgressBarStyle.Marquee;
            _progressLabel.Text = "Checking which files need to be backed up...";
        }
    }

    private void ShowProgress(ProgressInfo p)
    {
        if (p.FilesTotal <= 0 || p.BytesTotal <= 0)
        {
            _progress.Style = ProgressBarStyle.Marquee;
            _progressLabel.Text = "Checking which files need to be backed up...";
            return;
        }

        // Start the speed clock when copying really begins, so the checking phase doesn't make the estimate too pessimistic.
        if (!_copyStarted) { _copyStarted = true; _busyWatch.Restart(); }

        _progress.Style = ProgressBarStyle.Continuous;
        _progress.Maximum = 1000;
        _progress.Value = (int)Math.Clamp(1000.0 * p.BytesDone / p.BytesTotal, 0, 1000);

        var name = Path.GetFileName(p.File);
        var line1 = $"Copying file {Math.Min(p.FilesDone + 1, p.FilesTotal):N0} of {p.FilesTotal:N0}:  {name}  ({FileSize.Bytes(p.FileLength)})";
        if (p.OnlineOnly) line1 += "\nThis is an online-only OneDrive file, so it is downloaded first. That can take a while.";

        var line2 = $"{FileSize.Bytes(p.BytesDone)} of {FileSize.Bytes(p.BytesTotal)} done";
        var secs = _busyWatch.Elapsed.TotalSeconds;
        if (secs >= 10 && p.BytesDone > 0)
        {
            var speed = p.BytesDone / secs;
            line2 += $"   -   {FileSize.Bytes((long)speed)}/s   -   about {FormatTimeLeft((p.BytesTotal - p.BytesDone) / speed)} left";
        }
        _progressLabel.Text = line1 + "\n" + line2;
    }

    private static string FormatTimeLeft(double seconds)
    {
        if (seconds < 90) return $"{Math.Max(1, (int)seconds)} seconds";
        var minutes = seconds / 60;
        if (minutes < 90) return $"{(int)Math.Round(minutes)} minutes";
        var h = (int)(minutes / 60);
        return $"{h} h {(int)Math.Round(minutes - h * 60)} min";
    }

    private void RefreshDrives()
    {
        var drives = DriveHelper.GetCandidateDrives();
        _driveCombo.Items.Clear();
        foreach (var d in drives) _driveCombo.Items.Add(d);

        var saved = DriveHelper.FindSavedDrive(_settings);
        if (saved != null)
            _driveCombo.SelectedItem = drives.First(d => d.Root == saved.Root);
        else if (drives.Count == 1)
            _driveCombo.SelectedIndex = 0;

        UpdateDriveHint();
    }

    private void UpdateDriveHint()
    {
        if (_driveCombo.Items.Count == 0)
        {
            _driveHint.ForeColor = Color.Firebrick;
            _driveHint.Text = "No USB drive found. Plug one in, wait a few seconds, then click Refresh.";
        }
        else if (_driveCombo.SelectedItem is DriveEntry d)
        {
            bool tooSmall = _selectedBytes >= 0 && _selectedBytes > d.TotalBytes;
            _driveHint.ForeColor = tooSmall ? Color.Firebrick : SystemColors.GrayText;
            _driveHint.Text = tooSmall
                ? "The files you picked are bigger than this drive. Choose a bigger drive or tick fewer folders."
                : "Tip: a drive with at least twice the space your files use gives you room to grow.";
        }
        else
        {
            _driveHint.ForeColor = SystemColors.GrayText;
            _driveHint.Text = !string.IsNullOrWhiteSpace(_settings.DriveLabel)
                ? $"Your saved drive \"{_settings.DriveLabel}\" isn't plugged in. Plug it in and click Refresh."
                : "Choose the drive from the list.";
        }
    }

    private void RefreshStatus()
    {
        var status = Status.Load();
        if (status.LastSuccessLocal is { } last)
        {
            var age = DateTime.Now - last;
            _statusLabel.Text = $"Last backup: {last:dddd, MMMM d} at {last:h:mm tt}";
            _statusLabel.ForeColor = age.TotalDays > 10 ? Color.DarkOrange : Color.ForestGreen;
        }
        else
        {
            _statusLabel.Text = "No backup has been made yet.";
            _statusLabel.ForeColor = SystemColors.ControlText;
        }

        if (status.LastAttemptLocal is { } attempt && !status.LastAttemptOk && !string.IsNullOrEmpty(status.Message))
        {
            _statusLabel.Text += $"\nLatest attempt ({attempt:MMM d, h:mm tt}): {status.Message}";
            _statusLabel.ForeColor = Color.Firebrick;
        }

        if (_settings.ScheduleEnabled)
        {
            bool exists = ScheduleHelper.Exists();
            _scheduleLabel.Text = exists
                ? $"Automatic backup is ON: every {_settings.Day} at {DateTime.Today.AddHours(_settings.Hour).AddMinutes(_settings.Minute):h:mm tt}."
                : "Automatic backup is not active. Click \"Save and turn on weekly backup\".";
            _scheduleLabel.ForeColor = exists ? SystemColors.ControlText : Color.Firebrick;
        }
        else
        {
            _scheduleLabel.Text = "Automatic backup is OFF. Finish the steps below, then click Save.";
            _scheduleLabel.ForeColor = SystemColors.GrayText;
        }
    }

    private void UpdateSizeEstimate()
    {
        _sizeCts?.Cancel();
        var cts = _sizeCts = new CancellationTokenSource();
        var paths = _folderList.CheckedItems.Cast<FolderItem>().Select(f => f.Path).ToList();
        _selectedBytes = -1;

        if (paths.Count == 0) { _sizeLabel.Text = "Nothing is ticked."; return; }
        _sizeLabel.Text = "Working out how much space your files need...";

        Task.Run(() =>
        {
            long total = 0;
            var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
            foreach (var p in paths)
            {
                if (!Directory.Exists(p)) continue;
                // Same rules as the real backup: files only, and skip the cache folders it skips.
                var sizes = new System.IO.Enumeration.FileSystemEnumerable<long>(
                    p, (ref System.IO.Enumeration.FileSystemEntry e) => e.Length, opts)
                {
                    ShouldIncludePredicate = (ref System.IO.Enumeration.FileSystemEntry e) => !e.IsDirectory,
                    // Skip the same folders the backup skips: cache folders and real links (but not OneDrive folders).
                    ShouldRecursePredicate = (ref System.IO.Enumeration.FileSystemEntry e) =>
                        !BackupEngine.SkippedFolderNames.Contains(e.FileName.ToString()) &&
                        !((e.Attributes & FileAttributes.ReparsePoint) != 0 && e.ToFileSystemInfo().LinkTarget != null)
                };
                foreach (var length in sizes)
                {
                    // Stop quietly when a newer scan has replaced this one (no exception, so the debugger stays calm).
                    if (cts.IsCancellationRequested) return -1L;
                    total += length;
                }
            }
            return total;
        }).ContinueWith(t =>
        {
            if (!t.IsCompletedSuccessfully || cts.IsCancellationRequested || IsDisposed || !IsHandleCreated) return;
            BeginInvoke(() =>
            {
                _selectedBytes = t.Result;
                var text = $"These folders hold about {FileSize.Bytes(t.Result)}. Your first backup copies all of it; later ones copy only what changed.";
                if (t.Result >= 1L << 30)
                {
                    // Rough range only: about 100 MB/s for a fast external drive on a USB 3 port, about 4 MB/s for a basic flash drive.
                    var fast = FormatTimeLeft(t.Result / (100.0 * 1024 * 1024));
                    var slow = FormatTimeLeft(t.Result / (4.0 * 1024 * 1024));
                    text += $"\nThe first backup is the slow one: roughly {fast} on a fast external drive in a USB 3 port, or up to {slow} on a basic USB flash drive. You can keep using your computer meanwhile.";
                }
                _sizeLabel.Text = text;
                UpdateDriveHint();
            });
        }, TaskScheduler.Default);
    }

    private void AddFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = "Choose a folder to back up", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var path = dlg.SelectedPath;

        foreach (FolderItem existing in _folderList.Items)
        {
            if (string.Equals(existing.Path.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                Info("That folder is already in the list.");
                return;
            }
        }
        var name = Path.GetFileName(path.TrimEnd('\\'));
        _folderList.Items.Add(new FolderItem(string.IsNullOrEmpty(name) ? path : name, path, true), true);
        UpdateSizeEstimate();
    }

    private void RemoveSelectedFolder()
    {
        if (_folderList.SelectedItem is not FolderItem item) { Info("Click a folder in the list first."); return; }
        if (!item.Custom) { Info("The standard folders can't be removed. Just untick the ones you don't want."); return; }
        _folderList.Items.Remove(item);
        UpdateSizeEstimate();
    }

    private string DriveName()
        => string.IsNullOrWhiteSpace(_settings.DriveLabel) ? "your backup drive" : _settings.DriveLabel;

    private static void OpenInExplorer(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
        catch { }
    }

    private void Info(string text) => MessageBox.Show(this, text, AppPaths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
    private void Warn(string text) => MessageBox.Show(this, text, AppPaths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
}

namespace BTSSimpleBackup;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // Windows Task Scheduler starts the program this way. No window, just a small notification.
        if (args.Any(a => a.Equals("--scheduled", StringComparison.OrdinalIgnoreCase)))
        {
            Application.Run(new ScheduledRunContext());
            return 0;
        }

        using var mutex = new Mutex(true, @"Local\BTSSimpleBackup.UI", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show("BTS SimpleBackup is already open. Look for it on your taskbar.",
                AppPaths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        Application.Run(new MainForm());
        return 0;
    }
}

using System.Reflection;

namespace BTSSimpleBackup;

/// <summary>The Bishop Technical Services logo: window/taskbar icon and the picture shown in the window.</summary>
internal static class Branding
{
    // app.ico and logo.png are embedded in the program, so the single .exe carries its own logo.
    public static Icon AppIcon { get; } = LoadIcon();
    public static Image? Logo { get; } = LoadLogo();

    private static Stream? Open(string name) => Assembly.GetExecutingAssembly().GetManifestResourceStream(name);

    private static Icon LoadIcon()
    {
        try
        {
            using var s = Open("app.ico");
            if (s != null) return new Icon(s);
        }
        catch { }
        return SystemIcons.Application;
    }

    private static Image? LoadLogo()
    {
        try
        {
            using var s = Open("logo.png");
            if (s != null)
            {
                // Copy into memory so the stream can be closed.
                using var img = Image.FromStream(s);
                return new Bitmap(img);
            }
        }
        catch { }
        return null;
    }
}

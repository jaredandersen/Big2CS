using System.Reflection;
using System.Windows;

namespace Big2.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ForceLeftMenuDropAlignment();

        // A headless render dump, gated behind an environment variable, so one
        // launch produces the whole UI for review without a person at the
        // screen. Screen-grabbing a freshly launched window is unreliable: a
        // window that has not painted yet captures as blank white, which reads
        // exactly like a rendering bug and is not one.
        if (Environment.GetEnvironmentVariable("BIG2_DUMP") is { Length: > 0 } spec)
        {
            RenderDump.Run(spec);
            Shutdown();
            return;
        }

        if (Environment.GetEnvironmentVariable("BIG2_DUMP_DIALOGS") is { Length: > 0 } dir)
        {
            DialogDump.Run(dir);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        // Created explicitly rather than via StartupUri, because OnStartup is
        // already overridden for the dump path and a StartupUri would show a
        // window during it too.
        new MainWindow().Show();

        // Report the menu-alignment fix having FAILED, on stderr, where the
        // launch check will see it. Submenus dropping right-aligned is invisible
        // in a static render (a detached Popup measures against infinity, so a
        // captured image proves nothing) and easy to miss by eye on a menu this
        // small -- but it is one boolean, and the check already fails on any
        // stderr output.
        if (SystemParameters.MenuDropAlignment)
            Console.Error.WriteLine("MenuDropAlignment is still true: submenus will drop right-aligned");

        // Same trick for the icon. DialogSupport swallows a load failure so a
        // missing resource cannot take a dialog down -- which means a broken
        // pack URI would otherwise show up as nothing at all, on every window,
        // silently. One line on stderr turns that into a red launch check.
        if (DialogSupport.AppIcon is null)
            Console.Error.WriteLine("app icon failed to load: windows and dialogs will have no icon");
        if (DialogSupport.AppIconLarge is null)
            Console.Error.WriteLine("large app icon failed to load: the About box will have no icon");
        if (MainWindow?.Icon is null)
            Console.Error.WriteLine("main window has no icon");
    }

    /// <summary>
    /// WPF reads SPI_GETMENUDROPALIGNMENT through a long-standing interop bug
    /// (dotnet/wpf#5944) that reports true even on a US-locale LTR machine, so
    /// submenus drop right-aligned and hang left of their header. This has
    /// recurred on every WPF port in this series.
    ///
    /// Three details all matter: MenuItem's template reads
    /// IsMenuDropRightAligned rather than MenuDropAlignment, so both backing
    /// fields must be overwritten; each property fetches from Win32 on first
    /// read and only then marks itself cached, so the getter must be read BEFORE
    /// the field is written; and it must run before base.OnStartup, which
    /// creates the main window and realises the menu templates.
    ///
    /// Harmless now (there is no menu until Phase 4) and set here so it is not
    /// rediscovered then.
    /// </summary>
    private static void ForceLeftMenuDropAlignment()
    {
        try
        {
            _ = SystemParameters.MenuDropAlignment;
            SetField("_menuDropAlignment");

            var p = typeof(SystemParameters).GetProperty("IsMenuDropRightAligned",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
            if (p is not null) _ = p.GetValue(null);
            SetField("_isMenuDropRightAligned");
        }
        catch
        {
            // A cosmetic fix; never worth failing startup over.
        }

        static void SetField(string name)
        {
            var f = typeof(SystemParameters).GetField(name,
                BindingFlags.NonPublic | BindingFlags.Static);
            f?.SetValue(null, false);
        }
    }
}

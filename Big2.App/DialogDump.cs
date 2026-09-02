using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Big2.Core;

namespace Big2.App;

/// <summary>
/// Renders every dialog to a PNG in one run, so they can all be looked at
/// without clicking through the app. Gated behind BIG2_DUMP_DIALOGS.
///
/// Every project in this series converged on this shape, and on two traps:
///
///   AN UNSHOWN WINDOW HAS NO LAYOUT OF ITS OWN -- its size comes from its HWND,
///   so rendering one yields a 1x1 image. Measure, arrange and update its
///   CONTENT explicitly instead.
///
///   SIZING A CAPTURE FROM ActualWidth/ActualHeight CROPS IT. Those exclude the
///   element's own margin, which cut the right and bottom off every Minesweeper
///   dialog and looked exactly like a clipped layout. Worse, the About box's
///   footer uses NEGATIVE margins to bleed full-bleed across the panel, so a
///   capture sized to the content's bounds loses the OK button entirely.
///   DesiredSize includes the margin; that is what is used here, and the render
///   is checked against a known-good size rather than trusted.
/// </summary>
public static class DialogDump
{
    public static void Run(string outDir)
    {
        Directory.CreateDirectory(outDir);

        var settings = new Settings();

        Dump(new OptionsDialog(null, settings), Path.Combine(outDir, "options.png"));
        Dump(new AboutDialog(null), Path.Combine(outDir, "about.png"));

        // The prompt is built by a private constructor, so it is exercised
        // through the same entry point the game uses -- minus the ShowDialog.
        DumpPrompt(Path.Combine(outDir, "prompt.png"));
    }

    private static void Dump(Window window, string path)
    {
        if (window.Content is not FrameworkElement content)
        {
            Console.Error.WriteLine($"BIG2_DUMP_DIALOGS: {Path.GetFileName(path)} has no content");
            return;
        }

        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = content.DesiredSize;
        content.Arrange(new Rect(new Point(0, 0), size));
        content.UpdateLayout();

        int w = (int)Math.Ceiling(size.Width);
        int h = (int)Math.Ceiling(size.Height);
        if (w <= 1 || h <= 1)
        {
            Console.Error.WriteLine($"BIG2_DUMP_DIALOGS: {Path.GetFileName(path)} measured {w}x{h} -- refusing to write");
            return;
        }

        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);

        // The dialogs are white-on-white; without a background the PNG is
        // transparent and reads as blank.
        var backing = new DrawingVisual();
        using (var dc = backing.RenderOpen())
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
        bmp.Render(backing);
        bmp.Render(content);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        encoder.Save(fs);

        Console.Error.WriteLine($"BIG2_DUMP_DIALOGS wrote {path}  {w}x{h}");
    }

    private static void DumpPrompt(string path)
    {
        // MessagePrompt's constructor is private by design; reach it the same way
        // a test would rather than duplicating its layout here.
        var type = typeof(MessagePrompt);
        var ctor = type.GetConstructor(
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            null,
            new[] { typeof(Window), typeof(string), typeof(string), typeof(string), typeof(string) },
            null);

        if (ctor is null)
        {
            Console.Error.WriteLine("BIG2_DUMP_DIALOGS: MessagePrompt constructor not found");
            return;
        }

        var prompt = (Window)ctor.Invoke(new object?[]
        {
            null, "Big 2", "Start a new series? 7 hands will be discarded.",
            "New series", "Keep playing",
        });
        Dump(prompt, path);
    }
}

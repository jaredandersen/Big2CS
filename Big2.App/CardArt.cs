using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Big2.Core;

namespace Big2.App;

/// <summary>
/// Loads the card bitmaps and hands them out as WPF bitmaps with the corner
/// notch already baked in.
///
/// Nothing here re-colours or re-samples the art. The deck is drawn at native
/// 71x96 with a real black border and pure ink, so the decode goes straight to
/// Bgra32 and the only edit is the corner cut.
/// </summary>
public static class CardArt
{
    /// <summary>The only back this game draws.</summary>
    public const int BackBitmapId = 54;

    private static readonly Dictionary<string, BitmapSource> Cache = new();

    /// <summary>Face for a card id 0..51.</summary>
    public static BitmapSource Face(int cardId) => Load($"Cards/card_{Cards.BitmapIdOf(cardId)}");

    /// <summary>The card back, drawn for every opponent's hand.</summary>
    public static BitmapSource Back() => Load($"Backs/back_{BackBitmapId}");

    private static BitmapSource Load(string relativePath)
    {
        if (Cache.TryGetValue(relativePath, out var cached)) return cached;

        var uri = new Uri($"pack://application:,,,/Assets/{relativePath}.bmp");
        var decoder = BitmapDecoder.Create(uri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);

        int w = converted.PixelWidth, h = converted.PixelHeight;
        int stride = w * 4;
        var pixels = new byte[stride * h];
        converted.CopyPixels(pixels, stride, 0);

        CornerCut.Apply(pixels, w, h, stride);

        // Every pixel is now either fully opaque with its RGB unchanged, or
        // fully transparent with RGB zeroed -- already valid premultiplied data.
        var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
        bmp.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
        bmp.Freeze();

        Cache[relativePath] = bmp;
        return bmp;
    }
}

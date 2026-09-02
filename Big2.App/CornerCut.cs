namespace Big2.App;

/// <summary>
/// Cuts the transparent staircase notch into the four corners of a Bgra32
/// pixel buffer, in place, so the green table shows through the rounded corner.
///
/// Confirmed against the deck's own art: (0,0), (1,0) and (0,1) are the cut
/// pixels, while (1,1), (2,0) and (0,2) are genuine border. Size 2 reproduces
/// exactly that, and the deck is drawn to match.
///
/// The alpha is baked into the bitmap's own pixels rather than applied as a
/// Clip or an OpacityMask. Both of those were tried and both still produced
/// real RGB blending at the edge regardless of RenderOptions, because
/// WPF anti-aliases the compositing itself.
/// </summary>
public static class CornerCut
{
    public const int Size = 2;

    public static void Apply(byte[] pixels, int w, int h, int stride)
    {
        CutCorner(pixels, w, h, stride, right: false, bottom: false);
        CutCorner(pixels, w, h, stride, right: true, bottom: false);
        CutCorner(pixels, w, h, stride, right: false, bottom: true);
        CutCorner(pixels, w, h, stride, right: true, bottom: true);
    }

    private static void CutCorner(byte[] pixels, int w, int h, int stride, bool right, bool bottom)
    {
        for (int i = 0; i < Size; i++)
        {
            int y = bottom ? h - 1 - i : i;
            int cutWidth = Size - i;
            for (int j = 0; j < cutWidth; j++)
            {
                int x = right ? w - 1 - j : j;
                int idx = y * stride + x * 4;
                pixels[idx] = pixels[idx + 1] = pixels[idx + 2] = pixels[idx + 3] = 0;
            }
        }
    }
}

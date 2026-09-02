namespace Big2.Core;

public readonly record struct LayoutPoint(int X, int Y);

public readonly record struct LayoutRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;

    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;

    public bool IntersectsWith(LayoutRect o) =>
        Left < o.Right && Right > o.Left && Top < o.Bottom && Bottom > o.Top;

    public LayoutRect Inflate(int dx, int dy) =>
        new(Left - dx, Top - dy, Width + 2 * dx, Height + 2 * dy);
}

/// <summary>Which way a seat's hand fans.</summary>
public enum FanDirection { Horizontal, Vertical }

/// <summary>Everything positional for one seat, in table-relative device pixels.</summary>
public sealed record SeatLayout
{
    public required int Seat { get; init; }
    public required FanDirection Direction { get; init; }

    /// <summary>Top-left of card 0.</summary>
    public required LayoutPoint Origin { get; init; }

    /// <summary>Per-card step. Exactly one of these is non-zero.</summary>
    public required int Dx { get; init; }
    public required int Dy { get; init; }

    /// <summary>Where this seat's played cards sit in the middle.</summary>
    public required LayoutPoint PlayLoc { get; init; }

    /// <summary>Baseline for the seat's name and card count.</summary>
    public required LayoutPoint LabelLoc { get; init; }

    public LayoutPoint SlotOrigin(int slot) => new(Origin.X + Dx * slot, Origin.Y + Dy * slot);
}

/// <summary>A fully resolved table at one particular size.</summary>
public sealed record TableGeometry
{
    public required double Scale { get; init; }
    public required int TableWidth { get; init; }
    public required int TableHeight { get; init; }

    public required int CardWidth { get; init; }
    public required int CardHeight { get; init; }
    public required int FanSpacing { get; init; }
    public required int SideFanSpacing { get; init; }
    public required int CentreSpacing { get; init; }
    public required int PopSpacing { get; init; }
    public required int TextHeight { get; init; }

    public required LayoutRect CentreRect { get; init; }

    /// <summary>Where a refusal message is drawn, between the hand and the buttons.</summary>
    public required LayoutRect MessageRow { get; init; }
    public required IReadOnlyList<LayoutRect> Buttons { get; init; }
    public required IReadOnlyList<SeatLayout> Seats { get; init; }

    public SeatLayout this[int seat] => Seats[seat];

    /// <summary>
    /// Rectangle covering <paramref name="count"/> cards fanned from a seat's
    /// origin. This is the BARE fan -- no room for the selection lift.
    /// </summary>
    public LayoutRect FanRect(int seat, int count)
    {
        var s = Seats[seat];
        if (count <= 0) return new LayoutRect(s.Origin.X, s.Origin.Y, 0, 0);
        int w = s.Direction == FanDirection.Horizontal ? s.Dx * (count - 1) + CardWidth : CardWidth;
        int h = s.Direction == FanDirection.Vertical ? s.Dy * (count - 1) + CardHeight : CardHeight;
        return new LayoutRect(s.Origin.X, s.Origin.Y, w, h);
    }

    /// <summary>
    /// The bottom hand's fan including room for the selection lift.
    ///
    /// Big 2 selects cards WHILE a play is on the table, so this rect genuinely
    /// has to clear the centre pile, and <see cref="TableLayout.FitsCleanly"/>
    /// checks that it does.
    /// </summary>
    public LayoutRect BottomCoverRect(int count)
    {
        var r = FanRect(0, count);
        return new LayoutRect(r.Left, r.Top - PopSpacing, r.Width, r.Height + PopSpacing);
    }

    /// <summary>Width of a play of <paramref name="total"/> cards in the centre.</summary>
    public int PlayWidth(int total) => CentreSpacing * (Math.Max(total, 1) - 1) + CardWidth;

    /// <summary>
    /// Where the nth card of <paramref name="seat"/>'s play sits in the centre.
    ///
    /// North and South are horizontally centred in their half; West and East are
    /// pinned to the centre's left and right edges and vertically centred on the
    /// seam between the two halves, which is where there is room beside them.
    /// </summary>
    public LayoutPoint PlaySlot(int seat, int index, int total)
    {
        var p = Seats[seat].PlayLoc;
        int w = PlayWidth(total);

        int left = seat switch
        {
            TableLayout.SeatWest => p.X,
            TableLayout.SeatEast => p.X - w,
            _ => p.X - w / 2,
        };
        return new LayoutPoint(left + CentreSpacing * index, p.Y);
    }

    /// <summary>Bounding box of a seat's play in the centre.</summary>
    public LayoutRect PlayRect(int seat, int total)
    {
        var first = PlaySlot(seat, 0, total);
        return new LayoutRect(first.X, first.Y, PlayWidth(total), CardHeight);
    }
}

/// <summary>
/// Turns a table rectangle into card sizes and seat positions.
///
/// Seat order is play order: 0 = South (the human, bottom), 1 = East (right),
/// 2 = North (top), 3 = West (left). That matches <see cref="Big2Game"/>'s
/// (seat + 1) % 4 and the counter-clockwise rotation recovered from the DOS log,
/// where the cycle is N -> W -> S -> E.
///
/// All arithmetic is integer device pixels. Keeping the geometry integral avoids
/// the epsilon problem that fractional layout otherwise forces onto every
/// adjacency test.
/// </summary>
public static class TableLayout
{
    public const int SeatSouth = 0;
    public const int SeatEast = 1;
    public const int SeatNorth = 2;
    public const int SeatWest = 3;

    // Native art and spacing. The card size is the deck's native resolution
    // and is not a choice; the rest is.
    public const int NativeCardWidth = 71;
    public const int NativeCardHeight = 96;
    public const int NativeFanSpacing = 15;
    public const int NativeSideFanSpacing = 15;
    public const int NativeCentreSpacing = 20;
    public const int NativePopSpacing = 20;
    public const int NativeEdge = 8;
    public const int NativeGap = 16;
    public const int NativeButtonHeight = 26;
    public const int NativeButtonWidth = 76;
    public const int NativeTextHeight = 16;

    /// <summary>
    /// The native table, DERIVED from the constants above rather than written
    /// down as a magic number, so the two cannot drift apart.
    ///
    /// This is a DESIGN CHOICE, not a measurement. There is no original Big 2
    /// to be faithful to, so this project picks a table at which the artwork is
    /// 1:1 and grows from there. It is a chosen size, not a measured one.
    ///
    /// Width  = edge + card + gap + max(fan, centre) + gap + card + edge
    /// Height = edge + card + text + gap + 2*card + gap + text + pop + card
    ///          + message + button + edge
    ///
    /// The centre is TWO cards tall because it shows ALL FOUR seats' plays in the
    /// current trick, not just the last one -- you cannot judge a play without
    /// seeing what you are beating. North's play sits in the upper half, South's
    /// in the lower, and West's and East's are vertically centred on the seam
    /// where there is horizontal room beside them.
    ///
    /// The two text rows are load-bearing, not padding: North's label sits under
    /// its fan and South's above its own, and without reserved rows they land on
    /// top of the centre pile. Deriving the table from the same constants the
    /// layout uses is what keeps that honest.
    /// </summary>
    /// <summary>
    /// Widest a single play can be: five cards fanned at the centre spacing.
    /// </summary>
    public static int NativeMaxPlayWidth { get; } =
        FanExtent(5, NativeCentreSpacing, NativeCardWidth);

    /// <summary>
    /// The centre has to hold THREE five-card plays across -- West, then North's
    /// or South's centred column, then East -- with a gap between them. That is
    /// what showing the whole trick costs, and it is why the table is wider than
    /// the 13-card fan alone would need.
    /// </summary>
    public static int NativeCentreWidth { get; } =
        3 * NativeMaxPlayWidth + 2 * NativeGap;

    public static int NativeTableWidth { get; } =
        NativeEdge + NativeCardWidth + NativeGap +
        Math.Max(FanExtent(Dealer.HandSize, NativeFanSpacing, NativeCardWidth),
                 NativeCentreWidth) +
        NativeGap + NativeCardWidth + NativeEdge;

    public static int NativeTableHeight { get; } =
        NativeEdge + NativeCardHeight + NativeTextHeight + NativeGap +
        2 * NativeCardHeight + NativeGap + NativeTextHeight + NativePopSpacing +
        NativeCardHeight + NativeGap + NativeTextHeight + NativeGap +
        NativeButtonHeight + NativeEdge;

    /// <summary>
    /// Never below 1:1. Under one screen pixel per source pixel the renderer is
    /// deleting rows and columns of the artwork outright -- a damaged picture,
    /// not a smaller one.
    /// </summary>
    public const double MinScale = 1.0;

    private static int FanExtent(int count, int spacing, int cardExtent) =>
        spacing * (count - 1) + cardExtent;

    private static int S(double scale, int native) => (int)Math.Round(native * scale);

    /// <summary>
    /// Largest scale whose INTEGER layout still fits, floored at 1:1.
    ///
    /// It searches; it does not divide. The native size is where the pieces
    /// exactly touch, so the raw ratio leaves zero clearance in continuous
    /// arithmetic and rounding each constant to whole pixels then tips it into a
    /// genuine 1px overlap. Take the ratio as an upper bound and binary search
    /// downward, with the rounding INSIDE the fit test. Clearance is monotonic
    /// in scale, so the search is well defined.
    /// </summary>
    public static double ScaleFor(int tableWidth, int tableHeight)
    {
        double hi = Math.Min(tableWidth / (double)NativeTableWidth,
                             tableHeight / (double)NativeTableHeight);

        if (hi <= MinScale) return MinScale;
        if (FitsCleanly(tableWidth, tableHeight, hi)) return hi;

        double lo = MinScale;
        for (int i = 0; i < 40; i++)
        {
            double mid = (lo + hi) / 2;
            if (FitsCleanly(tableWidth, tableHeight, mid)) lo = mid; else hi = mid;
        }
        return lo;
    }

    /// <summary>
    /// Whether the integer layout at <paramref name="scale"/> has every piece
    /// inside the table and nothing overlapping that should not.
    /// </summary>
    public static bool FitsCleanly(int tableWidth, int tableHeight, double scale)
    {
        if (scale < MinScale) return false;
        var g = Build(tableWidth, tableHeight, scale);

        // Everything inside the table.
        foreach (int seat in new[] { SeatSouth, SeatEast, SeatNorth, SeatWest })
        {
            var fan = g.FanRect(seat, Dealer.HandSize);
            if (fan.Left < 0 || fan.Top < 0 || fan.Right > tableWidth || fan.Bottom > tableHeight)
                return false;
        }
        if (g.CentreRect.Left < 0 || g.CentreRect.Top < 0 ||
            g.CentreRect.Right > tableWidth || g.CentreRect.Bottom > tableHeight)
            return false;

        // The centre must actually hold a card. Every constant is rounded
        // independently, so at a fractional scale the derived centre height can
        // land one pixel short of the card it has to contain -- which is exactly
        // the class of bug that makes rounding belong INSIDE the fit test rather
        // than after it.
        if (g.CentreRect.Height < 2 * g.CardHeight) return false;
        if (g.CentreRect.Width < 3 * g.PlayWidth(5) + 2 * S(scale, NativeGap)) return false;

        // No two seats' plays may collide, in the worst case of five cards each.
        // Showing the whole trick is worthless if the plays sit on top of each
        // other.
        for (int a = 0; a < Dealer.Seats; a++)
            for (int b = a + 1; b < Dealer.Seats; b++)
                if (g.PlayRect(a, 5).IntersectsWith(g.PlayRect(b, 5))) return false;
        foreach (var b in g.Buttons)
            if (b.Left < 0 || b.Right > tableWidth || b.Bottom > tableHeight) return false;

        // The centre pile must clear every hand, and the bottom hand's clearance
        // must hold with its cards LIFTED, because selection happens while a
        // play is on the table.
        if (g.CentreRect.IntersectsWith(g.BottomCoverRect(Dealer.HandSize))) return false;
        foreach (int seat in new[] { SeatEast, SeatNorth, SeatWest })
            if (g.CentreRect.IntersectsWith(g.FanRect(seat, Dealer.HandSize))) return false;

        // The buttons must clear the bottom hand, lifted included.
        foreach (var b in g.Buttons)
            if (b.IntersectsWith(g.BottomCoverRect(Dealer.HandSize))) return false;

        // Side hands must clear the top and bottom fans.
        foreach (int side in new[] { SeatEast, SeatWest })
        {
            var s = g.FanRect(side, Dealer.HandSize);
            if (s.IntersectsWith(g.FanRect(SeatNorth, Dealer.HandSize))) return false;
            if (s.IntersectsWith(g.BottomCoverRect(Dealer.HandSize))) return false;
        }
        return true;
    }

    /// <summary>Resolves the whole table at a given size and scale.</summary>
    public static TableGeometry Build(int tableWidth, int tableHeight, double scale)
    {
        int cw = S(scale, NativeCardWidth);
        int ch = S(scale, NativeCardHeight);
        int fan = S(scale, NativeFanSpacing);
        int sideFan = S(scale, NativeSideFanSpacing);
        int centreSpacing = S(scale, NativeCentreSpacing);
        int pop = S(scale, NativePopSpacing);
        int edge = S(scale, NativeEdge);
        int gap = S(scale, NativeGap);
        int btnH = S(scale, NativeButtonHeight);
        int btnW = S(scale, NativeButtonWidth);
        int textH = S(scale, NativeTextHeight);

        int handSpan = FanExtent(Dealer.HandSize, fan, cw);
        int sideSpan = FanExtent(Dealer.HandSize, sideFan, ch);

        int centreSpan = Math.Max(handSpan, 3 * (centreSpacing * 4 + cw) + 2 * gap);
        int centreX = (tableWidth - centreSpan) / 2;
        int handX = (tableWidth - handSpan) / 2;
        int buttonsTop = tableHeight - edge - btnH;

        // A row for the "why you cannot play that" message, between the hand and
        // the buttons. It gets its own space rather than being squeezed into the
        // gap: a message that overlaps the cards it is about is worse than a beep.
        int messageTop = buttonsTop - gap - textH;
        int bottomTop = messageTop - gap - ch;
        int topTop = edge;

        var seats = new SeatLayout[Dealer.Seats];

        seats[SeatSouth] = new SeatLayout
        {
            Seat = SeatSouth,
            Direction = FanDirection.Horizontal,
            Origin = new LayoutPoint(handX, bottomTop),
            Dx = fan,
            Dy = 0,
            PlayLoc = new LayoutPoint(0, 0),      // filled below, once CentreRect exists
            // At the fan's own left edge, NOT at the table edge -- the table
            // edge is where the West hand lives, and West is drawn after North,
            // so an edge-anchored label is painted over and simply vanishes.
            LabelLoc = new LayoutPoint(handX, bottomTop - pop - textH),
        };

        seats[SeatNorth] = new SeatLayout
        {
            Seat = SeatNorth,
            Direction = FanDirection.Horizontal,
            Origin = new LayoutPoint(handX, topTop),
            Dx = fan,
            Dy = 0,
            PlayLoc = new LayoutPoint(0, 0),
            LabelLoc = new LayoutPoint(handX, topTop + ch),
        };

        int sideTop = (tableHeight - sideSpan) / 2;

        seats[SeatWest] = new SeatLayout
        {
            Seat = SeatWest,
            Direction = FanDirection.Vertical,
            Origin = new LayoutPoint(edge, sideTop),
            Dx = 0,
            Dy = sideFan,
            PlayLoc = new LayoutPoint(0, 0),
            LabelLoc = new LayoutPoint(edge, sideTop - textH - 2),
        };

        seats[SeatEast] = new SeatLayout
        {
            Seat = SeatEast,
            Direction = FanDirection.Vertical,
            Origin = new LayoutPoint(tableWidth - edge - cw, sideTop),
            Dx = 0,
            Dy = sideFan,
            PlayLoc = new LayoutPoint(0, 0),
            LabelLoc = new LayoutPoint(tableWidth - edge - cw, sideTop - textH - 2),
        };

        // The centre sits between the top fan and the lifted bottom fan.
        int centreTop = topTop + ch + textH + gap;
        int centreBottom = bottomTop - pop - textH - gap;
        var centre = new LayoutRect(centreX, centreTop, centreSpan, Math.Max(0, centreBottom - centreTop));

        // Each seat's play gets its own band of the centre, positioned toward
        // that seat so "who played what" is spatial rather than only labelled.
        // PlayLoc is the top-left of the seat's play area; the play itself is
        // centred within it by PlayOrigin.
        int midX = centre.Left + centre.Width / 2;
        int midY = centre.Top + centre.Height / 2;

        seats[SeatNorth] = seats[SeatNorth] with { PlayLoc = new LayoutPoint(midX, centre.Top) };
        seats[SeatSouth] = seats[SeatSouth] with { PlayLoc = new LayoutPoint(midX, centre.Bottom - ch) };
        seats[SeatWest] = seats[SeatWest] with { PlayLoc = new LayoutPoint(centre.Left, midY - ch / 2) };
        seats[SeatEast] = seats[SeatEast] with { PlayLoc = new LayoutPoint(centre.Right, midY - ch / 2) };

        int buttonGap = S(scale, 8);
        var buttons = new LayoutRect[3];
        int totalButtons = btnW * 3 + buttonGap * 2;
        int bx = (tableWidth - totalButtons) / 2;
        for (int i = 0; i < 3; i++)
            buttons[i] = new LayoutRect(bx + i * (btnW + buttonGap), buttonsTop, btnW, btnH);

        return new TableGeometry
        {
            MessageRow = new LayoutRect(edge, messageTop, tableWidth - 2 * edge, textH),
            Scale = scale,
            TableWidth = tableWidth,
            TableHeight = tableHeight,
            CardWidth = cw,
            CardHeight = ch,
            FanSpacing = fan,
            SideFanSpacing = sideFan,
            CentreSpacing = centreSpacing,
            PopSpacing = pop,
            TextHeight = textH,
            CentreRect = centre,
            Buttons = buttons,
            Seats = seats,
        };
    }

    /// <summary>Resolves the table at the largest scale that fits.</summary>
    public static TableGeometry For(int tableWidth, int tableHeight) =>
        Build(tableWidth, tableHeight, ScaleFor(tableWidth, tableHeight));
}

/// <summary>The three buttons under the bottom hand.</summary>
public enum TableButton { Play = 0, Pass = 1, Sort = 2 }

/// <summary>What a click landed on.</summary>
public readonly record struct HitResult(bool IsCard, int CardSlot, TableButton? Button)
{
    public static readonly HitResult None = new(false, -1, null);
    public static HitResult Card(int slot) => new(true, slot, null);
    public static HitResult OnButton(TableButton b) => new(false, -1, b);
}

/// <summary>
/// Which card or button a point lands on. Headless because that is the only way
/// it becomes testable.
/// </summary>
public static class HitTest
{
    /// <summary>
    /// Hit-tests the bottom hand and the button row.
    /// <paramref name="lifted"/> is the set of selected slots, which sit
    /// <see cref="TableGeometry.PopSpacing"/> higher.
    /// </summary>
    public static HitResult At(TableGeometry g, int handCount, IReadOnlySet<int> lifted, int x, int y)
    {
        for (int i = 0; i < g.Buttons.Count; i++)
            if (g.Buttons[i].Contains(x, y)) return HitResult.OnButton((TableButton)i);

        // Later cards overlap earlier ones, so scan from the top of the z-order
        // down -- the rightmost card wins where two overlap.
        var seat = g[TableLayout.SeatSouth];
        for (int slot = handCount - 1; slot >= 0; slot--)
        {
            var o = seat.SlotOrigin(slot);
            int top = o.Y - (lifted.Contains(slot) ? g.PopSpacing : 0);
            var r = new LayoutRect(o.X, top, g.CardWidth, g.CardHeight);
            if (r.Contains(x, y)) return HitResult.Card(slot);
        }
        return HitResult.None;
    }
}

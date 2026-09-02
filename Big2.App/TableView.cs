using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Big2.Core;

namespace Big2.App;

/// <summary>
/// Draws the table. Everything positional comes from <see cref="TableLayout"/>
/// in the headless core -- this class only paints.
///
/// All arithmetic is in DEVICE pixels, not DIPs. A 71px card computed in DIPs is
/// 106 physical pixels at 150% scaling, so the framework would resample artwork
/// that had just been scaled exactly right; the symptom is soft output with
/// every setting apparently correct. The view therefore measures its own DPI and
/// applies one inverse transform at the root.
/// </summary>
public sealed class TableView : FrameworkElement
{
    /// <summary>
    /// The table felt, <c>RGB(0, 127, 0)</c> -- the classic card-table green.
    ///
    /// It is NOT derived from anything here: there is no original Big 2 binary,
    /// so nothing in this game measures it. It is inherited on purpose, because
    /// it is the one constant the family actually shares -- in the ports it comes
    /// from <c>CreateSolidBrush(0x7F00)</c>, and a COLORREF is 0x00BBGGRR, so it
    /// is #007F00 and not the #008000 it is easy to assume.
    ///
    /// This shipped briefly as #005A28, a darker green chosen freely on the
    /// grounds that nothing anchored it. Nothing did; matching the siblings was
    /// a house-consistency decision, not a measurement, and this comment says so
    /// rather than letting a later reader infer a fidelity that was never here.
    /// </summary>
    public static readonly Color TableColor = Color.FromRgb(0x00, 0x7F, 0x00);

    private static readonly Brush TableBrush = Freeze(new SolidColorBrush(TableColor));
    private static readonly Brush TextBrush = Freeze(new SolidColorBrush(Colors.White));
    private static readonly Brush DimTextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xB0, 0xC8, 0xB8)));
    private static readonly Brush ButtonFace = Freeze(new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)));
    private static readonly Brush ButtonDisabled = Freeze(new SolidColorBrush(Color.FromRgb(0x70, 0x86, 0x78)));
    private static readonly Brush ButtonText = Freeze(new SolidColorBrush(Colors.Black));
    private static readonly Brush LeadTextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0xC8, 0x70)));
    private static readonly Brush MessageBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF0, 0xC8, 0x60)));
    private static readonly Brush OverlayBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x06, 0x33, 0x18)));
    private static readonly Pen OverlayPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xC8, 0xA0, 0x3C)), 2));
    private static readonly Pen TurnPen = Freeze(new Pen(new SolidColorBrush(Colors.White), 2));

    /// <summary>
    /// Behind the play that currently holds the trick. Amber rather than a
    /// lighter green: against a green table a green halo reads as a shadow, and
    /// "what do I have to beat" has to be answerable at a glance among four
    /// plays.
    /// </summary>
    private static readonly Brush LeadBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xC8, 0xA0, 0x3C)));

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    private static readonly Typeface Face =
        new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public TableView()
    {
        // Both of these are opt-outs from defaults tuned to make NEW content
        // look good, and both are invisible until specifically checked.
        //
        // NearestNeighbor: without it WPF bilinearly interpolates the card art.
        // Measured before this line existed: a 3-colour source bitmap rendered
        // as 94 distinct colours at 3x and 255 at a fractional scale, every one
        // of them an intermediate grey.
        //
        // Aliased: edge antialiasing puts a halo of intermediate colours around
        // every sprite edge. The originals drew hard GDI edges and have no such
        // colours anywhere.
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
    }

    /// <summary>What the view needs to draw one frame. Set by MainWindow, read here.</summary>
    public Big2Game? Game { get; set; }
    public IReadOnlySet<int> Selected { get; set; } = new HashSet<int>();
    public string[] SeatNames { get; set; } = { "You", "East", "North", "West" };

    /// <summary>
    /// Button labels and enablement, owned by the window rather than baked in
    /// here: the same three slots read Play/Pass/Sort during a hand and
    /// Next Hand/Finish once one is over.
    /// </summary>
    public string[] ButtonLabels { get; set; } = { "Play", "Pass", "Sort" };
    public bool[] ButtonEnabled { get; set; } = { false, false, true };

    /// <summary>
    /// A panel over the table. Non-null suspends play: a hand does not roll into
    /// the next one on a timer, it waits to be acknowledged.
    /// </summary>
    /// <summary>
    /// Cards currently in flight. They are drawn LAST, over everything, and are
    /// suppressed wherever they came from so nothing appears twice.
    /// </summary>
    public IReadOnlyList<MovingCard> Moving { get; set; } = Array.Empty<MovingCard>();

    /// <summary>
    /// Why the last attempt could not be played. Shown until the selection
    /// changes -- a beep says only THAT something is wrong, and these rules are
    /// exactly the part a new player is still learning.
    /// </summary>
    public string? Message { get; set; }

    public string? OverlayTitle { get; set; }
    public IReadOnlyList<ScoreBoard.Row> OverlayRows { get; set; } = Array.Empty<ScoreBoard.Row>();

    /// <summary>Resolved geometry for the current size. Null before the first layout.</summary>
    public TableGeometry? Geometry { get; private set; }

    /// <summary>Device pixels per DIP. Measured, never assumed to be 1.</summary>
    private double _dpiScale = 1.0;

    /// <summary>
    /// Pins the DPI scale, so one DIP is one device pixel. Used ONLY by
    /// RenderDump: the harness asks for a size in device pixels, and without
    /// this the view lays out for size * displayScale and the result is squeezed
    /// back down into the requested bitmap. That is a real bug this project hit
    /// -- a 900x800 request produced scale 3.015 instead of 2.01 on a 150%
    /// display, and the giveaway was the ratio.
    /// </summary>
    public bool PinDpiToOne { get; set; }

    /// <summary>Table size in DEVICE pixels.</summary>
    public int DeviceWidth => (int)Math.Round(ActualWidth * _dpiScale);
    public int DeviceHeight => (int)Math.Round(ActualHeight * _dpiScale);

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        Rebuild();
    }

    protected override void OnDpiChanged(DpiScale old, DpiScale now)
    {
        base.OnDpiChanged(old, now);
        if (!PinDpiToOne) _dpiScale = now.DpiScaleX;
        Rebuild();
    }

    public void Rebuild()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        _dpiScale = PinDpiToOne ? 1.0 : VisualTreeHelper.GetDpi(this).DpiScaleX;
        Geometry = TableLayout.For(DeviceWidth, DeviceHeight);
        InvalidateVisual();
    }

    /// <summary>Converts a mouse position (DIPs) into table device pixels.</summary>
    public (int X, int Y) ToDevice(Point p) =>
        ((int)Math.Round(p.X * _dpiScale), (int)Math.Round(p.Y * _dpiScale));

    protected override void OnRender(DrawingContext dc)
    {
        if (Geometry is null) Rebuild();
        var g = Geometry;
        if (g is null) return;

        // One inverse transform at the root, so everything below is drawn in
        // whole device pixels.
        double inv = 1.0 / _dpiScale;
        dc.PushTransform(new ScaleTransform(inv, inv));

        dc.DrawRectangle(TableBrush, null, new Rect(0, 0, g.TableWidth, g.TableHeight));

        var game = Game;
        if (game is not null)
        {
            DrawOpponent(dc, g, game, TableLayout.SeatNorth);
            DrawOpponent(dc, g, game, TableLayout.SeatEast);
            DrawOpponent(dc, g, game, TableLayout.SeatWest);
            DrawCentre(dc, g, game);
            DrawBottomHand(dc, g, game);
            DrawButtons(dc, g);
            DrawMessage(dc, g);
            DrawMoving(dc, g);
        }

        if (OverlayTitle is not null) DrawOverlay(dc, g);

        dc.Pop();
    }

    private void DrawOpponent(DrawingContext dc, TableGeometry g, Big2Game game, int seat)
    {
        var s = g[seat];
        var back = CardArt.Back();

        // While a card is flying out of this hand it is still counted in the
        // seat's card total (the play has already been applied), so the fan is
        // drawn one longer and the flying slot left empty. Without that the hand
        // visibly jumps a card shorter before the card arrives anywhere.
        int flying = Moving.Count(m => m.Seat == seat);
        int count = game.CardsLeft(seat) + flying;

        for (int i = 0; i < count; i++)
        {
            if (Moving.Any(m => m.Seat == seat && m.Slot == i)) continue;
            var o = s.SlotOrigin(i);
            dc.DrawImage(back, new Rect(o.X, o.Y, g.CardWidth, g.CardHeight));
        }

        DrawLabel(dc, g, seat, game, s.LabelLoc);
    }

    private void DrawBottomHand(DrawingContext dc, TableGeometry g, Big2Game game)
    {
        var s = g[TableLayout.SeatSouth];
        var hand = game.Hand(TableLayout.SeatSouth);

        for (int i = 0; i < hand.Count; i++)
        {
            if (Moving.Any(m => m.Seat == TableLayout.SeatSouth && m.Slot == i)) continue;
            var o = s.SlotOrigin(i);
            int y = o.Y - (Selected.Contains(i) ? g.PopSpacing : 0);
            dc.DrawImage(CardArt.Face(hand[i]), new Rect(o.X, y, g.CardWidth, g.CardHeight));
        }

        DrawLabel(dc, g, TableLayout.SeatSouth, game, s.LabelLoc);
    }

    private void DrawLabel(DrawingContext dc, TableGeometry g, int seat, Big2Game game, LayoutPoint at)
    {
        string text = $"{SeatNames[seat]}  ({game.CardsLeft(seat)})";

        var ft = Text(text, g.TextHeight, seat == game.Turn ? TextBrush : DimTextBrush);
        dc.DrawText(ft, new Point(at.X, at.Y));

        // The seat to play gets a rule under its label, so whose turn it is is
        // readable without colour alone.
        if (seat == game.Turn)
            dc.DrawLine(TurnPen,
                new Point(at.X, at.Y + ft.Height + 1),
                new Point(at.X + ft.Width, at.Y + ft.Height + 1));
    }

    /// <summary>
    /// Draws EVERY seat's play in the current trick, not only the last one. You
    /// cannot judge a play without seeing what you are beating, and with only the
    /// most recent play visible the round is unreadable by the time it reaches
    /// you.
    ///
    /// The play that currently holds the trick is outlined, so "what do I have to
    /// beat" is answerable at a glance among four plays.
    ///
    /// The plays are NOT labelled with their seat's name. Each sits toward the
    /// seat that made it -- North above, South below, West and East on the flanks
    /// -- and every seat is already named over its own hand, so a second copy of
    /// the name is redundant. Only the lead play carries a mark, and it is a word
    /// rather than the halo alone so the cue does not depend on colour.
    /// </summary>
    private void DrawCentre(DrawingContext dc, TableGeometry g, Big2Game game)
    {
        // During a sweep the centre's cards are in flight, so the pile itself is
        // not drawn at all -- the moving copies ARE the pile.
        bool sweeping = Moving.Any(m => m.FromCentre);

        for (int seat = 0; seat < Dealer.Seats; seat++)
        {
            // A play is applied to the game BEFORE its animation starts, so
            // while cards are flying toward the centre the seat's play is
            // already recorded there. Drawing it would show the card twice --
            // once landed, once mid-flight.
            bool arriving = Moving.Any(m => m.Seat == seat && !m.FromCentre);

            var cards = sweeping || arriving ? Array.Empty<int>() : game.TrickPlay(seat);
            bool passed = !sweeping && !arriving && game.HasPassed(seat);

            // A seat that passed shows "pass" where its cards would be, whoever
            // it is -- the point is to read the whole round at a glance, and an
            // opponent's pass is as much a part of that as a play.
            if (passed)
            {
                var slot = g.PlayRect(seat, 1);
                var pt = Text("pass", g.TextHeight, DimTextBrush);
                dc.DrawText(pt, new Point(slot.Left, slot.Top - pt.Height - Math.Max(3, g.CardWidth / 16) - 1));
                continue;
            }

            if (cards.Count == 0) continue;

            var rect = g.PlayRect(seat, cards.Count);

            int t = Math.Max(3, g.CardWidth / 16);

            if (seat == game.TableOwner)
            {
                // The play to beat. A halo behind it rather than a border over
                // it, so nothing is drawn on top of the artwork.
                var halo = rect.Inflate(t, t);
                dc.DrawRectangle(LeadBrush, null,
                    new Rect(halo.Left, halo.Top, halo.Width, halo.Height));
            }

            for (int i = 0; i < cards.Count; i++)
            {
                var p = g.PlaySlot(seat, i, cards.Count);
                dc.DrawImage(CardArt.Face(cards[i]), new Rect(p.X, p.Y, g.CardWidth, g.CardHeight));
            }

            if (seat == game.TableOwner)
            {
                var ft = Text("to beat", g.TextHeight, LeadTextBrush);
                dc.DrawText(ft, new Point(rect.Left, rect.Top - ft.Height - t - 1));
            }
        }
    }

    private void DrawButtons(DrawingContext dc, TableGeometry g)
    {
        for (int i = 0; i < g.Buttons.Count; i++)
        {
            var r = g.Buttons[i];
            string label = i < ButtonLabels.Length ? ButtonLabels[i] : "";
            if (label.Length == 0) continue;

            bool on = i < ButtonEnabled.Length && ButtonEnabled[i];
            dc.DrawRectangle(on ? ButtonFace : ButtonDisabled, null,
                             new Rect(r.Left, r.Top, r.Width, r.Height));

            var ft = Text(label, g.TextHeight, on ? ButtonText : DimTextBrush);
            dc.DrawText(ft, new Point(r.Left + (r.Width - ft.Width) / 2,
                                      r.Top + (r.Height - ft.Height) / 2));
        }
    }

    private void DrawMoving(DrawingContext dc, TableGeometry g)
    {
        foreach (var m in Moving)
        {
            var art = m.FaceUp ? CardArt.Face(m.CardId) : CardArt.Back();
            dc.DrawImage(art, new Rect(m.Current.X, m.Current.Y, g.CardWidth, g.CardHeight));
        }
    }

    private void DrawMessage(DrawingContext dc, TableGeometry g)
    {
        if (Message is null) return;

        var ft = Text(Message, g.TextHeight, MessageBrush);
        var r = g.MessageRow;
        dc.DrawText(ft, new Point(r.Left + (r.Width - ft.Width) / 2, r.Top));
    }

    /// <summary>
    /// The hand-over / series-over panel. Drawn over the table rather than as a
    /// modal window on purpose: the cards behind it stay visible, so the result
    /// can be checked against the hands that produced it.
    /// </summary>
    private void DrawOverlay(DrawingContext dc, TableGeometry g)
    {
        var title = Text(OverlayTitle!, (int)(g.TextHeight * 1.6), TextBrush);

        // Real columns, measured. Column 0 is left-aligned and every later one is
        // right-aligned against its own widest cell -- padding with spaces only
        // lines up in a monospaced font, and this table draws Segoe UI.
        var rows = OverlayRows.Select(r => r.Cells.Select(c => Text(c, g.TextHeight, TextBrush)).ToArray())
                              .ToArray();
        int columns = rows.Length == 0 ? 0 : rows.Max(r => r.Length);
        var colWidth = new double[columns];
        foreach (var r in rows)
            for (int c = 0; c < r.Length; c++)
                colWidth[c] = Math.Max(colWidth[c], r[c].Width);

        double colGap = Math.Max(12, g.CardWidth / 6.0);
        var colX = new double[columns];
        for (int c = 1; c < columns; c++)
            colX[c] = colX[c - 1] + colWidth[c - 1] + colGap;

        double bodyW = columns == 0 ? 0 : colX[columns - 1] + colWidth[columns - 1];
        double lineH = (rows.Length == 0 ? title.Height : rows.Max(r => r.Length == 0 ? title.Height : r.Max(c => c.Height))) + 4;

        double w = Math.Max(title.Width, bodyW);
        double h = title.Height + 12 + rows.Length * lineH;

        int padX = Math.Max(16, g.CardWidth / 4);
        int padY = Math.Max(12, g.CardHeight / 8);

        var box = new Rect(g.CentreRect.Left + (g.CentreRect.Width - w) / 2 - padX,
                           g.CentreRect.Top + (g.CentreRect.Height - h) / 2 - padY,
                           w + 2 * padX, h + 2 * padY);

        dc.DrawRectangle(OverlayBrush, OverlayPen, box);

        double bodyLeft = box.Left + (box.Width - bodyW) / 2;
        double y = box.Top + padY;
        dc.DrawText(title, new Point(box.Left + (box.Width - title.Width) / 2, y));
        y += title.Height + 12;

        foreach (var r in rows)
        {
            for (int c = 0; c < r.Length; c++)
            {
                // Column 0 left-aligned, the rest right-aligned within their column.
                double x = c == 0
                    ? bodyLeft
                    : bodyLeft + colX[c] + colWidth[c] - r[c].Width;
                dc.DrawText(r[c], new Point(x, y));
            }
            y += lineH;
        }
    }

    private FormattedText Text(string s, int pixelHeight, Brush brush)
    {
        // A GDI cell height is not a WPF em size. Divide by the typeface's own
        // height ratio rather than hardcoding one: passing a GDI cell height of
        // 13 straight through renders 17% oversized.
        double em = pixelHeight / Face.FontFamily.LineSpacing;
        return new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                 Face, em, brush, 1.0);
    }
}

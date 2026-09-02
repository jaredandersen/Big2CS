using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Big2.Core;

namespace Big2.App;

/// <summary>
/// Renders the table to a PNG without showing a window, so a change can be
/// checked without a person at the screen.
///
/// It renders the LIVE VISUAL TREE rather than screen-grabbing. A window that
/// has not painted yet, or that Windows is drawing a "not responding" ghost for,
/// captures as blank white -- which reads exactly like a rendering bug and is
/// not one. RenderTargetBitmap.Render always reflects what the element actually
/// contains, needs no foreground or z-order games, and works on a background
/// process.
///
/// Driven by three environment variables rather than one packed string:
///
///     BIG2_DUMP      = "900x800"                 (device pixels, required)
///     BIG2_DUMP_OUT  = a file path               (optional)
///     BIG2_DUMP_ARGS = "seed=7,plays=6,select=3" (optional)
///
/// The first version packed all of it into BIG2_DUMP separated by colons, which
/// broke on the first real Windows path handed to it: a drive letter is followed
/// by a colon, so the path split in half. Colons are not a usable delimiter here.
///
/// The size is in DEVICE PIXELS, not DIPs, so the output is directly comparable
/// whatever the display scale.
/// </summary>
public static class RenderDump
{
    public static void Run(string spec)
    {
        var size = spec.Trim().Split('x');
        if (size.Length != 2 || !int.TryParse(size[0], out int w) || !int.TryParse(size[1], out int h))
            throw new ArgumentException($"BIG2_DUMP must be WIDTHxHEIGHT in device pixels, got '{spec}'");

        string outPath = Environment.GetEnvironmentVariable("BIG2_DUMP_OUT") is { Length: > 0 } o
            ? o : "table.png";

        var parts = (Environment.GetEnvironmentVariable("BIG2_DUMP_ARGS") ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries);

        int seed = ArgInt(parts, "seed", 1);
        int plays = ArgInt(parts, "plays", 0);
        int selected = ArgInt(parts, "select", 0);

        // Resolve relative to the working directory and create the folder, so a
        // dump into a fresh directory does not fail after doing all the work.
        outPath = Path.GetFullPath(outPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

        var game = Big2Game.NewSeries(seed);
        var opponent = new HeuristicPlayer();
        var rng = new DealRandom(seed);

        // Drive the game far enough to have something on the table. Dumping at
        // startup catches the app mid-deal with an empty middle, which is not
        // the state anyone wants to look at.
        for (int i = 0; i < plays && !game.IsHandOver; i++)
        {
            if (game.IsTrickComplete) { game.CompleteTrick(); continue; }
            var move = opponent.ChooseMove(game, rng);
            if (move is null) game.Pass(); else game.Play(move);
        }

        // Drive to the end of the hand and show the real panel, built by the same
        // ScoreBoard the window uses -- a harness that formats its own copy would
        // be verifying a re-implementation.
        string? overlayTitle = null;
        IReadOnlyList<ScoreBoard.Row> overlayRows = Array.Empty<ScoreBoard.Row>();
        var buttons = new[] { "Play", "Pass", "Sort" };
        var buttonsOn = new[] { selected > 0, true, true };

        if (ArgInt(parts, "over", 0) == 1)
        {
            while (!game.IsHandOver)
            {
                if (game.IsTrickComplete) { game.CompleteTrick(); continue; }
                var m = opponent.ChooseMove(game, rng);
                if (m is null) game.Pass(); else game.Play(m);
            }

            var names = new[] { "You", "East", "North", "West" };
            var scores = new ScoreSheet();
            var penalties = new int[Dealer.Seats];
            for (int s2 = 0; s2 < Dealer.Seats; s2++)
                penalties[s2] = ScoreSheet.PenaltyFor(game.Hand(s2));
            scores.Record(penalties);

            overlayTitle = ScoreBoard.HandOverTitle(game.Winner, names, TableLayout.SeatSouth);
            overlayRows = ScoreBoard.Rows(scores, names, penalties);
            buttons = new[] { "Next Hand", "Finish", "" };
            buttonsOn = new[] { true, true, false };
        }

        // A mid-flight frame. The animator itself needs a render loop, but the
        // DRAWING is what can be wrong -- a card must appear in flight and NOT
        // also at the slot it left, and during a sweep the centre pile must not
        // be drawn under the cards leaving it. Both are checkable from a still.
        var moving = new List<MovingCard>();
        if (ArgInt(parts, "fly", 0) == 1 && !game.IsHandOver)
        {
            var g0 = TableLayout.For(w, h);
            int seat = game.Turn;
            var flyer = game.Hand(seat);
            var pick = MoveGenerator.Legal(flyer, game.Table, game.RequiredCard).FirstOrDefault()
                       ?? new[] { flyer[0] };

            for (int i = 0; i < pick.Length; i++)
            {
                int slot = Array.IndexOf(flyer.ToArray(), pick[i]);
                var from = g0[seat].SlotOrigin(Math.Max(0, slot));
                var to = g0.PlaySlot(seat, i, pick.Length);
                moving.Add(new MovingCard
                {
                    CardId = pick[i], FaceUp = true, From = from, To = to,
                    Seat = seat, Slot = slot,
                    Current = new LayoutPoint((from.X + to.X) / 2, (from.Y + to.Y) / 2),
                });
            }

            // The live code applies the play BEFORE animating it, so the dump
            // must too -- otherwise it renders a state the game never reaches
            // and misses exactly the double-draw this mode exists to catch.
            game.Play(pick);
        }

        var view = new TableView
        {
            Moving = moving,
            OverlayTitle = overlayTitle,
            OverlayRows = overlayRows,
            ButtonLabels = buttons,
            ButtonEnabled = buttonsOn,
            // One DIP == one device pixel for the dump, so the requested size is
            // honoured exactly and the bitmap needs no rescaling.
            PinDpiToOne = true,
            Game = game,
            Selected = new HashSet<int>(Enumerable.Range(0, Math.Min(selected, game.CardsLeft(0)))),
        };

        // An unshown Window has no layout of its own -- its size comes from its
        // HWND, so rendering one yields a 1x1 image. Measure and arrange the
        // content explicitly instead.
        view.Measure(new Size(w, h));
        view.Arrange(new Rect(0, 0, w, h));
        view.UpdateLayout();
        view.Rebuild();
        view.UpdateLayout();

        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(view);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using (var fs = File.Create(outPath))
            encoder.Save(fs);

        var g = view.Geometry!;

        // Emit the geometry alongside the image so a checker knows where the
        // cards are without re-deriving the layout -- a checker that computes
        // its own idea of the geometry is testing itself, not the renderer.
        var south = g[TableLayout.SeatSouth];
        var hand = game.Hand(TableLayout.SeatSouth);

        // The winner ends the hand holding nothing, so there may be no last card
        // to point the palette check at. Say so rather than indexing past the end.
        string lastCard = "null";
        if (hand.Count > 0)
        {
            var slot = south.SlotOrigin(hand.Count - 1);
            lastCard = "{ \"x\": " + slot.X + ", \"y\": " + slot.Y +
                       ", \"id\": " + hand[^1] +
                       ", \"bitmapId\": " + Cards.BitmapIdOf(hand[^1]) + " }";
        }

        File.WriteAllText(Path.ChangeExtension(outPath, ".json"),
            $$"""
            {
              "width": {{w}}, "height": {{h}},
              "scale": {{g.Scale.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}},
              "cardWidth": {{g.CardWidth}}, "cardHeight": {{g.CardHeight}},
              "tableColor": "#{{TableView.TableColor.R:X2}}{{TableView.TableColor.G:X2}}{{TableView.TableColor.B:X2}}",
              "lastHandCard": {{lastCard}}
            }
            """);
        Console.Error.WriteLine(
            $"BIG2_DUMP wrote {outPath}  {w}x{h} device px  scale {g.Scale:F3}  " +
            $"card {g.CardWidth}x{g.CardHeight}  seed {seed}  plays {plays}");
    }

    private static int ArgInt(string[] parts, string name, int fallback)
    {
        foreach (var p in parts)
            if (p.StartsWith(name + "=", StringComparison.Ordinal) &&
                int.TryParse(p.AsSpan(name.Length + 1), out int v))
                return v;
        return fallback;
    }
}

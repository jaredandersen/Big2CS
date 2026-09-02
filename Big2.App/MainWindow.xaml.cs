using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Big2.Core;

namespace Big2.App;

public partial class MainWindow : Window
{
    private readonly TableView _table = new();
    private readonly HashSet<int> _selected = new();
    private readonly ScoreSheet _scores = new();
    private readonly Settings _settings = Settings.Load();
    private readonly CardAnimator _animator = new();
    private readonly DealRandom _rng = new(Environment.TickCount);
    /// <summary>
    /// Rebuilt whenever the difficulty setting changes -- a policy's skills are
    /// fixed at construction, so changing the setting has to replace the player
    /// rather than poke at it.
    /// </summary>
    private IPlayer _opponent = HeuristicPlayer.For(Difficulty.Normal);

    private Big2Game _game = null!;
    private int _seed = Environment.TickCount;

    /// <summary>
    /// A scripted opening position, for capturing a screenshot of the real
    /// window. Set by BIG2_POSE, e.g. "seed=7". Null in normal play.
    ///
    /// This exists because <see cref="RenderDump"/> renders the TABLE ONLY --
    /// no menu bar, no title bar -- so it cannot produce a picture of the
    /// application. It also selects cards by index, which is fine for a layout
    /// check and wrong for a screenshot: an arbitrary pair of cards next to a
    /// trick of singles shows an illegal move.
    /// </summary>
    private readonly Pose? _pose = Pose.FromEnvironment();

    private sealed record Pose(int Seed)
    {
        public static Pose? FromEnvironment()
        {
            string? spec = Environment.GetEnvironmentVariable("BIG2_POSE");
            if (string.IsNullOrWhiteSpace(spec)) return null;

            int seed = 1;
            foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (part.StartsWith("seed=", StringComparison.Ordinal) &&
                    int.TryParse(part[5..], out int v)) seed = v;
            return new Pose(seed);
        }
    }
    private int _previousWinner = -1;
    private bool _framesMeasured;
    private double _frameWidth, _frameHeight;

    /// <summary>Guards the pump against re-entry while an opponent move is pending.</summary>
    private bool _thinking;

    /// <summary>
    /// What the table is doing.
    ///
    /// A finished hand does NOT roll into the next one on a timer. It waits to be
    /// acknowledged, and the player decides whether to play on or stop -- a hand
    /// that vanishes before you have read the result is worse than no result.
    /// </summary>
    private enum Phase { Playing, HandOver, SeriesOver }

    private Phase _phase = Phase.Playing;

    /// <summary>How the player's fan is arranged. Cycled by the Sort button.</summary>
    private HandSort _handOrder;


    public MainWindow()
    {
        InitializeComponent();
        TableHost.Content = _table;

        _table.MouseLeftButtonDown += OnTableClick;
        _table.SeatNames = _settings.SeatNames;
        _opponent = HeuristicPlayer.For(_settings.Difficulty);

        _animator.Tick += (_, _) =>
        {
            _table.Moving = _animator.Cards;
            _table.InvalidateVisual();
        };

        // A live resize invalidates every endpoint in flight, because they were
        // computed against the old geometry. Snapping to the end is cosmetic
        // here, not a crash fix -- this animator holds two points per card and
        // nothing sized at its own start.
        SizeChanged += (_, _) => _animator.CancelToEnd();
        _handOrder = _settings.HandOrder;

        MenuNewHand.Click += (_, _) => AskThenNewHand();
        MenuNewSeries.Click += (_, _) => AskThenNewSeries();
        MenuSort.Click += (_, _) => OnButton(TableButton.Sort);
        MenuOptions.Click += (_, _) => ShowOptions();
        MenuExit.Click += (_, _) => Close();
        MenuAbout.Click += (_, _) => new AboutDialog(this).ShowDialog();

        PreviewKeyDown += OnPreviewKeyDown;

        // Restore in Loaded, not the constructor: a size assigned before the
        // window has a handle does not survive to the first layout pass.
        Loaded += (_, _) => { RestoreWindow(); NewSeries(); };
        ContentRendered += (_, _) => MeasureFrameOnce();
        Closing += (_, _) => SaveSettings();
    }

    /// <summary>
    /// The window frame is measured after ContentRendered, not at Loaded, and
    /// exactly once.
    ///
    /// At Loaded the content has not settled on its final size, so the difference
    /// reads as the chrome alone. And re-measuring on every SizeChanged is worse:
    /// during a resize cascade the content has not been arranged yet, so the
    /// difference is momentarily huge, and latching that into MinWidth drags the
    /// window bigger -- which raises SizeChanged again. A 555x914 client has been
    /// produced by exactly that feedback.
    /// </summary>
    private void MeasureFrameOnce()
    {
        if (_framesMeasured) return;
        _framesMeasured = true;

        _frameWidth = ActualWidth - _table.ActualWidth;
        _frameHeight = ActualHeight - _table.ActualHeight;

        var dpi = VisualTreeHelper.GetDpi(this);
        MinWidth = TableLayout.NativeTableWidth / dpi.DpiScaleX + _frameWidth;
        MinHeight = TableLayout.NativeTableHeight / dpi.DpiScaleY + _frameHeight;
    }

    // -------------------------------------------------------------------- menu

    /// <summary>
    /// Accelerators are handled in PreviewKeyDown rather than as InputBindings.
    ///
    /// A bare F10 is "activate the menu bar" to Windows and WPF honours it, so an
    /// F10 accelerator arrives as Key.System with SystemKey set. Nothing here
    /// binds F10 today, but the handler is shaped to cope if one is added.
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        switch (key)
        {
            case Key.F2: AskThenNewHand(); e.Handled = true; break;
            case Key.F5: ShowOptions(); e.Handled = true; break;
            case Key.F6: OnButton(TableButton.Sort); e.Handled = true; break;
        }
    }

    /// <summary>Abandoning a hand in progress is worth confirming; a finished one is not.</summary>
    private bool MayAbandonHand()
    {
        if (_phase != Phase.Playing) return true;
        if (_game.PlayHistory.Count == 0) return true;

        return MessagePrompt.Ask(this, "Big 2",
            "Abandon this hand and deal again?", "Deal", "Keep playing");
    }

    private void AskThenNewHand()
    {
        if (MayAbandonHand()) NewHand();
    }

    private void AskThenNewSeries()
    {
        // A series can be arbitrarily long and nothing else ends it, so throwing
        // one away always asks -- even from the hand-over panel.
        if (_scores.HandsPlayed > 0 &&
            !MessagePrompt.Ask(this, "Big 2",
                $"Start a new series? {_scores.HandsPlayed} hand" +
                $"{(_scores.HandsPlayed == 1 ? "" : "s")} will be discarded.",
                "New series", "Keep playing"))
            return;

        NewSeries();
    }

    private void ShowOptions()
    {
        var dlg = new OptionsDialog(this, _settings);
        dlg.ShowDialog();
        if (!dlg.Accepted) return;

        dlg.ApplyTo(_settings);
        _table.SeatNames = _settings.SeatNames;

        // The three opponents are one policy object asked in turn, so swapping
        // it here changes all three. It takes effect on the next move rather
        // than the next hand -- which is what a player changing the setting
        // mid-hand almost certainly wants, and is harmless because the policy
        // holds no per-hand state.
        _opponent = HeuristicPlayer.For(_settings.Difficulty);
        Title = SeriesTitle();
        Redraw();
    }

    // ---------------------------------------------------------------- settings

    private void RestoreWindow()
    {
        if (_settings.Window is not { } saved) return;

        // Clamp against the display AS IT IS NOW. A null means "use the default"
        // rather than a salvaged position -- see WindowPlacement.ClampTo.
        var fitted = saved.ClampTo(SystemParameters.VirtualScreenLeft,
                                   SystemParameters.VirtualScreenTop,
                                   SystemParameters.VirtualScreenWidth,
                                   SystemParameters.VirtualScreenHeight);
        if (fitted is not { } p) return;

        Left = p.Left;
        Top = p.Top;
        Width = p.Width;
        Height = p.Height;
        if (p.Maximized) WindowState = WindowState.Maximized;
    }

    private void SaveSettings()
    {
        // RestoreBounds, NOT Left/Top/Width/Height: those report the MAXIMIZED
        // rectangle while maximized, and remembering that then restoring it
        // un-maximized gives a normal window covering the whole screen.
        var r = RestoreBounds;
        _settings.Window = new WindowPlacement(r.Left, r.Top, r.Width, r.Height,
                                               WindowState == WindowState.Maximized);
        _settings.HandOrder = _handOrder;
        _settings.SeatNames = _table.SeatNames;
        _settings.SeriesTotals = _scores.Totals.ToArray();
        _settings.SeriesHands = _scores.HandsPlayed;
        _settings.Save();
    }

    // ------------------------------------------------------------------ series

    private void NewSeries()
    {
        _scores.Reset();
        _previousWinner = -1;
        NewHand();
    }

    private void NewHand()
    {
        _seed = _pose is { } p ? p.Seed : unchecked(_seed * 1103515245 + 12345);
        _game = _previousWinner < 0
            ? Big2Game.NewSeries(_seed)
            : Big2Game.NextHand(_seed, _previousWinner);

        _game.SortHand(TableLayout.SeatSouth, _handOrder);

        _selected.Clear();
        _phase = Phase.Playing;
        _table.Message = null;
        _table.OverlayTitle = null;
        _table.OverlayRows = Array.Empty<ScoreBoard.Row>();
        _table.Game = _game;
        _table.Selected = _selected;
        _table.Rebuild();

        Redraw();
        Pump();
    }

    /// <summary>
    /// Under a pose, pre-select a play that is actually LEGAL against whatever
    /// is on the table, so the picture shows a move the game would accept.
    /// Selecting by index instead is what produced a screenshot with two
    /// unrelated cards raised against a trick of singles.
    ///
    /// It prefers the largest legal play, because the point of the picture is
    /// the table with every seat's cards on it, and a five-card hand shows more
    /// of the game than a single does.
    /// </summary>
    private void PoseSelection()
    {
        if (_pose is null || _selected.Count > 0) return;

        var legal = _game.LegalPlays();
        if (legal.Count == 0) return;

        var pick = legal[0];
        foreach (var m in legal) if (m.Length > pick.Length) pick = m;

        var hand = _game.Hand(TableLayout.SeatSouth);
        foreach (int id in pick)
        {
            int slot = -1;
            for (int i = 0; i < hand.Count; i++) if (hand[i] == id) { slot = i; break; }
            if (slot >= 0) _selected.Add(slot);
        }
    }

    /// <summary>
    /// Button labels and enablement for the current phase. The same three slots
    /// read Play/Pass/Sort during a hand and Next Hand/Finish once one is over,
    /// so the labels live here rather than being baked into the view.
    /// </summary>
    private void SyncButtons()
    {
        switch (_phase)
        {
            case Phase.HandOver:
                _table.ButtonLabels = new[] { "Next Hand", "Finish", "" };
                _table.ButtonEnabled = new[] { true, true, false };
                break;

            case Phase.SeriesOver:
                _table.ButtonLabels = new[] { "New Series", "", "" };
                _table.ButtonEnabled = new[] { true, false, false };
                break;

            default:
                bool human = _game.Turn == TableLayout.SeatSouth && !_game.IsHandOver;
                _table.ButtonLabels = new[] { "Play", "Pass", "Sort" };
                _table.ButtonEnabled = new[]
                {
                    human && _selected.Count > 0,
                    human && _game.CanPass,
                    true,
                };
                break;
        }
    }

    private void Redraw()
    {
        SyncButtons();
        _table.InvalidateVisual();
    }

    // -------------------------------------------------------------------- pump

    /// <summary>
    /// Advances the game until it is the human's turn or the hand ends.
    ///
    /// One step per tick rather than a loop, so there is a gap for animation to
    /// live in later. Phase 3 replaces the timer with the animator; the structure
    /// does not change.
    /// </summary>
    private void Pump()
    {
        if (_thinking || _animator.IsRunning || _phase != Phase.Playing) return;

        if (_game.IsHandOver)
        {
            EndHand();
            return;
        }

        if (_game.IsTrickComplete)
        {
            // The sweep animates BEFORE CompleteTrick, because completing the
            // trick CLEARS THE TABLE and afterwards there is nothing left to
            // move. IsTrickComplete and PendingTrickWinner exist to be peeked at
            // for exactly this.
            int winner = _game.PendingTrickWinner;
            var sweep = BuildSweep(winner);

            Defer(() => Animate(sweep, Animation.SweepMs(_settings.AnimationSpeed), () =>
            {
                _game.CompleteTrick();
                Redraw();
                Pump();
            }), (int)Animation.TrickPauseMs(_settings.AnimationSpeed));
            return;
        }

        if (_game.Turn == TableLayout.SeatSouth)
        {
            PoseSelection();
            Redraw();
            return;
        }

        _thinking = true;
        Defer(() =>
        {
            _thinking = false;
            int seat = _game.Turn;
            var move = _opponent.ChooseMove(_game, _rng);

            if (move is null)
            {
                _game.Pass();
                Redraw();
                Pump();
                return;
            }

            var flight = BuildPlay(seat, move);
            _game.Play(move);
            Redraw();
            Animate(flight, Animation.PlayMs(_settings.AnimationSpeed), Pump);
        }, (int)Animation.ThinkMs(_settings.AnimationSpeed));
    }

    private void EndHand()
    {
        _previousWinner = _game.Winner;

        var penalties = new int[Dealer.Seats];
        for (int s = 0; s < Dealer.Seats; s++)
            penalties[s] = ScoreSheet.PenaltyFor(_game.Hand(s));
        _scores.Record(penalties);

        _phase = Phase.HandOver;
        _table.OverlayTitle = ScoreBoard.HandOverTitle(_game.Winner, _table.SeatNames, TableLayout.SeatSouth);
        _table.OverlayRows = ScoreBoard.Rows(_scores, _table.SeatNames, penalties);

        Title = SeriesTitle();
        Redraw();

        // An optional target, off by default. When it is set, reaching it ends
        // the series rather than merely being noted.
        if (_settings.TargetScore > 0 && _scores.SeatsReaching(_settings.TargetScore).Count > 0)
            EndSeries();
    }

    private void EndSeries()
    {
        _phase = Phase.SeriesOver;
        _table.OverlayTitle = ScoreBoard.SeriesOverTitle(_scores, _table.SeatNames);
        _table.OverlayRows = ScoreBoard.Rows(_scores, _table.SeatNames, null);
        Redraw();
    }

    private string SeriesTitle() =>
        "Big 2  -  " + string.Join("   ", Enumerable.Range(0, Dealer.Seats)
            .Select(s => $"{_table.SeatNames[s]} {_scores.Totals[s]}"));

    // ------------------------------------------------------------- animation

    private void Animate(List<MovingCard> cards, double ms, Action onDone)
    {
        if (cards.Count == 0 || ms < 1) { onDone(); return; }

        _table.Moving = cards;
        _animator.Start(cards, ms, () =>
        {
            _table.Moving = Array.Empty<MovingCard>();
            _table.InvalidateVisual();
            onDone();
        });
    }

    /// <summary>
    /// Cards flying from a seat's hand into the centre.
    ///
    /// Built BEFORE Play() is called, because the slots the cards leave from
    /// only exist while they are still in the hand.
    /// </summary>
    private List<MovingCard> BuildPlay(int seat, IReadOnlyList<int> cards)
    {
        var result = new List<MovingCard>();
        if (_table.Geometry is not { } g) return result;

        var hand = _game.Hand(seat);
        var layout = g[seat];

        for (int i = 0; i < cards.Count; i++)
        {
            // For an opponent we do not know which back was which, so the slot
            // is taken positionally from the end of the fan; for the human the
            // real slot is known.
            int slot = seat == TableLayout.SeatSouth
                ? IndexOf(hand, cards[i])
                : hand.Count - cards.Count + i;

            var from = layout.SlotOrigin(Math.Max(0, slot));
            var to = g.PlaySlot(seat, i, cards.Count);

            result.Add(new MovingCard
            {
                CardId = cards[i],
                FaceUp = true,          // a played card always arrives face up
                From = from,
                To = to,
                Seat = seat,
                Slot = slot,
            });
        }
        return result;
    }

    /// <summary>Every card on the table flying off to whoever took the trick.</summary>
    private List<MovingCard> BuildSweep(int winner)
    {
        var result = new List<MovingCard>();
        if (_table.Geometry is not { } g || winner < 0) return result;

        var target = g[winner];
        var home = target.SlotOrigin(0);

        for (int seat = 0; seat < Dealer.Seats; seat++)
        {
            var cards = _game.TrickPlay(seat);
            for (int i = 0; i < cards.Count; i++)
            {
                result.Add(new MovingCard
                {
                    CardId = cards[i],
                    FaceUp = true,
                    From = g.PlaySlot(seat, i, cards.Count),
                    To = home,
                    FromCentre = true,
                });
            }
        }
        return result;
    }

    private static int IndexOf(IReadOnlyList<int> hand, int card)
    {
        for (int i = 0; i < hand.Count; i++)
            if (hand[i] == card) return i;
        return 0;
    }

    private void Defer(Action a, int ms = 320) =>
        new DispatcherTimer(TimeSpan.FromMilliseconds(ms), DispatcherPriority.Background,
            (s, _) => { ((DispatcherTimer)s!).Stop(); a(); }, Dispatcher).Start();

    // ------------------------------------------------------------------- input

    private void OnTableClick(object sender, MouseButtonEventArgs e)
    {
        if (_table.Geometry is not { } g) return;

        var (x, y) = _table.ToDevice(e.GetPosition(_table));
        var hit = HitTest.At(g, _game.Hand(TableLayout.SeatSouth).Count, _selected, x, y);

        if (hit.Button is { } button)
        {
            OnButton(button);
            return;
        }

        if (_phase != Phase.Playing) return;

        // Single-click targets are checked BEFORE any click-count branch. WPF
        // reports every other rapid click as ClickCount == 2, so routing this
        // through a double-click handler would make the game intermittently
        // ignore selections -- "sometimes it just doesn't respond".
        if (hit.IsCard && _game.Turn == TableLayout.SeatSouth)
        {
            if (!_selected.Add(hit.CardSlot)) _selected.Remove(hit.CardSlot);
            _table.Message = null;   // the selection changed: the complaint is stale
            Redraw();
        }
    }

    private void OnButton(TableButton button)
    {
        // The three slots mean different things per phase, so the phase is
        // checked first -- otherwise "Next Hand" would land on the Play handler
        // and try to play an empty selection.
        if (_phase == Phase.HandOver)
        {
            if (button == TableButton.Play) NewHand();
            else if (button == TableButton.Pass) EndSeries();
            return;
        }

        if (_phase == Phase.SeriesOver)
        {
            if (button == TableButton.Play) NewSeries();
            return;
        }

        switch (button)
        {
            case TableButton.Sort when true:
                // Cycles rank and suit. The selection is cleared because it is
                // held as SLOT indices, and re-sorting moves the cards under
                // them -- keeping it would silently change which cards are
                // chosen, which is worse than losing the selection.
                _handOrder = HandSorting.Next(_handOrder);
                _game.SortHand(TableLayout.SeatSouth, _handOrder);
                _selected.Clear();
                _table.Message = null;
                Redraw();
                break;

            case TableButton.Pass when _game.Turn == TableLayout.SeatSouth && _game.CanPass:
                _selected.Clear();
                _table.Message = null;
                _game.Pass();
                Redraw();
                Pump();
                break;

            case TableButton.Play when _game.Turn == TableLayout.SeatSouth && _selected.Count > 0:
                var hand = _game.Hand(TableLayout.SeatSouth);
                var cards = _selected.OrderBy(i => i).Select(i => hand[i]).ToArray();

                // Say WHICH rule was broken. A beep says only that something is
                // wrong, and these rules are exactly what a new player is still
                // learning. The message stays up until the selection changes.
                if (PlayExplanation.Why(_game, cards) is { } why)
                {
                    _table.Message = why;
                    Redraw();
                    return;
                }

                _table.Message = null;
                var flight = BuildPlay(TableLayout.SeatSouth, cards);
                _selected.Clear();
                _game.Play(cards);
                Redraw();
                Animate(flight, Animation.PlayMs(_settings.AnimationSpeed), Pump);
                break;
        }
    }
}

using System.Diagnostics;
using System.Windows.Media;
using Big2.Core;

namespace Big2.App;

/// <summary>A card in flight. Positions are table-relative device pixels.</summary>
public sealed class MovingCard
{
    public required int CardId { get; init; }
    public required bool FaceUp { get; init; }
    public required LayoutPoint From { get; init; }
    public required LayoutPoint To { get; init; }

    /// <summary>Where to draw it this frame.</summary>
    public LayoutPoint Current { get; internal set; }

    /// <summary>Which seat and slot it left, so the fan can avoid drawing it twice.</summary>
    public int Seat { get; init; } = -1;
    public int Slot { get; init; } = -1;

    /// <summary>True while it is leaving the centre rather than arriving there.</summary>
    public bool FromCentre { get; init; }
}

/// <summary>
/// Slides cards between two points, driven by the COMPOSITOR rather than a
/// timer, so motion is sampled at the display's refresh rate.
///
/// A card that advances a whole number of pixels per loop iteration jumps in
/// visible increments -- how card animation was done when a loop iteration was
/// the only clock available. Interpolating continuously over the same total
/// distance keeps the movement and drops the artefact.
///
/// For Big 2 the duration is invented outright -- there is no original to take a
/// pace from, only a rule set. The duration is therefore a chosen number, not
/// a measured one.
///
/// The animator holds nothing but two points per card. That matters for the
/// resize hazard: an animation that owns state SIZED AT ITS OWN START can freeze
/// outright when a concurrent resize invalidates it. Holding only endpoints
/// reduces that to a stale frame, which is cosmetic. So
/// <see cref="CancelToEnd"/> exists to stop cards flying to stale destinations,
/// not to prevent a crash, and this comment says so rather than letting a future
/// reader infer a severity that was never here.
/// </summary>
public sealed class CardAnimator
{
    private readonly List<MovingCard> _cards = new();
    private readonly Stopwatch _clock = new();
    private double _durationMs;
    private Action? _onComplete;
    private bool _running;

    public bool IsRunning => _running;

    /// <summary>Cards currently in flight, for the renderer to draw on top.</summary>
    public IReadOnlyList<MovingCard> Cards => _cards;

    /// <summary>Raised every frame so the view can repaint.</summary>
    public event EventHandler? Tick;

    public void Start(IEnumerable<MovingCard> cards, double durationMs, Action onComplete)
    {
        Stop();

        _cards.AddRange(cards);
        foreach (var c in _cards) c.Current = c.From;
        _onComplete = onComplete;

        // A zero-length move, or a duration rounded away to nothing, would divide
        // by zero below. Finish it immediately instead of animating it.
        if (_cards.Count == 0 || durationMs < 1.0)
        {
            Finish();
            return;
        }

        _durationMs = durationMs;
        _running = true;
        _clock.Restart();
        CompositionTarget.Rendering += OnRendering;
    }

    /// <summary>
    /// Ends the animation now, snapping every card to its destination and
    /// running the completion callback. This is what a live resize calls: the
    /// endpoints in flight were computed against the old geometry, so without it
    /// a card finishes its journey to a stale destination and then jumps.
    /// </summary>
    public void CancelToEnd()
    {
        if (_running) Finish();
    }

    /// <summary>Ends the animation WITHOUT running the completion callback.</summary>
    public void Stop()
    {
        if (_running)
        {
            CompositionTarget.Rendering -= OnRendering;
            _running = false;
        }
        _clock.Reset();
        _onComplete = null;
        _cards.Clear();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        double t = _clock.Elapsed.TotalMilliseconds / _durationMs;
        if (t >= 1.0)
        {
            Finish();
            return;
        }

        // Ease out: a card that decelerates into place reads as being PUT
        // somewhere rather than fired at it.
        double e2 = 1 - (1 - t) * (1 - t);

        foreach (var c in _cards)
        {
            c.Current = new LayoutPoint(
                (int)Math.Round(c.From.X + (c.To.X - c.From.X) * e2),
                (int)Math.Round(c.From.Y + (c.To.Y - c.From.Y) * e2));
        }

        Tick?.Invoke(this, EventArgs.Empty);
    }

    private void Finish()
    {
        if (_running)
        {
            CompositionTarget.Rendering -= OnRendering;
            _running = false;
        }
        _clock.Reset();

        foreach (var c in _cards) c.Current = c.To;

        var done = _onComplete;
        _onComplete = null;
        _cards.Clear();

        Tick?.Invoke(this, EventArgs.Empty);
        done?.Invoke();
    }
}

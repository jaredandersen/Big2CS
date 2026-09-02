namespace Big2.Core;

/// <summary>
/// Running totals across an open-ended series.
///
/// The scoring is NOT TEGL's -- deliberately, and this is the one place the
/// project departs from the rules it otherwise follows exactly:
///
///   points = cardsLeft * (cardsLeft &gt;= 10 ? 2 : 1) * 2^(twos held)
///
/// TEGL doubles at ten cards and TRIPLES at all thirteen, and has no
/// unplayed-twos clause at all. This keeps the twos multiplier and drops the
/// thirteen-card tier. Both departures are deliberate.
///
/// There is NO target score and NO terminal state. The series runs until the
/// player stops, so this class deliberately exposes no Winner or IsGameOver --
/// offering them would invite the UI to render a game-over that never fires. A
/// target is an optional UI setting layered on top, not a property of the sheet.
/// </summary>
public sealed class ScoreSheet
{
    private readonly int[] _totals = new int[Dealer.Seats];
    private readonly List<int[]> _hands = new();

    /// <summary>Running total per seat. Lower is better.</summary>
    public IReadOnlyList<int> Totals => _totals;

    /// <summary>Per-hand results, oldest first. Unbounded -- the sheet must not assume a length.</summary>
    public IReadOnlyList<int[]> Hands => _hands;

    public int HandsPlayed => _hands.Count;

    /// <summary>
    /// The penalty for one hand: cards left, times the count tier, times two for
    /// each unplayed two.
    /// </summary>
    public static int PenaltyFor(IReadOnlyList<int> hand)
    {
        int cards = hand.Count;
        if (cards == 0) return 0;

        int points = cards * (cards >= 10 ? 2 : 1);

        foreach (int id in hand)
            if (Cards.RankOf(id) == 12)   // a two
                points *= 2;

        return points;
    }

    /// <summary>Records one hand's penalties and adds them to the running totals.</summary>
    public void Record(IReadOnlyList<int> penalties)
    {
        if (penalties.Count != Dealer.Seats)
            throw new ArgumentException("expected one penalty per seat", nameof(penalties));

        var row = penalties.ToArray();
        _hands.Add(row);
        for (int s = 0; s < Dealer.Seats; s++) _totals[s] += row[s];
    }

    /// <summary>Clears the series. This is New Series, not New Hand.</summary>
    public void Reset()
    {
        Array.Clear(_totals);
        _hands.Clear();
    }

    /// <summary>
    /// Seats at or past <paramref name="target"/>, for the optional target-score
    /// setting. Returns empty when no target is set (<paramref name="target"/>
    /// less than or equal to zero), which is the default.
    /// </summary>
    public IReadOnlyList<int> SeatsReaching(int target)
    {
        if (target <= 0) return Array.Empty<int>();
        var hit = new List<int>();
        for (int s = 0; s < Dealer.Seats; s++)
            if (_totals[s] >= target) hit.Add(s);
        return hit;
    }
}

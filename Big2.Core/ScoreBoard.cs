namespace Big2.Core;

/// <summary>
/// The text of the hand-over and series-over panels.
///
/// This lives in the core, away from the window, for one practical reason: the
/// render harness has to be able to draw the real panel. Building the strings in
/// MainWindow and a second copy in RenderDump would mean the harness verifies a
/// re-implementation rather than the thing that ships -- the failure Pinball's
/// ball-test harness had, where a duplicated game loop silently omitted a call
/// and left a real bug with zero coverage.
///
/// It is also pure data, so it can be pinned by a test.
///
/// Rows are COLUMNS, not padded strings. Padding with spaces only lines up in a
/// monospaced font, and the table draws Segoe UI -- the first version padded and
/// the numbers came out visibly ragged.
/// </summary>
public static class ScoreBoard
{
    /// <summary>Column 0 is left-aligned; every later column is right-aligned.</summary>
    public sealed record Row(params string[] Cells);

    public static string HandOverTitle(int winner, IReadOnlyList<string> names, int humanSeat) =>
        winner == humanSeat ? "You win the hand" : $"{names[winner]} wins the hand";

    public static string SeriesOverTitle(ScoreSheet scores, IReadOnlyList<string> names)
    {
        int best = scores.Totals.Min();
        var leaders = Enumerable.Range(0, Dealer.Seats)
                                .Where(s => scores.Totals[s] == best)
                                .Select(s => names[s])
                                .ToArray();

        return leaders.Length > 1
            ? $"{string.Join(" and ", leaders)} tie the series"
            : $"{leaders[0]} wins the series";
    }

    /// <summary>
    /// One row per seat. With <paramref name="penalties"/> the panel shows this
    /// hand alongside the running total; without them it is the series summary.
    ///
    /// Lower is better -- the score is what you were left holding -- so the panel
    /// says so rather than leaving the player to infer it from a column of
    /// numbers that only grows.
    /// </summary>
    public static Row[] Rows(ScoreSheet scores, IReadOnlyList<string> names, int[]? penalties)
    {
        var rows = new List<Row>();

        if (penalties is not null)
            rows.Add(new Row("", "this hand", "total"));

        for (int s = 0; s < Dealer.Seats; s++)
        {
            rows.Add(penalties is null
                ? new Row(names[s], scores.Totals[s].ToString())
                : new Row(names[s], penalties[s].ToString(), scores.Totals[s].ToString()));
        }

        rows.Add(new Row(""));
        rows.Add(new Row($"hands played: {scores.HandsPlayed}"));
        rows.Add(new Row("lowest total wins"));
        return rows.ToArray();
    }
}

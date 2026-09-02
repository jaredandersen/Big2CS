namespace Big2.Core;

/// <summary>How a single hand ended.</summary>
public readonly record struct HandResult(int Winner, int[] CardsLeft, int[] Points);

/// <summary>A head-to-head result with an interval, so it can be believed or not.</summary>
public readonly record struct MatchResult(
    string Candidate, string Opponent, int Games, int Wins,
    double WinRate, double Low, double High,
    double MeanPoints, double PointsLow, double PointsHigh)
{
    /// <summary>
    /// True when the interval excludes chance. A candidate that does not clear
    /// this has not been shown to be better, however good its mean looks.
    /// </summary>
    public bool SignificantlyBetterThanChance => Low > 0.25;

    public override string ToString() =>
        $"{Candidate} vs {Opponent}: {Wins}/{Games} = {WinRate:P1} " +
        $"[{Low:P1}, {High:P1}]  points {MeanPoints:F2} [{PointsLow:F2}, {PointsHigh:F2}]";
}

/// <summary>
/// The strength instrument. The DOS corpus cannot provide one -- it records
/// plays, not scores, and TEGL is not a standard to be right against -- so this
/// is the only thing that answers "is this policy better than that one".
///
/// Built on maxjiang216's eval_match design rather than a naive mean, for a
/// reason his repository demonstrates by omission elsewhere: cappinmeow's
/// published weights were tuned against TWELVE games per candidate, which cannot
/// resolve a real edge in a game this variable, and are consequently mostly
/// noise.
///
/// Two things fix that:
///
///   PAIRED DEALS -- each deal is played four times with the candidate rotated
///   through all four seats. Both sides see identical cards, so deal luck
///   cancels instead of being averaged over.
///
///   A WILSON INTERVAL on the win rate, so a result says whether the difference
///   is real rather than just reporting a number.
/// </summary>
public static class SelfPlay
{
    /// <summary>Hard cap on plays per hand, so a policy bug cannot hang a run.</summary>
    private const int MaxPlaysPerHand = 400;

    /// <summary>
    /// Plays one hand to completion. <paramref name="players"/> is indexed by
    /// seat. Returns the result, or throws if a policy misbehaves.
    /// </summary>
    public static HandResult PlayHand(IPlayer[] players, int[][] hands, int leader,
                                      bool openingLeadOfSeries, DealRandom rng)
    {
        var game = new Big2Game(hands, leader, openingLeadOfSeries);

        for (int guard = 0; !game.IsHandOver; guard++)
        {
            if (guard > MaxPlaysPerHand)
                throw new InvalidOperationException("hand did not terminate");

            if (game.IsTrickComplete)
            {
                game.CompleteTrick();
                continue;
            }

            var move = players[game.Turn].ChooseMove(game, rng);
            if (move is null)
            {
                if (!game.CanPass)
                    throw new InvalidOperationException(
                        $"{players[game.Turn].Name} passed while holding the lead");
                game.Pass();
            }
            else
            {
                game.Play(move);
            }
        }

        var left = new int[Dealer.Seats];
        var points = new int[Dealer.Seats];
        for (int s = 0; s < Dealer.Seats; s++)
        {
            left[s] = game.CardsLeft(s);
            points[s] = ScoreSheet.PenaltyFor(game.Hand(s));
        }
        return new HandResult(game.Winner, left, points);
    }

    /// <summary>
    /// Paired-deal match: <paramref name="candidate"/> against three copies of
    /// <paramref name="opponent"/>, over <paramref name="deals"/> deals, the
    /// candidate rotated through all four seats on each. Total hands = 4 x deals.
    /// </summary>
    public static MatchResult Match(IPlayer candidate, IPlayer opponent, int deals, int baseSeed)
    {
        int wins = 0, games = 0;
        long totalPoints = 0, totalPointsSq = 0;

        for (int d = 0; d < deals; d++)
        {
            int seed = baseSeed + d;

            for (int seat = 0; seat < Dealer.Seats; seat++)
            {
                // Same seed => identical cards on every rotation. That is what
                // makes this paired rather than merely repeated.
                var hands = Dealer.Deal(seed);
                var players = new IPlayer[Dealer.Seats];
                for (int s = 0; s < Dealer.Seats; s++)
                    players[s] = s == seat ? candidate : opponent;

                var rng = new DealRandom(seed * 4 + seat);
                var r = PlayHand(players, hands, Dealer.SeatHoldingThreeOfDiamonds(hands),
                                 openingLeadOfSeries: true, rng);

                games++;
                if (r.Winner == seat) wins++;
                long p = r.Points[seat];
                totalPoints += p;
                totalPointsSq += p * p;
            }
        }

        var (lo, hi) = WilsonInterval(wins, games);
        var (mean, pLo, pHi) = MeanInterval(totalPoints, totalPointsSq, games);
        return new MatchResult(candidate.Name, opponent.Name, games, wins,
                               games == 0 ? 0 : (double)wins / games,
                               lo, hi, mean, pLo, pHi);
    }

    /// <summary>Per-game penalty for the candidate, one entry per hand played.</summary>
    public static int[] PenaltiesPerGame(IPlayer candidate, IPlayer opponent, int deals, int baseSeed)
    {
        var result = new int[deals * Dealer.Seats];
        int i = 0;

        for (int d = 0; d < deals; d++)
        {
            int seed = baseSeed + d;
            for (int seat = 0; seat < Dealer.Seats; seat++)
            {
                var hands = Dealer.Deal(seed);
                var players = new IPlayer[Dealer.Seats];
                for (int s = 0; s < Dealer.Seats; s++)
                    players[s] = s == seat ? candidate : opponent;

                var r = PlayHand(players, hands, Dealer.SeatHoldingThreeOfDiamonds(hands),
                                 openingLeadOfSeries: true, new DealRandom(seed * 4 + seat));
                result[i++] = r.Points[seat];
            }
        }
        return result;
    }

    /// <summary>
    /// Mean paired difference in penalty between two candidates over the SAME
    /// deals, with a 95% interval. Negative means <paramref name="a"/> scores
    /// lower, which is better.
    ///
    /// This is what the paired-deal design is actually for. Comparing two
    /// INDEPENDENT estimates throws that away: each carries the full variance of
    /// the deal, so a real edge of a fraction of a point is buried under an
    /// interval of plus or minus 0.3. Measured on this project's own tuner --
    /// comparing independent estimates, it accepted ZERO candidates in 25
    /// generations and returned its starting weights unchanged.
    ///
    /// Differencing per deal cancels the deal, which is the entire point.
    /// </summary>
    public static (double MeanDifference, double Low, double High) PairedPenaltyDifference(
        IPlayer a, IPlayer b, IPlayer opponent, int deals, int baseSeed, double z = 1.96)
    {
        var pa = PenaltiesPerGame(a, opponent, deals, baseSeed);
        var pb = PenaltiesPerGame(b, opponent, deals, baseSeed);

        int n = pa.Length;
        if (n == 0) return (0, 0, 0);

        double sum = 0, sumSq = 0;
        for (int i = 0; i < n; i++)
        {
            double d = pa[i] - pb[i];
            sum += d;
            sumSq += d * d;
        }

        double mean = sum / n;
        if (n == 1) return (mean, mean, mean);

        double variance = Math.Max(0, (sumSq - sum * sum / n) / (n - 1));
        double se = Math.Sqrt(variance / n);
        return (mean, mean - z * se, mean + z * se);
    }

    /// <summary>
    /// 95% interval on the MEAN penalty per hand.
    ///
    /// This is the metric that decides a series -- lowest TOTAL penalty wins,
    /// not most hands won -- and the two genuinely disagree. Measured on the
    /// first tuned candidate: it went from 23.8% to 34.6% of hands won against
    /// Greedy while its mean penalty got WORSE, 3.19 to 3.53. Better at winning
    /// hands, worse at winning series.
    ///
    /// A normal approximation on the mean, which is sound here because a match
    /// is thousands of hands.
    /// </summary>
    public static (double Mean, double Low, double High) MeanInterval(long sum, long sumSq, int n, double z = 1.96)
    {
        if (n == 0) return (0, 0, 0);
        double mean = (double)sum / n;
        if (n == 1) return (mean, mean, mean);

        double variance = Math.Max(0, ((double)sumSq - (double)sum * sum / n) / (n - 1));
        double se = Math.Sqrt(variance / n);
        return (mean, mean - z * se, mean + z * se);
    }

    /// <summary>
    /// Wilson score interval for a proportion, 95% by default. Chosen over the
    /// normal approximation because it stays sane at small n and near 0 or 1,
    /// which is exactly where a tuner's early candidates live.
    /// </summary>
    public static (double Low, double High) WilsonInterval(int successes, int n, double z = 1.96)
    {
        if (n == 0) return (0, 1);
        double p = (double)successes / n;
        double z2 = z * z;
        double denom = 1 + z2 / n;
        double centre = p + z2 / (2 * n);
        double margin = z * Math.Sqrt(p * (1 - p) / n + z2 / (4.0 * n * n));

        // The interval is mathematically inside [0, 1], but at 0 or n successes
        // the arithmetic lands a few ulps outside it (0/10 gives -1e-17).
        // Clamping restores the intended semantics rather than hiding anything.
        return (Math.Clamp((centre - margin) / denom, 0, 1),
                Math.Clamp((centre + margin) / denom, 0, 1));
    }
}

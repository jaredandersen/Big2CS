namespace Big2.Core;

/// <summary>One accepted step of the search.</summary>
public readonly record struct TuneStep(int Generation, double WinRate, double Low, double High, long GamesUsed);

/// <summary>
/// A (1 + lambda) evolution strategy over <see cref="PolicyWeights"/>, evaluated
/// through <see cref="SelfPlay"/>.
///
/// This is cappinmeow's method with its measurement fixed. Theirs evaluates each
/// candidate over TWELVE games, which cannot resolve a real edge in a game this
/// variable -- which is why their published weights are largely noise and their
/// reported best score means nothing.
///
/// Two things make this one able to tell:
///
///   PAIRED DEALS. Each deal is played four times with the candidate rotated
///   through all four seats, so deal luck cancels rather than being averaged.
///
///   A WILSON INTERVAL. A candidate is accepted only if its interval clears the
///   incumbent's win rate -- not merely if its mean looks better. Without that a
///   tuner spends its whole budget chasing noise and reports confident nonsense.
///
/// It is test-support and tooling: it does not ship. The RESULT ships, as a
/// plain array in PolicyWeights.
/// </summary>
public static class WeightTuner
{
    /// <summary>
    /// What the search optimises.
    ///
    /// <see cref="Points"/> is the default, and it is not a preference: a series
    /// is decided by the LOWEST TOTAL PENALTY, not by hands won, and the two
    /// disagree. The first run of this tuner optimised win rate and produced a
    /// policy that won 34.6% of hands against 23.8% while its mean penalty got
    /// worse -- better at winning hands, worse at winning series. Optimising the
    /// wrong objective is not visible in the objective's own number.
    /// </summary>
    public enum Objective { Points, WinRate }

    public sealed record Options
    {
        public Objective Optimise { get; init; } = Objective.Points;

        /// <summary>Candidates per generation, excluding the incumbent.</summary>
        public int Lambda { get; init; } = 8;

        /// <summary>Deals per evaluation. Each is four hands, so games = 4x this.</summary>
        public int Deals { get; init; } = 120;

        public int Generations { get; init; } = 20;

        /// <summary>Initial mutation size, as a fraction of each weight's scale.</summary>
        public double Sigma { get; init; } = 0.25;

        /// <summary>Sigma is multiplied by this each generation, so the search settles.</summary>
        public double SigmaDecay { get; init; } = 0.9;

        public int Seed { get; init; } = 20260831;

        /// <summary>
        /// The skills the policy being tuned has.
        ///
        /// This is what makes a difficulty level its own player rather than a
        /// crippled copy of the top one. Tuning under the FULL skill set and
        /// then switching a skill off at play time leaves weights that reference
        /// information the policy no longer receives -- a large SpendControl
        /// weight is meaningless once every holding reads as uncontrolled. Each
        /// level is searched with its own skills in place, so its numbers are
        /// the best available to a player who knows what it knows.
        /// </summary>
        public PolicySkills Skills { get; init; } = PolicySkills.Hard;

        /// <summary>Reported as the search runs, so a long run is not silent.</summary>
        public Action<string>? Log { get; init; }
    }

    /// <summary>
    /// Searches for weights that beat <paramref name="opponent"/> by more than
    /// the incumbent does. Returns the best found and the accepted steps.
    /// </summary>
    public static (PolicyWeights Best, IReadOnlyList<TuneStep> History) Run(
        PolicyWeights start, IPlayer opponent, Options? options = null)
    {
        var o = options ?? new Options();
        var rng = new DealRandom(o.Seed);
        var history = new List<TuneStep>();

        var best = start;
        var bestResult = Evaluate(best, opponent, o, generation: 0);
        long games = bestResult.Games;

        o.Log?.Invoke($"gen 00  incumbent {Describe(bestResult, o)}");
        history.Add(new TuneStep(0, bestResult.WinRate, bestResult.Low, bestResult.High, games));

        double sigma = o.Sigma;

        for (int gen = 1; gen <= o.Generations; gen++)
        {
            PolicyWeights? champion = null;
            (double Diff, double Low, double High) championGain = default;
            var incumbentPlayer = new HeuristicPlayer(best, "incumbent", o.Skills);

            double bestGain = 0;

            for (int c = 0; c < o.Lambda; c++)
            {
                var candidate = Mutate(best, sigma, rng);
                var player = new HeuristicPlayer(candidate, "candidate", o.Skills);

                // PAIRED against the incumbent on the SAME deals. Comparing two
                // independent estimates throws away the variance reduction the
                // paired deals exist for, and this tuner demonstrated the cost:
                // it accepted zero candidates in 25 generations that way.
                var (diff, low, high) = SelfPlay.PairedPenaltyDifference(
                    player, incumbentPlayer, opponent, o.Deals,
                    baseSeed: o.Seed + gen * 7919);

                games += 2L * o.Deals * Dealer.Seats;

                // Lower penalty is better, so an improvement is a NEGATIVE
                // difference whose interval excludes zero.
                bool improves = o.Optimise == Objective.Points
                    ? high < 0
                    : diff < 0 && high < 0;

                if (improves && diff < bestGain)
                {
                    bestGain = diff;
                    champion = candidate;
                    championGain = (diff, low, high);
                }
            }

            if (champion is not null)
            {
                best = champion;
                history.Add(new TuneStep(gen, championGain.Diff, championGain.Low, championGain.High, games));
                o.Log?.Invoke($"gen {gen:00}  ACCEPT   penalty {championGain.Diff:+0.000;-0.000} per hand " +
                              $"[{championGain.Low:+0.000;-0.000}, {championGain.High:+0.000;-0.000}]  sigma {sigma:F3}");
            }
            else
            {
                o.Log?.Invoke($"gen {gen:00}  --       none of {o.Lambda} improved  sigma {sigma:F3}");
            }

            sigma *= o.SigmaDecay;
        }

        return (best, history);
    }

    /// <summary>Lower penalty is better; higher win rate is better.</summary>
    private static bool Clears(MatchResult candidate, MatchResult incumbent, Objective objective) =>
        objective == Objective.Points
            ? candidate.PointsHigh < incumbent.MeanPoints
            : candidate.Low > incumbent.WinRate;

    private static bool Better(MatchResult a, MatchResult b, Objective objective) =>
        objective == Objective.Points ? a.MeanPoints < b.MeanPoints : a.WinRate > b.WinRate;

    private static string Describe(MatchResult r, Options o) =>
        o.Optimise == Objective.Points
            ? $"points {r.MeanPoints:F2} [{r.PointsLow:F2}, {r.PointsHigh:F2}]  wins {r.WinRate:P1}"
            : $"wins {r.WinRate:P1} [{r.Low:P1}, {r.High:P1}]  points {r.MeanPoints:F2}";

    private static MatchResult Evaluate(PolicyWeights w, IPlayer opponent, Options o, int generation)
    {
        var player = new HeuristicPlayer(w, "candidate", o.Skills);

        // A DIFFERENT base seed per generation, so the search cannot overfit one
        // fixed set of deals -- which would be the tuning equivalent of a test
        // that always passes.
        return SelfPlay.Match(player, opponent, o.Deals, baseSeed: o.Seed + generation * 7919);
    }

    private static PolicyWeights Mutate(PolicyWeights parent, double sigma, DealRandom rng)
    {
        var w = parent.ToArray();
        for (int i = 0; i < w.Length; i++)
        {
            // Scale the step to the weight, with a floor so a weight that has
            // collapsed to zero can still move.
            double scale = Math.Max(Math.Abs(w[i]), 10.0) * sigma;
            w[i] = Math.Round(w[i] + Gaussian(rng) * scale, 3);
        }
        return new PolicyWeights(w);
    }

    /// <summary>Box-Muller, using the deal RNG so a run is reproducible from its seed.</summary>
    private static double Gaussian(DealRandom rng)
    {
        double u1 = (rng.Next(1_000_000) + 1) / 1_000_001.0;
        double u2 = (rng.Next(1_000_000) + 1) / 1_000_001.0;
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    /// <summary>Formats weights as a C# array literal, ready to paste into PolicyWeights.</summary>
    public static string ToSourceLiteral(PolicyWeights w)
    {
        var names = Enum.GetNames<Feature>();
        var values = w.ToArray();
        var lines = values.Select((v, i) =>
            $"        {v.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture),8},".PadRight(20) +
            $"// {names[i]}");
        return "new[]\n    {\n" + string.Join("\n", lines) + "\n    }";
    }
}

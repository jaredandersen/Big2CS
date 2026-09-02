namespace Big2.Core;

/// <summary>
/// The named features a play is scored on. The order is the order of the weight
/// vector, so this enum IS the schema -- adding one in the middle invalidates
/// any tuned weights, which is why <see cref="PolicyWeights.Count"/> is asserted.
///
/// The vocabulary starts from cappinmeow's, which is the only self-play-tuned
/// policy among the references and therefore the only evidence about which
/// features matter. Its trained 4-player weights, for reference:
///
///     finish_round 152.7 · block_opponent 73.2 · keep_two_early 53.2
///     high_power_when_danger 32.1 · avoid_break_triple 30.3
///     avoid_break_pair 28.5 · play_more_cards 27.0 · five_card_bonus 20.0
///     keep_high_card_early 17.6 · empty_table_combo_bonus 15.4 · low_power 12.9
///
/// Going out dominates; blocking outweighs every card-preservation term. Those
/// magnitudes are a sanity check on ours, NOT values to copy -- they were tuned
/// against twelve games per candidate, which cannot resolve a real edge.
///
/// Two features are ours and have no counterpart in any reference: SpendControl
/// and KeepControl, both fed by <see cref="ControlClasses"/>.
/// </summary>
public enum Feature
{
    /// <summary>Cards shed. Emptying the hand is the only way to win.</summary>
    CardsShed,

    /// <summary>Strength of the play, 0..1 WITHIN ITS OWN PLAY TYPE. See PolicyWeights.</summary>
    Power,

    /// <summary>This play empties the hand.</summary>
    Finish,

    /// <summary>Strength, but only when an opponent is close to going out.</summary>
    PowerWhenDanger,

    /// <summary>Any play at all while an opponent is close to going out.</summary>
    BlockOpponent,

    /// <summary>Spending a two while the hand is still long.</summary>
    SpendTwoEarly,

    /// <summary>Spending an ace or king while the hand is still long.</summary>
    SpendHighEarly,

    /// <summary>Cards taken out of a pair that is left broken.</summary>
    BreakPair,

    /// <summary>Cards taken out of a triple that is left broken.</summary>
    BreakTriple,

    /// <summary>A five-card play, which sheds the most at once.</summary>
    FiveCard,

    /// <summary>A multi-card play when leading a fresh trick.</summary>
    LeadCombo,

    /// <summary>Spending a holding nothing unseen can beat.</summary>
    SpendControl,

    /// <summary>Playing something that cannot be beaten, when it will actually take the trick.</summary>
    KeepControl,

    /// <summary>
    /// The value of passing, scored as a competing option rather than compared
    /// against an arbitrary cutoff.
    ///
    /// The first version tested the best play's score against a literal 0.0, on
    /// a scale whose ORIGIN is meaningless -- so whether the policy passed
    /// depended on where the weights happened to put zero. Measured: it passed
    /// 57% of the time against Greedy's 42%, all of the excess voluntary.
    ///
    /// Making it a feature puts the origin under the tuner's control, which is
    /// where it belongs.
    /// </summary>
    PassBias,
}

/// <summary>
/// Weights for <see cref="Feature"/>, one double each.
///
/// Ships as a plain array in the source -- no JSON file, no runtime dependency,
/// nothing to lose. cappinmeow ships a 590-byte JSON, which is fine for a Python
/// script and pointless for a single-file executable.
/// </summary>
public sealed class PolicyWeights
{
    public static readonly int Count = Enum.GetValues<Feature>().Length;

    private readonly double[] _w;

    public PolicyWeights(double[] weights)
    {
        if (weights.Length != Count)
            throw new ArgumentException($"expected {Count} weights, got {weights.Length}", nameof(weights));
        _w = weights;
    }

    public double this[Feature f] => _w[(int)f];

    public double[] ToArray() => (double[])_w.Clone();

    /// <summary>
    /// The hand-written starting point, before any tuning. Kept as the tuner's
    /// seed and as the control the tuned weights are measured against.
    ///
    /// Magnitudes follow cappinmeow's relative ordering, which is the only
    /// evidence available: finishing dominates, blocking outweighs preservation.
    /// </summary>
    public static PolicyWeights HandWritten { get; } = new(new[]
    {
        27.0,    // CardsShed
        -13.0,   // Power            (negative: do not overpay)
        150.0,   // Finish
        32.0,    // PowerWhenDanger
        73.0,    // BlockOpponent
        -53.0,   // SpendTwoEarly
        -18.0,   // SpendHighEarly
        -28.0,   // BreakPair
        -30.0,   // BreakTriple
        20.0,    // FiveCard
        15.0,    // LeadCombo
        -40.0,   // SpendControl
        45.0,    // KeepControl
        -25.0,   // PassBias
    });

    /// <summary>
    /// What ships. Found by <see cref="WeightTuner"/>, seeded from
    /// <see cref="HandWritten"/>, optimising mean penalty against Greedy over
    /// paired deals.
    ///
    /// MEASURED ON INDEPENDENT DEALS, not the tuner's own -- 400 deals, 1600
    /// hands, seeds unrelated to the search:
    ///
    ///   vs Greedy       24.3% -> 31.8% of hands won   (intervals disjoint)
    ///   vs LowestLegal  43.8% -> 54.4%                (disjoint)
    ///   vs Random       73.4% -> 78.6%                (disjoint)
    ///   mean penalty    3.44 -> 3.38, 1.86 -> 1.67, 0.57 -> 0.48
    ///
    /// HONEST LIMIT: the penalty improvement the tuner optimised did NOT
    /// replicate at that sample size -- paired on fresh deals it is -0.068 per
    /// hand with an interval of [-0.230, +0.094], straddling zero. Penalty is
    /// far higher variance than win rate (one bad hand costs 50+), so resolving
    /// a small effect in it needs roughly an order of magnitude more games.
    /// The weights are adopted on the win-rate evidence, which is unambiguous,
    /// and on penalty moving the right way in all three matchups.
    /// </summary>
    public static PolicyWeights Default { get; } = new(new[]
    {
        21.456,   // CardsShed
        -15.962,  // Power
        130.868,  // Finish
        24.810,   // PowerWhenDanger
        90.443,   // BlockOpponent
        -65.985,  // SpendTwoEarly
        -22.706,  // SpendHighEarly
        -25.595,  // BreakPair
        -32.901,  // BreakTriple
        23.077,   // FiveCard
        17.076,   // LeadCombo
        -51.861,  // SpendControl
        49.999,   // KeepControl
        -24.168,  // PassBias
    });

    /// <summary>
    /// Tuned for <see cref="PolicySkills.Easy"/>, by the same search, reference
    /// opponent and seed as <see cref="Default"/> with only the skills differing:
    /// 18 generations, 150 paired deals per candidate, 3 accepted steps from an
    /// incumbent at 7.64 penalty.
    ///
    /// It is NOT <see cref="Default"/> with terms deleted. Given its own search,
    /// a policy that only ever sees its two cheapest plays moves toward getting
    /// value out of them -- Power -16.0 -> -8.7, Finish 130.9 -> 114.7.
    /// </summary>
    public static PolicyWeights Easy { get; } = new(new[]
    {
        27.16,    // CardsShed
        -8.714,   // Power
        114.653,  // Finish
        26.371,   // PowerWhenDanger
        100.101,  // BlockOpponent
        -37.826,  // SpendTwoEarly
        -11.547,  // SpendHighEarly
        -29.364,  // BreakPair
        -25.339,  // BreakTriple
        20.934,   // FiveCard
        33.439,   // LeadCombo
        -45.204,  // SpendControl
        61.181,   // KeepControl
        -51.969,  // PassBias
    });

    /// <summary>
    /// Tuned for <see cref="PolicySkills.Normal"/>. Same search, same opponent,
    /// same seed; 5 accepted steps from an incumbent at 5.12 penalty.
    /// </summary>
    public static PolicyWeights Normal { get; } = new(new[]
    {
        27.095,   // CardsShed
        -5.548,   // Power
        102.818,  // Finish
        28.017,   // PowerWhenDanger
        89.569,   // BlockOpponent
        -29.316,  // SpendTwoEarly
        -12.514,  // SpendHighEarly
        -26.316,  // BreakPair
        -26.886,  // BreakTriple
        13.085,   // FiveCard
        19.334,   // LeadCombo
        -44.579,  // SpendControl
        41.919,   // KeepControl
        -40.552,  // PassBias
    });

    /// <summary>
    /// Weights for a difficulty. Each set is tuned SEPARATELY against the same
    /// reference opponent with only the skills differing, so each level plays
    /// its own best game rather than reusing one tuning three times.
    ///
    /// The ladder that results, 800 paired deals against Greedy, each level with
    /// its own skills AND its own weights:
    ///
    ///     Hard    penalty 3.30 [3.04, 3.56]   wins 31.1% [29.5, 32.7]
    ///     Normal  penalty 3.97 [3.73, 4.21]   wins 26.9% [25.4, 28.5]
    ///     Easy    penalty 4.76 [4.49, 5.02]   wins 19.8% [18.4, 21.2]
    ///
    /// Both adjacent pairs are disjoint on both metrics. That the two metrics
    /// agree here is worth noting, because for most of this design they did not.
    /// </summary>
    public static PolicyWeights For(Difficulty d) => d switch
    {
        Difficulty.Easy => Easy,
        Difficulty.Normal => Normal,
        _ => Default,
    };
}

/// <summary>
/// What a policy is able to notice. This is what difficulty is made of.
///
/// Difficulty is expressed by REMOVING A NAMED ABILITY, not by adding noise.
/// kyleliao's three tiers differ by how often they pass at random -- 30%, 15%,
/// 10% -- which does not make an opponent easier so much as erratic, and it is
/// visible at the table as a player throwing away winnable tricks for no reason.
/// An opponent that notices an endgame too late is weaker in a way you can
/// describe, and in a way that reads as a plausible human failing.
///
/// THE LADDER HANGS ON ONE AXIS, AND THAT IS A MEASUREMENT, NOT A SIMPLIFICATION.
/// The first design ablated two skills, card counting and opponent watching. The
/// full 2x2 over 800 paired deals against Greedy:
///
///     count + watch   penalty 3.30 [3.04, 3.56]
///     count only      penalty 7.82 [7.14, 8.49]
///     watch only      penalty 3.29 [3.06, 3.52]
///     neither         penalty 7.65 [6.98, 8.33]
///
/// Watching decides everything; counting decides nothing, in either condition,
/// with fully overlapping intervals. Two binary skills where only one carries
/// weight can only make two distinct players, so the surviving axis is graded
/// instead. <see cref="ControlClasses"/> is still computed and still used -- it
/// is simply not what makes these opponents strong, and no level is sold on it.
///
/// Note the effect is invisible in WIN RATE, which is flat at ~31% across all
/// four cells. A series is decided by the lowest total penalty, and the two
/// numbers disagree -- the same trap recorded on <see cref="WeightTuner.Objective"/>.
/// </summary>
/// <param name="DangerHorizon">
/// How few cards an opponent must be down to before this policy reacts at all.
/// 0 means never. 5 is the full graded term, and reproduces the behaviour the
/// shipping weights were tuned against exactly.
/// </param>
/// <param name="CountsCards">
/// Whether the policy is told the play history. A policy without it genuinely
/// believes every card it does not hold is still out there, which is what a
/// player who is not paying attention actually believes.
/// </param>
/// <param name="CandidateLimit">
/// How many candidate plays the policy looks at, taken from the cheapest end --
/// fewest cards first, lowest first within that. It never sees the rest.
///
/// THIS IS THE AXIS THAT MAKES THE LADDER HOLD, and it is here because the two
/// signal-based ones did not. Withholding a *signal* only handicaps a policy
/// whose weights are frozen: retuned with its own skills in place, the
/// horizon-2 policy came back at 3.21 penalty against the full policy's 3.30 --
/// indistinguishable, and nominally better. A weight search simply compensates
/// for information it no longer receives.
///
/// A candidate limit cannot be compensated for, because a play that is never
/// scored can never be chosen no matter what the weights say. The failure it
/// produces is also the recognisable one: a weak player dumps singles and misses
/// the big shed, which is exactly nickmqb's bare-single failure mode.
///
/// THE VALUES ARE CHOSEN FROM WHERE THE LIMIT ACTUALLY BINDS, not by taste. The
/// first attempt used 5 and 20, and 20 turned out to bind at 0.8% of positions --
/// so the middle level was untouched and its measured penalty came back identical
/// to three significant figures, which is what gave the no-op away. Measured over
/// 7,916 positions, the mean number of legal plays is 4.4 and the median lead
/// offers about 5, so the useful range is small:
///
///     limit 1 binds 71.4% of positions (90.2% of leads)
///     limit 2 binds 55.6%              (79.4%)
///     limit 5 binds 28.8%              (49.7%)
///     limit 8 binds 15.1%              (28.3%)
/// </param>
public readonly record struct PolicySkills(int DangerHorizon, bool CountsCards, int CandidateLimit)
{
    /// <summary>Sees two candidate plays, and never an endgame coming.</summary>
    public static readonly PolicySkills Easy = new(0, false, 2);

    /// <summary>Sees five, and notices an opponent only on a single card.</summary>
    public static readonly PolicySkills Normal = new(2, true, 5);

    /// <summary>Every legal play, and the full graded danger term.</summary>
    public static readonly PolicySkills Hard = new(5, true, int.MaxValue);

    public static PolicySkills For(Difficulty d) => d switch
    {
        Difficulty.Easy => Easy,
        Difficulty.Normal => Normal,
        _ => Hard,
    };

    /// <summary>
    /// The 0..1 danger this policy reads from an opponent's card count. At
    /// <see cref="Hard"/>'s horizon of 5 this is (5 - threat) / 4, which is the
    /// original expression -- the grading generalises it without moving it.
    /// </summary>
    public double DangerFrom(int threat)
    {
        if (DangerHorizon < 2 || threat >= DangerHorizon) return 0;
        return (DangerHorizon - threat) / (double)(DangerHorizon - 1);
    }

    /// <summary>
    /// At or below how many opponent cards this policy refuses to pass. Capped
    /// at 2 so Hard keeps the exact threshold it was tuned with.
    /// </summary>
    public int NeverPassBelow => Math.Min(2, DangerHorizon - 1);
}

public enum Difficulty { Easy, Normal, Hard }

/// <summary>
/// A weighted linear policy over hand-written features.
///
/// This supersedes "hand-written heuristic": the same amount of hand-written
/// code, but the magic numbers get MEASURED by <see cref="SelfPlay"/> rather
/// than guessed. That is cappinmeow's design, done with a sample size that can
/// actually resolve a difference.
///
/// Four failures in the references it deliberately does not repeat:
///
///   * nickmqb leads a bare single every time (its five-card branch is
///     literally <c>// TODO</c>);
///   * kyleliao passes blindly 10-30% of the time by difficulty;
///   * cappinmeow's opponent danger is a flat binary at three cards, so a seat
///     on one card reads the same as one on three;
///   * and none of them counts cards, so none can tell a king that wins the
///     trick from a king that loses it.
/// </summary>
public sealed class HeuristicPlayer : IPlayer
{
    private readonly PolicyWeights _w;
    private readonly PolicySkills _skills;

    public HeuristicPlayer(PolicyWeights? weights = null, string name = "Heuristic",
                           PolicySkills? skills = null)
    {
        _w = weights ?? PolicyWeights.Default;
        _skills = skills ?? PolicySkills.Hard;
        Name = name;
    }

    /// <summary>The opponent for a difficulty setting: its own skills, its own tuned weights.</summary>
    public static HeuristicPlayer For(Difficulty d) =>
        new(PolicyWeights.For(d), d.ToString(), PolicySkills.For(d));

    public string Name { get; }

    /// <summary>Why the last move was a pass. Recorded so a pass can be shown to be reasoned.</summary>
    public PassReason LastPassReason { get; private set; } = PassReason.None;

    public enum PassReason
    {
        None,
        /// <summary>Nothing in hand beats the table. The only forced pass.</summary>
        NoLegalPlay,
        /// <summary>Every legal answer costs more than the trick is worth.</summary>
        TooExpensive,
    }

    public int[]? ChooseMove(Big2Game game, DealRandom rng)
    {
        int seat = game.Turn;
        var hand = game.Hand(seat);

        // Going out ends the hand, so nothing else can be worth more. Asked
        // before any scoring, as cappinmeow does.
        if (MoveGenerator.FindFinishingPlay(hand, game.Table) is { } finish)
        {
            LastPassReason = PassReason.None;
            return finish;
        }

        var moves = game.LegalPlays();
        if (moves.Count == 0)
        {
            LastPassReason = PassReason.NoLegalPlay;
            return null;
        }

        moves = Narrow(moves, _skills.CandidateLimit);

        // A policy that does not count cards is given an EMPTY history rather
        // than a disabled code path: it then genuinely believes every card it
        // does not hold is still out there, which is what a player who is not
        // paying attention actually believes.
        var control = new ControlClasses(hand,
            _skills.CountsCards ? game.PlayHistory : Array.Empty<int>());

        int threat = MinOpponentCards(game, seat);

        int[]? best = null;
        double bestScore = double.NegativeInfinity;

        foreach (var move in moves)
        {
            double score = Score(game, hand, control, threat, move);
            if (score > bestScore) { bestScore = score; best = move; }
        }

        // Passing competes on the same scale as a play. kyleliao's coin-flip
        // pass is noise and is visible at the table; this one always traces to
        // "every answer cost more than the trick was worth".
        if (game.CanPass && PassValue(threat) > bestScore)
        {
            LastPassReason = PassReason.TooExpensive;
            return null;
        }

        LastPassReason = PassReason.None;
        return best;
    }

    /// <summary>
    /// What passing is worth. Blocking an opponent who is about to go out beats
    /// any economy, so the option is withdrawn entirely when one is close.
    /// </summary>
    /// <summary>
    /// The cheapest <paramref name="limit"/> plays: fewest cards first, then
    /// lowest top card. Always leaves at least one, so a policy with a limit can
    /// still always answer when it has a legal answer.
    /// </summary>
    private static List<int[]> Narrow(List<int[]> moves, int limit)
    {
        if (limit >= moves.Count) return moves;

        var ordered = new List<int[]>(moves);
        ordered.Sort((a, b) =>
        {
            int c = a.Length.CompareTo(b.Length);
            if (c != 0) return c;
            return Top(a).CompareTo(Top(b));
        });
        return ordered.GetRange(0, Math.Max(1, limit));
    }

    private static int Top(int[] move)
    {
        int top = -1;
        foreach (int id in move) top = Math.Max(top, Cards.OrderKey(id));
        return top;
    }

    private double PassValue(int threat)
    {
        if (threat <= _skills.NeverPassBelow) return double.NegativeInfinity;
        return _w[Feature.PassBias];
    }

    /// <summary>
    /// Weights per difficulty. Each set was found by the same tuner against the
    /// same reference opponent, with only the SKILLS differing -- so the numbers
    /// describe the best each level can do with what it has, rather than one
    /// tuning being reused three times.
    /// </summary>
    public static PolicyWeights WeightsFor(Difficulty d) => PolicyWeights.For(d);

    private static int MinOpponentCards(Big2Game game, int seat)
    {
        int min = int.MaxValue;
        for (int s = 0; s < Dealer.Seats; s++)
            if (s != seat) min = Math.Min(min, game.CardsLeft(s));
        return min;
    }

    private double Score(Big2Game game, IReadOnlyList<int> hand, ControlClasses control,
                         int threat, int[] move)
    {
        var combo = Combinations.Parse(move)!.Value;
        var f = new double[PolicyWeights.Count];

        f[(int)Feature.CardsShed] = move.Length;
        f[(int)Feature.Power] = NormalisedPower(combo);
        f[(int)Feature.Finish] = move.Length == hand.Count ? 1 : 0;
        f[(int)Feature.FiveCard] = move.Length == 5 ? 1 : 0;
        f[(int)Feature.LeadCombo] = game.Table is null && move.Length > 1 ? 1 : 0;

        // GRADED, not cappinmeow's flat "<= 3" binary: a seat on one card is a
        // very different problem from one on three.
        double danger = _skills.DangerFrom(threat);
        f[(int)Feature.PowerWhenDanger] = danger * NormalisedPower(combo);
        f[(int)Feature.BlockOpponent] = danger;

        int remaining = hand.Count - move.Length;
        bool early = remaining > 6;
        foreach (int c in move)
        {
            if (early && Cards.RankOf(c) == 12) f[(int)Feature.SpendTwoEarly] += 1;
            if (early && Cards.RankOf(c) is 10 or 11) f[(int)Feature.SpendHighEarly] += 1;
        }

        var (breakPair, breakTriple) = BreakCost(hand, move);
        f[(int)Feature.BreakPair] = breakPair;
        f[(int)Feature.BreakTriple] = breakTriple;

        // Control: spending an unbeatable holding costs, UNLESS it actually
        // takes the trick -- which is what it is for.
        if (control.IsUnbeatable(combo))
        {
            f[(int)Feature.SpendControl] = 1;
            if (game.Table is not null || danger > 0) f[(int)Feature.KeepControl] = 1;
        }

        double score = 0;
        for (int i = 0; i < f.Length; i++) score += f[i] * _w[(Feature)i];
        return score;
    }

    /// <summary>
    /// Strength as a 0..1 position WITHIN THE PLAY'S OWN TYPE.
    ///
    /// cappinmeow folds the sort-key tuple as <c>total*20 + value</c> and divides
    /// by 100, so a five-card play's power lands on a completely different scale
    /// from a single's -- yet the same weight multiplies both, which means that
    /// weight means different things in different situations. Normalising per
    /// type is what makes one weight mean one thing.
    /// </summary>
    public static double NormalisedPower(Combination combo) => combo.Count switch
    {
        1 or 2 or 3 => combo.Key / (double)(Cards.Count - 1),
        5 => ((int)combo.Kind - (int)PlayKind.Straight) / 4.0,
        _ => 0,
    };

    /// <summary>
    /// How many cards this play strands. Playing a WHOLE group is not breaking
    /// it -- cappinmeow gets this right and it is easy to get wrong.
    /// </summary>
    private static (int Pair, int Triple) BreakCost(IReadOnlyList<int> hand, int[] move)
    {
        Span<int> inHand = stackalloc int[Cards.RankCount];
        Span<int> inMove = stackalloc int[Cards.RankCount];
        foreach (int c in hand) inHand[Cards.RankOf(c)]++;
        foreach (int c in move) inMove[Cards.RankOf(c)]++;

        int pair = 0, triple = 0;
        for (int r = 0; r < Cards.RankCount; r++)
        {
            int used = inMove[r];
            if (used == 0 || used >= inHand[r]) continue;   // whole group played: not a break

            if (inHand[r] == 3) triple += used;
            else if (inHand[r] == 2) pair += used;
            else if (inHand[r] == 4) triple += used;
        }
        return (pair, triple);
    }
}

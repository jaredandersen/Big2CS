namespace Big2.Core;

/// <summary>
/// What a seat can still be beaten by.
///
/// This is the abstraction the whole policy rests on, taken from
/// Jennifer-Lion's <c>cardClassifier</c> -- the one idea no other reference
/// implementation has. Given what has been played and what we hold, every
/// remaining card is either seen or unseen; a holding that no unseen card can
/// beat is CONTROL, and control is what lets a pass be reasoned rather than
/// guessed.
///
/// cappinmeow's policy has no equivalent: it never looks at the play history, so
/// it cannot tell a king that wins the trick from a king that loses it. That is
/// the single largest gap in the references.
///
/// "Unseen" means neither played nor in our own hand. It is shared among the
/// three opponents, so this is a bound on what CAN beat us, not a read on any
/// particular seat.
/// </summary>
public sealed class ControlClasses
{
    private readonly bool[] _unseen = new bool[Cards.Count];

    /// <summary>Unseen cards per Big 2 rank position (0 = three .. 12 = two).</summary>
    private readonly int[] _unseenByRank = new int[Cards.RankCount];

    /// <summary>Unseen cards per suit rank (0 = diamonds .. 3 = spades).</summary>
    private readonly int[] _unseenBySuit = new int[Cards.SuitCount];

    /// <summary>Unseen rank positions present in each suit, as a bitmask.</summary>
    private readonly int[] _unseenSuitRankMask = new int[Cards.SuitCount];

    public int UnseenCount { get; }

    public ControlClasses(IReadOnlyList<int> hand, IReadOnlyList<int> playHistory)
    {
        var seen = new bool[Cards.Count];
        foreach (int c in hand) seen[c] = true;
        foreach (int c in playHistory) seen[c] = true;

        for (int c = 0; c < Cards.Count; c++)
        {
            if (seen[c]) continue;
            _unseen[c] = true;
            UnseenCount++;
            _unseenByRank[Cards.RankOf(c)]++;
            int suit = Cards.SuitRank(Cards.SuitOf(c));
            _unseenBySuit[suit]++;
            _unseenSuitRankMask[suit] |= 1 << Cards.RankOf(c);
        }
    }

    /// <summary>How many unseen cards outrank this one.</summary>
    public int UnseenAbove(int cardId)
    {
        int key = Cards.OrderKey(cardId);
        int n = 0;
        for (int c = 0; c < Cards.Count; c++)
            if (_unseen[c] && Cards.OrderKey(c) > key) n++;
        return n;
    }

    /// <summary>Nothing unseen beats this card: playing it wins the trick outright.</summary>
    public bool IsTopSingle(int cardId) => UnseenAbove(cardId) == 0;

    /// <summary>No unseen rank above this one has enough cards left to form a set of the same size.</summary>
    public bool IsTopSet(int rankPosition, int size)
    {
        for (int r = rankPosition + 1; r < Cards.RankCount; r++)
            if (_unseenByRank[r] >= size) return false;
        return true;
    }

    /// <summary>
    /// Whether <paramref name="play"/> cannot be beaten by anything still
    /// unseen. Five-card hands are answered by category feasibility rather than
    /// by enumerating subsets -- C(39,5) is 575,757 and this is called for every
    /// candidate move.
    /// </summary>
    public bool IsUnbeatable(Combination play) => play.Count switch
    {
        1 => IsTopSingle(FromKey(play.Key)),
        2 => IsTopSet(Cards.RankOf(FromKey(play.Key)), 2),
        3 => IsTopSet(Cards.RankOf(FromKey(play.Key)), 3),
        5 => BestUnseenFiveCard() is { } best && !best.Beats(play),
        _ => false,
    };

    /// <summary>
    /// The strongest five-card hand the unseen cards could form, or null if they
    /// cannot form one at all. Approximate in its KEY (it assumes the best
    /// available), exact in its CATEGORY.
    /// </summary>
    public Combination? BestUnseenFiveCard()
    {
        // Straight flush, highest first.
        for (int suit = Cards.SuitCount - 1; suit >= 0; suit--)
        {
            int best = BestStraightTop(_unseenSuitRankMask[suit]);
            if (best >= 0) return new Combination(PlayKind.StraightFlush, 5, best * 4 + suit);
        }

        for (int r = Cards.RankCount - 1; r >= 0; r--)
            if (_unseenByRank[r] == 4 && UnseenCount >= 5)
                return new Combination(PlayKind.Quads, 5, r);

        for (int r = Cards.RankCount - 1; r >= 0; r--)
        {
            if (_unseenByRank[r] < 3) continue;
            for (int p = Cards.RankCount - 1; p >= 0; p--)
                if (p != r && _unseenByRank[p] >= 2)
                    return new Combination(PlayKind.FullHouse, 5, r);
        }

        for (int suit = Cards.SuitCount - 1; suit >= 0; suit--)
        {
            if (_unseenBySuit[suit] < 5) continue;
            int top = HighestSetBit(_unseenSuitRankMask[suit]);
            return new Combination(PlayKind.Flush, 5, top * 4 + suit);
        }

        int anyRankMask = 0;
        for (int r = 0; r < Cards.RankCount; r++)
            if (_unseenByRank[r] > 0) anyRankMask |= 1 << r;

        int straightTop = BestStraightTop(anyRankMask);
        if (straightTop >= 0)
            return new Combination(PlayKind.Straight, 5, straightTop * 4 + 3);

        return null;
    }

    /// <summary>
    /// Highest rank position completing one of the ten legal straight shapes
    /// within <paramref name="rankMask"/>, or -1. Uses the same enumerated
    /// shapes as <see cref="Combinations"/> so the two cannot disagree about
    /// whether J-Q-K-A-2 is a straight.
    /// </summary>
    private static int BestStraightTop(int rankMask)
    {
        int best = -1;
        foreach (int shape in Combinations.StraightMasks)
        {
            if ((rankMask & shape) != shape) continue;
            int top = HighestSetBit(shape);
            if (top > best) best = top;
        }
        return best;
    }

    private static int HighestSetBit(int mask)
    {
        for (int b = Cards.RankCount - 1; b >= 0; b--)
            if ((mask & (1 << b)) != 0) return b;
        return -1;
    }

    /// <summary>A combination's key is its top card's order key, so this recovers the card.</summary>
    private static int FromKey(int orderKey)
    {
        for (int c = 0; c < Cards.Count; c++)
            if (Cards.OrderKey(c) == orderKey) return c;
        return Cards.Empty;
    }
}

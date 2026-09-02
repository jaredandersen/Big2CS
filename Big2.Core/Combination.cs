namespace Big2.Core;

/// <summary>
/// The kinds of play, ordered so that a larger value beats a smaller one among
/// five-card hands. Single/Pair/Triple never compare against a five-card hand
/// because the card counts differ.
/// </summary>
public enum PlayKind
{
    Single,
    Pair,
    Triple,
    Straight,
    Flush,
    FullHouse,
    Quads,
    StraightFlush,
}

/// <summary>
/// A parsed, legal play: what kind it is and the single integer that ranks it
/// against another play of the same kind.
/// </summary>
public readonly record struct Combination(PlayKind Kind, int Count, int Key)
{
    /// <summary>Five-card hands compare by category first, then by key.</summary>
    public bool IsFiveCard => Count == 5;

    /// <summary>
    /// Whether this beats <paramref name="other"/>. Card counts must match --
    /// there are no bombs, so quads and straight flushes do NOT override the
    /// count.
    /// </summary>
    public bool Beats(Combination other)
    {
        if (Count != other.Count) return false;
        if (Kind != other.Kind) return Kind > other.Kind;
        return Key > other.Key;
    }
}

/// <summary>
/// Parses a set of cards into a <see cref="Combination"/> under TEGL 1990's
/// rules, or reports that it is not a legal play.
///
/// Verified against the DOS corpus: all 4,658 logged plays classify, and the
/// perturbation controls fire (removing the wraparound straights makes 21 of
/// them illegal; a suit-first flush rule produces 20 comparison disagreements).
/// </summary>
public static class Combinations
{
    /// <summary>
    /// TEGL's straight shapes, as sets of Big 2 rank positions (0 = three,
    /// 12 = two).
    ///
    /// EIGHT ordinary runs, 3-4-5-6-7 through 10-J-Q-K-A, PLUS TWO wraparounds:
    /// A-2-3-4-5 and 2-3-4-5-6. A wraparound tops at the two and therefore
    /// outranks a royal flush, exactly as big2.doc states.
    ///
    /// J-Q-K-A-2 is NOT a straight, even though it is "consecutive" in Big 2's
    /// own 3..2 rank order -- zero occurrences in the corpus's 765 five-card
    /// plays. That is precisely why these are ENUMERATED and never computed:
    /// any arithmetic rule over the rank order admits J-Q-K-A-2 by accident.
    /// </summary>
    /// <summary>
    /// Exposed so <see cref="ControlClasses"/> can reason about what straights
    /// the unseen cards could form. Two independent copies of this set would be
    /// free to disagree about whether J-Q-K-A-2 is a straight, which is exactly
    /// the rule most likely to drift.
    /// </summary>
    public static readonly IReadOnlySet<int> StraightMasks = BuildStraightMasks();

    private static IReadOnlySet<int> BuildStraightMasks()
    {
        var masks = new HashSet<int>();

        // The eight ordinary runs. Rank positions 0..11 are three..ace, so the
        // last legal run starts at 7 (ten) and ends at 11 (ace).
        for (int start = 0; start <= 7; start++)
        {
            int m = 0;
            for (int i = 0; i < 5; i++) m |= 1 << (start + i);
            masks.Add(m);
        }

        masks.Add(MaskOf(11, 12, 0, 1, 2));   // A-2-3-4-5
        masks.Add(MaskOf(12, 0, 1, 2, 3));    // 2-3-4-5-6
        return masks;
    }

    private static int MaskOf(params int[] rankPositions)
    {
        int m = 0;
        foreach (int r in rankPositions) m |= 1 << r;
        return m;
    }

    /// <summary>
    /// Parses <paramref name="cardIds"/> into a combination, or returns null if
    /// they are not a legal play. Order of the input does not matter.
    /// </summary>
    public static Combination? Parse(IReadOnlyList<int> cardIds)
    {
        int n = cardIds.Count;
        if (n is not (1 or 2 or 3 or 5)) return null;

        // Reject duplicates -- a caller handing us the same card twice is a bug
        // we would otherwise turn into a plausible-looking pair.
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                if (cardIds[i] == cardIds[j]) return null;

        int topKey = int.MinValue;
        int rankMask = 0;
        Span<int> perRank = stackalloc int[Cards.RankCount];
        Suit firstSuit = Cards.SuitOf(cardIds[0]);
        bool oneSuit = true;

        foreach (int id in cardIds)
        {
            int key = Cards.OrderKey(id);
            if (key > topKey) topKey = key;
            int rank = Cards.RankOf(id);
            rankMask |= 1 << rank;
            perRank[rank]++;
            if (Cards.SuitOf(id) != firstSuit) oneSuit = false;
        }

        switch (n)
        {
            case 1:
                return new Combination(PlayKind.Single, 1, topKey);

            case 2:
                return perRank[Cards.RankOf(cardIds[0])] == 2
                    ? new Combination(PlayKind.Pair, 2, topKey)
                    : null;

            case 3:
                return perRank[Cards.RankOf(cardIds[0])] == 3
                    ? new Combination(PlayKind.Triple, 3, topKey)
                    : null;
        }

        // Five cards. Shape first, because quads and full houses are decided by
        // their triple/quad rank rather than by the top card.
        int quadRank = -1, tripleRank = -1, pairCount = 0;
        int distinctRanks = 0;
        for (int r = 0; r < Cards.RankCount; r++)
        {
            switch (perRank[r])
            {
                case 0: continue;
                case 2: pairCount++; break;
                case 3: tripleRank = r; break;
                case 4: quadRank = r; break;
            }
            distinctRanks++;
        }

        if (quadRank >= 0)
            return new Combination(PlayKind.Quads, 5, quadRank);

        if (tripleRank >= 0 && pairCount == 1)
            return new Combination(PlayKind.FullHouse, 5, tripleRank);

        if (tripleRank >= 0)
            return null;   // trips plus two unrelated cards is not a play

        bool isStraight = distinctRanks == 5 && StraightMasks.Contains(rankMask);

        if (isStraight)
            return new Combination(oneSuit ? PlayKind.StraightFlush : PlayKind.Straight, 5, topKey);

        if (oneSuit)
            // Rank before suit, i.e. plain top-card order. Measured against
            // suit-first over the corpus: 0 violations vs 20.
            return new Combination(PlayKind.Flush, 5, topKey);

        return null;
    }

    /// <summary>True if these cards form a legal play of any kind.</summary>
    public static bool IsLegal(IReadOnlyList<int> cardIds) => Parse(cardIds) is not null;
}

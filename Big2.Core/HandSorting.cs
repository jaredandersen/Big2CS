namespace Big2.Core;

/// <summary>
/// How the player's fan is arranged. TEGL's {SORT} cycles rank, suit and a
/// "suggested set" order; the third needs the hand partitioned into playable
/// combinations, which arrives with the tuned AI, so it is not offered yet
/// rather than being offered and doing nothing.
/// </summary>
public enum HandSort
{
    /// <summary>Ascending Big 2 order. Equal ranks sit together, so pairs and triples are adjacent.</summary>
    Rank,

    /// <summary>Grouped by suit, ascending within each. Flushes and straight flushes read at a glance.</summary>
    Suit,
}

public static class HandSorting
{
    public static HandSort Next(HandSort order) =>
        order == HandSort.Rank ? HandSort.Suit : HandSort.Rank;

    /// <summary>Sorts in place. Deterministic: every card has a distinct key, so there are no ties to break.</summary>
    public static void Apply(List<int> hand, HandSort order)
    {
        hand.Sort(order == HandSort.Rank
            ? (a, b) => Cards.OrderKey(a).CompareTo(Cards.OrderKey(b))
            : (a, b) =>
            {
                int sa = Cards.SuitRank(Cards.SuitOf(a));
                int sb = Cards.SuitRank(Cards.SuitOf(b));
                return sa != sb ? sa.CompareTo(sb) : Cards.RankOf(a).CompareTo(Cards.RankOf(b));
            });
    }

    /// <summary>Convenience for callers holding an array rather than the game's own list.</summary>
    public static int[] Sorted(IEnumerable<int> cards, HandSort order)
    {
        var list = cards.ToList();
        Apply(list, order);
        return list.ToArray();
    }
}

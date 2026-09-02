namespace Big2.Core;

/// <summary>
/// Enumerates every legal play available from a hand.
///
/// This is deliberately the dumb, obviously-correct version: it walks all
/// subsets of size 1, 2, 3 and 5 and asks <see cref="Combinations.Parse"/>
/// whether each is a play. From thirteen cards that is 13 + 78 + 286 + 1287 =
/// 1,664 subsets, which costs nothing and cannot be subtly wrong the way a
/// clever per-category generator can.
///
/// nickmqb's generator is the cautionary case: its five-card branch is
/// <c>// TODO</c>, so its AI cannot play a five-card hand at all and always
/// passes against one. A generator that silently omits a category produces an
/// opponent that looks broken in a way no rules test would catch.
/// </summary>
public static class MoveGenerator
{
    /// <summary>
    /// Every legal play from <paramref name="hand"/> that beats
    /// <paramref name="toBeat"/>. Pass null for <paramref name="toBeat"/> to
    /// enumerate opening plays.
    ///
    /// <paramref name="mustInclude"/> constrains the play to contain a given
    /// card -- used for the opening lead of a series, which must contain the
    /// three of diamonds. Pass <see cref="Cards.Empty"/> for no constraint.
    /// </summary>
    public static List<int[]> Legal(IReadOnlyList<int> hand,
                                    Combination? toBeat,
                                    int mustInclude = Cards.Empty)
    {
        var result = new List<int[]>();
        int n = hand.Count;

        // Only sizes matching the play on the table are worth generating. There
        // are no bombs, so a five-card hand never answers a single.
        ReadOnlySpan<int> sizes = toBeat is { } t
            ? stackalloc int[] { t.Count }
            : stackalloc int[] { 1, 2, 3, 5 };

        foreach (int size in sizes)
        {
            if (size > n) continue;
            var idx = new int[size];
            for (int i = 0; i < size; i++) idx[i] = i;

            while (true)
            {
                var cards = new int[size];
                bool includes = mustInclude == Cards.Empty;
                for (int i = 0; i < size; i++)
                {
                    cards[i] = hand[idx[i]];
                    if (cards[i] == mustInclude) includes = true;
                }

                if (includes && Combinations.Parse(cards) is { } combo &&
                    (toBeat is null || combo.Beats(toBeat.Value)))
                {
                    result.Add(cards);
                }

                // Next combination of indices, lexicographic.
                int k = size - 1;
                while (k >= 0 && idx[k] == n - size + k) k--;
                if (k < 0) break;
                idx[k]++;
                for (int j = k + 1; j < size; j++) idx[j] = idx[j - 1] + 1;
            }
        }

        return result;
    }

    /// <summary>
    /// A play that empties the hand outright, or null. Worth asking before any
    /// scoring: going out ends the hand, so nothing else can be worth more.
    /// </summary>
    public static int[]? FindFinishingPlay(IReadOnlyList<int> hand, Combination? toBeat)
    {
        if (hand.Count is not (1 or 2 or 3 or 5)) return null;
        var all = hand.ToArray();
        if (Combinations.Parse(all) is not { } combo) return null;
        if (toBeat is { } t && !combo.Beats(t)) return null;
        return all;
    }
}

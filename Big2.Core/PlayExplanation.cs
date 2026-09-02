namespace Big2.Core;

/// <summary>
/// Why a selection cannot be played, in words.
///
/// A beep says only THAT something is wrong. In a game whose rules are the whole
/// difficulty -- five-card categories, a suit order that is not poker's, an
/// opening lead that must contain one specific card -- the useful thing is
/// WHICH rule was broken.
///
/// Headless, so the wording is testable and cannot drift between the button and
/// whatever else asks later (a hint, a tutorial, a status line).
/// </summary>
public static class PlayExplanation
{
    private static readonly string[] RankNames =
    {
        "3", "4", "5", "6", "7", "8", "9", "10", "jack", "queen", "king", "ace", "2",
    };

    private static readonly string[] SuitNames = { "diamonds", "clubs", "hearts", "spades" };

    /// <summary>Null when the selection is legal; otherwise one sentence saying why not.</summary>
    public static string? Why(Big2Game game, IReadOnlyList<int> cards)
    {
        if (cards.Count == 0)
            return game.CanPass
                ? "Select cards to play, or press Pass."
                : "You have the lead, so you must play. Select cards.";

        var hand = game.Hand(game.Turn);
        foreach (int c in cards)
            if (!hand.Contains(c))
                return "That card is not in your hand.";

        var combo = Combinations.Parse(cards);
        if (combo is null) return NotACombination(cards);

        if (game.RequiredCard != Cards.Empty && !cards.Contains(game.RequiredCard))
            return $"The first play of a game must include the {Short(game.RequiredCard)}.";

        if (game.Table is not { } table) return null;

        if (combo.Value.Count != table.Count)
            return $"The table has {Count(table.Count)}, so you must play {Count(table.Count)}.";

        if (!combo.Value.Beats(table))
            return $"{Capitalise(Describe(combo.Value, cards))} does not beat " +
                   $"{Describe(table, game.TableCards)}.";

        return null;
    }

    /// <summary>Why these cards are not a legal combination at all.</summary>
    private static string NotACombination(IReadOnlyList<int> cards)
    {
        switch (cards.Count)
        {
            case 2:
                return "Two cards must be a pair.";
            case 3:
                return "Three cards must be three of a kind.";
            case 5:
                return "Five cards must be a straight, a flush, a full house, " +
                       "four of a kind, or a straight flush.";
            case 4:
                return "You cannot play four cards. Try one, two, three or five.";
            default:
                return $"You cannot play {Count(cards.Count)}. Try one, two, three or five.";
        }
    }

    /// <summary>A play in words, e.g. "a pair of kings" or "the 5 of hearts".</summary>
    public static string Describe(Combination combo, IReadOnlyList<int> cards)
    {
        int topRank = Cards.RankOf(TopCard(cards));

        return combo.Kind switch
        {
            PlayKind.Single => $"the {Short(TopCard(cards))}",
            PlayKind.Pair => $"a pair of {Plural(topRank)}",
            PlayKind.Triple => $"three {Plural(topRank)}",
            PlayKind.Straight => $"a straight to the {RankNames[topRank]}",
            PlayKind.Flush => $"a flush in {SuitNames[Cards.SuitRank(Cards.SuitOf(TopCard(cards)))]}",
            PlayKind.FullHouse => $"a full house of {Plural(combo.Key)}",
            PlayKind.Quads => $"four {Plural(combo.Key)}",
            PlayKind.StraightFlush => $"a straight flush to the {RankNames[topRank]}",
            _ => "that",
        };
    }

    private static int TopCard(IReadOnlyList<int> cards)
    {
        int best = cards[0];
        foreach (int c in cards)
            if (Cards.OrderKey(c) > Cards.OrderKey(best)) best = c;
        return best;
    }

    private static string Short(int cardId) =>
        $"{RankNames[Cards.RankOf(cardId)]} of {SuitNames[Cards.SuitRank(Cards.SuitOf(cardId))]}";

    private static string Plural(int rankPosition) => rankPosition switch
    {
        11 => "aces",
        8 => "jacks",
        9 => "queens",
        10 => "kings",
        _ => RankNames[rankPosition] + "s",
    };

    private static string Count(int n) => n == 1 ? "one card" : $"{n} cards";

    private static string Capitalise(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}

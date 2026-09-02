namespace Big2.Core;

/// <summary>
/// Suits in bitmap index order. This is the STORAGE order and it is NOT
/// Big 2's ranking order -- see <see cref="Cards.SuitRank"/>.
/// </summary>
public enum Suit
{
    Clubs = 0,
    Diamonds = 1,
    Hearts = 2,
    Spades = 3,
}

/// <summary>
/// Card identity and the two orderings that have to be kept apart.
///
/// STORAGE is the deck's bitmap id space, so the artwork lookup is a
/// straight index:
///
///     id 0..51,  suit = id % 4  (clubs, diamonds, hearts, spades),
///     value = id / 4  with 0 = Ace, 1 = Two, ... 12 = King,
///     bitmap id = suit * 13 + value + 1.
///
/// BIG 2's ordering is a separate key and never the storage:
///
///     OrderKey = RankOrder(value) * 4 + SuitRank(suit)
///     RankOrder: 3 -> 0, 4 -> 1, ... K -> 10, A -> 11, 2 -> 12
///     SuitRank:  diamonds 0 &lt; clubs 1 &lt; hearts 2 &lt; spades 3
///
/// so 3(d) = 0, 2(s) = 51, and every comparison in the game is one integer
/// compare. Four independent implementations arrived at this same key, so it is
/// not in doubt -- but conflating it with the storage order is the single
/// easiest mistake to make here, which is why they are different methods with
/// different names.
/// </summary>
public static class Cards
{
    public const int Count = 52;
    public const int SuitCount = 4;
    public const int RankCount = 13;

    /// <summary>Sentinel for "no card".</summary>
    public const int Empty = -1;

    // Raw storage values: 0 = Ace, 1 = Two, ..., 12 = King.
    public const int RawAce = 0;
    public const int RawTwo = 1;
    public const int RawThree = 2;
    public const int RawKing = 12;

    public static Suit SuitOf(int cardId) => (Suit)(cardId % SuitCount);

    /// <summary>Raw value, 0 = Ace .. 12 = King. Storage order, not play order.</summary>
    public static int ValueOf(int cardId) => cardId / SuitCount;

    public static int IdOf(Suit suit, int rawValue) => rawValue * SuitCount + (int)suit;

    /// <summary>Bitmap resource id: clubs 1-13, diamonds 14-26, hearts 27-39, spades 40-52.</summary>
    public static int BitmapIdOf(int cardId) => (int)SuitOf(cardId) * RankCount + ValueOf(cardId) + 1;

    /// <summary>
    /// Big 2's rank position, 0 = three (lowest) .. 12 = two (highest).
    /// The three is lowest and the two is highest, which is the whole point of
    /// the game and the reason the raw value cannot be used directly.
    /// </summary>
    public static int RankOrder(int rawValue) => rawValue switch
    {
        RawAce => 11,   // ace sits above the king
        RawTwo => 12,   // and the two above the ace
        _ => rawValue - RawThree,
    };

    /// <summary>
    /// Big 2's suit ranking, diamonds lowest through spades highest. Agreed by
    /// big2.doc, Wikipedia and two of the reference implementations;
    /// tmwilliamlin168's clubs-below-diamonds is the lone outlier and is that
    /// repository's bug.
    /// </summary>
    public static int SuitRank(Suit suit) => suit switch
    {
        Suit.Diamonds => 0,
        Suit.Clubs => 1,
        Suit.Hearts => 2,
        Suit.Spades => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(suit)),
    };

    /// <summary>
    /// The single 0..51 comparison key. 3(d) = 0, 2(s) = 51.
    /// Every "does this beat that" in the game reduces to comparing these.
    /// </summary>
    public static int OrderKey(int cardId) =>
        RankOrder(ValueOf(cardId)) * SuitCount + SuitRank(SuitOf(cardId));

    /// <summary>Big 2 rank position of a card, 0 = three .. 12 = two.</summary>
    public static int RankOf(int cardId) => RankOrder(ValueOf(cardId));

    /// <summary>The three of diamonds -- leads the first hand of a series.</summary>
    public static int ThreeOfDiamonds { get; } = IdOf(Suit.Diamonds, RawThree);

    private const string RankGlyphs = "3456789TJQKA2";
    private const string SuitGlyphs = "dchs";   // indexed by SuitRank, not by Suit

    /// <summary>Short form matching the DOS log's notation, e.g. "3d", "Ts", "2s".</summary>
    public static string ToShort(int cardId) =>
        $"{RankGlyphs[RankOf(cardId)]}{SuitGlyphs[SuitRank(SuitOf(cardId))]}";

    /// <summary>Parses the DOS log's notation. Throws on anything else.</summary>
    public static int Parse(string text)
    {
        if (text is not { Length: 2 })
            throw new FormatException($"'{text}' is not a card");

        int rank = RankGlyphs.IndexOf(text[0]);
        int suitRank = SuitGlyphs.IndexOf(text[1]);
        if (rank < 0 || suitRank < 0)
            throw new FormatException($"'{text}' is not a card");

        Suit suit = suitRank switch
        {
            0 => Suit.Diamonds,
            1 => Suit.Clubs,
            2 => Suit.Hearts,
            _ => Suit.Spades,
        };

        int rawValue = rank switch
        {
            11 => RawAce,
            12 => RawTwo,
            _ => rank + RawThree,
        };

        return IdOf(suit, rawValue);
    }
}

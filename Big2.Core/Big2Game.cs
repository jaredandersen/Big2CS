namespace Big2.Core;

public enum StepKind
{
    /// <summary>A seat played cards.</summary>
    CardsPlayed,
    /// <summary>A seat passed.</summary>
    Passed,
    /// <summary>Three passes in a row: the table clears and the winner leads.</summary>
    TrickWon,
    /// <summary>A seat emptied its hand. The hand is over.</summary>
    HandOver,
    /// <summary>Nothing to do -- the hand has already ended.</summary>
    Idle,
}

/// <summary>One thing that happened, for the view to animate before asking for the next.</summary>
public readonly record struct GameStep(StepKind Kind, int Seat, IReadOnlyList<int> Cards);

/// <summary>
/// One hand of Big 2, driven one step at a time.
///
/// <see cref="Advance"/> performs exactly ONE step and returns what happened;
/// the view animates it and calls again. Collapsing this into direct calls
/// recurses a frame per play and leaves no gap for animation.
///
/// The sweep must be animated BEFORE the <see cref="StepKind.TrickWon"/> step is
/// consumed, because consuming it clears the table -- peek with
/// <see cref="IsTrickComplete"/> and <see cref="PendingTrickWinner"/>.
/// </summary>
public sealed class Big2Game
{
    private readonly List<int>[] _hands;
    private readonly List<int> _played = new();

    /// <summary>
    /// Each seat's most recent play IN THE CURRENT TRICK, so the view can show
    /// the whole round rather than only the last play. Cleared when the trick is
    /// taken.
    ///
    /// A seat CAN play twice in one trick -- that is not hypothetical, it is how
    /// the DOS log's tricks spill across display rows -- and the later play
    /// replaces the earlier one here, which is right: the earlier one is already
    /// beaten.
    /// </summary>
    private readonly int[][] _trickPlays =
        Enumerable.Range(0, Dealer.Seats).Select(_ => Array.Empty<int>()).ToArray();

    /// <summary>
    /// Whether each seat's most recent action IN THE CURRENT TRICK was a pass.
    ///
    /// Note this is not "has passed at some point": a seat that passes and then
    /// plays again when the turn comes back around is no longer passing. The
    /// reverse case -- played, then passed later -- means someone beat that play,
    /// so its cards are no longer the ones to beat and "pass" is the more useful
    /// thing to show in its place.
    /// </summary>
    private readonly bool[] _passedThisTrick = new bool[Dealer.Seats];

    private int _turn;
    private int _passesSinceLastPlay;
    private bool _openingLeadOfSeries;

    public Big2Game(int[][] hands, int leader, bool openingLeadOfSeries)
    {
        if (hands.Length != Dealer.Seats)
            throw new ArgumentException("Big 2 is a four-handed game", nameof(hands));

        _hands = hands.Select(h => h.ToList()).ToArray();
        _turn = leader;
        Leader = leader;
        _openingLeadOfSeries = openingLeadOfSeries;
    }

    /// <summary>Deals a fresh series: the three-of-diamonds holder leads and must play it.</summary>
    public static Big2Game NewSeries(int seed)
    {
        var hands = Dealer.Deal(seed);
        return new Big2Game(hands, Dealer.SeatHoldingThreeOfDiamonds(hands), openingLeadOfSeries: true);
    }

    /// <summary>Deals a subsequent hand: the previous hand's winner leads, unconstrained.</summary>
    public static Big2Game NextHand(int seed, int previousWinner) =>
        new(Dealer.Deal(seed), previousWinner, openingLeadOfSeries: false);

    // ------------------------------------------------------------------ state

    /// <summary>Seat that led the current trick.</summary>
    public int Leader { get; private set; }

    /// <summary>Seat to play.</summary>
    public int Turn => _turn;

    /// <summary>The play that must be beaten, or null when the table is clear.</summary>
    public Combination? Table { get; private set; }

    /// <summary>The cards currently on the table.</summary>
    public IReadOnlyList<int> TableCards { get; private set; } = Array.Empty<int>();

    /// <summary>Seat that played <see cref="TableCards"/>, or -1.</summary>
    public int TableOwner { get; private set; } = -1;

    /// <summary>
    /// What <paramref name="seat"/> has played in the current trick, empty if it
    /// has not played or has only passed. The view draws all four so the player
    /// can see the whole round, not just the play they have to beat.
    /// </summary>
    public IReadOnlyList<int> TrickPlay(int seat) => _trickPlays[seat];

    /// <summary>
    /// True when <paramref name="seat"/>'s most recent action in the current
    /// trick was a pass. Drawn in the centre where that seat's play would be.
    /// </summary>
    public bool HasPassed(int seat) => _passedThisTrick[seat];

    /// <summary>Every card played this hand, in order. This is what card counting reads.</summary>
    public IReadOnlyList<int> PlayHistory => _played;

    public IReadOnlyList<int> Hand(int seat) => _hands[seat];

    /// <summary>
    /// Reorders a seat's hand for display. The game does not care about hand
    /// order -- moves are generated from the SET of cards -- so this only
    /// affects how the fan is drawn and how slot indices map to cards.
    /// </summary>
    public void SortHand(int seat, HandSort order) => HandSorting.Apply(_hands[seat], order);

    public int CardsLeft(int seat) => _hands[seat].Count;

    /// <summary>Seat that emptied its hand, or -1 while the hand is live.</summary>
    public int Winner { get; private set; } = -1;

    public bool IsHandOver => Winner >= 0;

    /// <summary>
    /// True when the next <see cref="Advance"/> will clear the table. Peek at
    /// this before advancing so the sweep can be animated while the cards are
    /// still there.
    /// </summary>
    public bool IsTrickComplete => !IsHandOver && Table is not null && _passesSinceLastPlay >= Dealer.Seats - 1;

    /// <summary>Who will take the trick that <see cref="IsTrickComplete"/> reports.</summary>
    public int PendingTrickWinner => IsTrickComplete ? TableOwner : -1;

    /// <summary>The opening lead of a series must contain the three of diamonds.</summary>
    public int RequiredCard =>
        _openingLeadOfSeries && Table is null ? Cards.ThreeOfDiamonds : Cards.Empty;

    // ------------------------------------------------------------------ moves

    /// <summary>Every legal play for the seat to move.</summary>
    public List<int[]> LegalPlays() => MoveGenerator.Legal(_hands[_turn], Table, RequiredCard);

    /// <summary>
    /// Whether the seat on turn may pass. It may not when the table is clear --
    /// holding the lead obliges you to play.
    /// </summary>
    public bool CanPass => Table is not null;

    public bool IsLegal(IReadOnlyList<int> cards)
    {
        if (cards.Count == 0) return false;
        var hand = _hands[_turn];
        foreach (int c in cards)
            if (!hand.Contains(c)) return false;

        if (Combinations.Parse(cards) is not { } combo) return false;
        if (RequiredCard != Cards.Empty && !cards.Contains(RequiredCard)) return false;
        return Table is null || combo.Beats(Table.Value);
    }

    // ------------------------------------------------------------------- pump

    /// <summary>
    /// Plays <paramref name="cards"/> for the seat on turn. Throws if illegal --
    /// callers ask <see cref="IsLegal"/> first; the AI generates only legal moves.
    /// </summary>
    public GameStep Play(IReadOnlyList<int> cards)
    {
        if (IsHandOver) return new GameStep(StepKind.Idle, -1, Array.Empty<int>());
        if (!IsLegal(cards))
            throw new InvalidOperationException(
                $"seat {_turn} cannot play {string.Join(" ", cards.Select(Cards.ToShort))}");

        int seat = _turn;
        var played = cards.ToArray();

        foreach (int c in played) _hands[seat].Remove(c);
        _played.AddRange(played);

        Table = Combinations.Parse(played);
        TableCards = played;
        TableOwner = seat;
        _trickPlays[seat] = played;
        _passedThisTrick[seat] = false;
        _passesSinceLastPlay = 0;
        _openingLeadOfSeries = false;

        if (_hands[seat].Count == 0)
        {
            Winner = seat;
            return new GameStep(StepKind.HandOver, seat, played);
        }

        _turn = Next(seat);
        return new GameStep(StepKind.CardsPlayed, seat, played);
    }

    /// <summary>Passes for the seat on turn.</summary>
    public GameStep Pass()
    {
        if (IsHandOver) return new GameStep(StepKind.Idle, -1, Array.Empty<int>());
        if (!CanPass)
            throw new InvalidOperationException($"seat {_turn} holds the lead and must play");

        int seat = _turn;
        _passesSinceLastPlay++;
        _passedThisTrick[seat] = true;
        _turn = Next(seat);
        return new GameStep(StepKind.Passed, seat, Array.Empty<int>());
    }

    /// <summary>
    /// Consumes a completed trick: clears the table and gives the lead to the
    /// winner. Only call when <see cref="IsTrickComplete"/>.
    /// </summary>
    public GameStep CompleteTrick()
    {
        if (!IsTrickComplete)
            throw new InvalidOperationException("no completed trick to take");

        int winner = TableOwner;
        Table = null;
        TableCards = Array.Empty<int>();
        TableOwner = -1;
        for (int s = 0; s < Dealer.Seats; s++)
        {
            _trickPlays[s] = Array.Empty<int>();
            _passedThisTrick[s] = false;
        }
        _passesSinceLastPlay = 0;
        _turn = winner;
        Leader = winner;
        return new GameStep(StepKind.TrickWon, winner, Array.Empty<int>());
    }

    private static int Next(int seat) => (seat + 1) % Dealer.Seats;
}

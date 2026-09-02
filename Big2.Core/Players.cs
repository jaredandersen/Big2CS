namespace Big2.Core;

/// <summary>A policy: given the game and a seat, choose a play or pass (null).</summary>
public interface IPlayer
{
    string Name { get; }

    /// <summary>
    /// Returns the cards to play, or null to pass. Must return a legal play, and
    /// must not return null when the table is clear.
    /// </summary>
    int[]? ChooseMove(Big2Game game, DealRandom rng);
}

/// <summary>
/// Baselines of KNOWN relative strength, kept so the tournament can be shown to
/// discriminate. Jennifer-Lion's evaluator ships eight test agents with expected
/// ranks for exactly this reason: a ranking system that has never been made to
/// produce a known ordering has not been tested.
///
/// Expected, weakest first: Random &lt; LowestLegal &lt; Greedy.
/// </summary>
public static class Baselines
{
    /// <summary>Uniformly random among legal plays, and passes half the time when it may.</summary>
    public sealed class Random : IPlayer
    {
        public string Name => "Random";

        public int[]? ChooseMove(Big2Game game, DealRandom rng)
        {
            var moves = game.LegalPlays();
            if (moves.Count == 0) return null;
            if (game.CanPass && rng.Next(2) == 0) return null;
            return moves[rng.Next(moves.Count)];
        }
    }

    /// <summary>
    /// Always plays its cheapest legal move, never passes voluntarily. A real
    /// strategy, if a poor one -- it sheds low cards and hoards high ones, but
    /// never keeps a set together and never blocks.
    /// </summary>
    public sealed class LowestLegal : IPlayer
    {
        public string Name => "LowestLegal";

        public int[]? ChooseMove(Big2Game game, DealRandom rng)
        {
            var moves = game.LegalPlays();
            if (moves.Count == 0) return null;
            return moves.MinBy(Rank)!;
        }

        private static long Rank(int[] move)
        {
            var c = Combinations.Parse(move)!.Value;
            return ((long)c.Kind << 32) | (uint)c.Key;
        }
    }

    /// <summary>
    /// Cheapest legal move, but prefers shedding MORE cards, and takes a
    /// finishing play immediately.
    ///
    /// The card-count preference is the one idea worth taking from the reference
    /// implementations -- kyleliao's leading heuristic and cappinmeow's
    /// play_more_cards weight agree on it, and nickmqb's stub, which leads a bare
    /// single every time, is what it looks like when you leave it out.
    ///
    /// This is the Phase 1a placeholder opponent. The tuned policy replaces it.
    /// </summary>
    public sealed class Greedy : IPlayer
    {
        public string Name => "Greedy";

        public int[]? ChooseMove(Big2Game game, DealRandom rng)
        {
            if (MoveGenerator.FindFinishingPlay(game.Hand(game.Turn), game.Table) is { } finish)
                return finish;

            var moves = game.LegalPlays();
            if (moves.Count == 0) return null;

            // More cards first, then the cheapest play of that size.
            return moves.OrderByDescending(m => m.Length).ThenBy(Rank).First();
        }

        private static long Rank(int[] move)
        {
            var c = Combinations.Parse(move)!.Value;
            return ((long)c.Kind << 32) | (uint)c.Key;
        }
    }
}

namespace Big2.Core;

/// <summary>
/// A small explicit PRNG, so a seed produces the same deal on every runtime and
/// every machine.
///
/// <see cref="System.Random"/> would be simpler but its algorithm is not
/// contractually stable across .NET versions, and the whole value of a seeded
/// deal here is that a bug report can name one. This is xorshift128+, which is
/// short enough to read and adequate for shuffling cards.
/// </summary>
public sealed class DealRandom
{
    private ulong _s0, _s1;

    public DealRandom(int seed)
    {
        // SplitMix64 to spread a small seed across the state; a bare seed of 1
        // otherwise leaves the generator nearly all zeroes for a long while.
        ulong z = (ulong)seed + 0x9E3779B97F4A7C15UL;
        _s0 = Mix(ref z);
        _s1 = Mix(ref z);
        if ((_s0 | _s1) == 0) _s1 = 1;   // the all-zero state is a fixed point
    }

    private static ulong Mix(ref ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        ulong r = z;
        r = (r ^ (r >> 30)) * 0xBF58476D1CE4E5B9UL;
        r = (r ^ (r >> 27)) * 0x94D049BB133111EBUL;
        return r ^ (r >> 31);
    }

    public ulong NextUInt64()
    {
        ulong x = _s0, y = _s1;
        _s0 = y;
        x ^= x << 23;
        _s1 = x ^ y ^ (x >> 17) ^ (y >> 26);
        return _s1 + y;
    }

    /// <summary>Uniform in [0, bound), rejection-sampled so it is genuinely uniform.</summary>
    public int Next(int bound)
    {
        if (bound <= 0) throw new ArgumentOutOfRangeException(nameof(bound));
        ulong limit = ulong.MaxValue - (ulong.MaxValue % (ulong)bound);
        ulong r;
        do { r = NextUInt64(); } while (r >= limit);
        return (int)(r % (ulong)bound);
    }
}

/// <summary>
/// Deals thirteen cards to each of four seats.
///
/// The seed is internal to tests, self-play and the tuner. This game
/// deliberately exposes no game number and has no Select Game dialog, unlike
/// the deal-number card games it sits alongside.
/// </summary>
public static class Dealer
{
    public const int Seats = 4;
    public const int HandSize = 13;

    /// <summary>Deals a shuffled deck. Each hand comes back sorted by Big 2 order.</summary>
    public static int[][] Deal(DealRandom rng)
    {
        var deck = new int[Cards.Count];
        for (int i = 0; i < deck.Length; i++) deck[i] = i;

        // Fisher-Yates, downward.
        for (int i = deck.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }

        var hands = new int[Seats][];
        for (int s = 0; s < Seats; s++)
        {
            hands[s] = new int[HandSize];
            Array.Copy(deck, s * HandSize, hands[s], 0, HandSize);
            Array.Sort(hands[s], (a, b) => Cards.OrderKey(a).CompareTo(Cards.OrderKey(b)));
        }
        return hands;
    }

    public static int[][] Deal(int seed) => Deal(new DealRandom(seed));

    /// <summary>Which seat holds the three of diamonds, and therefore leads a fresh series.</summary>
    public static int SeatHoldingThreeOfDiamonds(int[][] hands)
    {
        for (int s = 0; s < hands.Length; s++)
            if (Array.IndexOf(hands[s], Cards.ThreeOfDiamonds) >= 0)
                return s;
        throw new InvalidOperationException("no seat holds the three of diamonds");
    }
}

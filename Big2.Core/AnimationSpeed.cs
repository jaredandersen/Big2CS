namespace Big2.Core;

/// <summary>
/// How fast cards move.
///
/// The durations are INVENTED. There is no Big 2 binary to take a pace from,
/// only TEGL's rule set, and a rule set has no pace. These are chosen numbers,
/// picked by watching them, and nothing here is a measurement.
///
/// <see cref="Off"/> is not a token gesture: an opponent's move is announced by
/// the centre changing, and a player who has watched it a hundred times may want
/// the game to keep up rather than perform.
/// </summary>
public enum AnimationSpeed
{
    Off,
    Fast,
    Normal,
    Slow,
}

public static class Animation
{
    /// <summary>How long a card takes to reach the centre.</summary>
    public static double PlayMs(AnimationSpeed speed) => speed switch
    {
        AnimationSpeed.Off => 0,
        AnimationSpeed.Fast => 110,
        AnimationSpeed.Slow => 380,
        _ => 200,
    };

    /// <summary>
    /// How long the winner's sweep takes. Longer than a play on purpose: it is
    /// the one moment where four plays leave at once, and it is also the beat
    /// that tells you who took the trick.
    /// </summary>
    public static double SweepMs(AnimationSpeed speed) => speed switch
    {
        AnimationSpeed.Off => 0,
        AnimationSpeed.Fast => 160,
        AnimationSpeed.Slow => 520,
        _ => 280,
    };

    /// <summary>
    /// How long to hold a completed trick before sweeping it. Without a pause the
    /// winning play is on screen for one frame and gone.
    /// </summary>
    public static double TrickPauseMs(AnimationSpeed speed) => speed switch
    {
        AnimationSpeed.Off => 0,
        AnimationSpeed.Fast => 260,
        AnimationSpeed.Slow => 900,
        _ => 500,
    };

    /// <summary>How long an opponent appears to think before playing.</summary>
    public static double ThinkMs(AnimationSpeed speed) => speed switch
    {
        AnimationSpeed.Off => 0,
        AnimationSpeed.Fast => 120,
        AnimationSpeed.Slow => 600,
        _ => 280,
    };
}

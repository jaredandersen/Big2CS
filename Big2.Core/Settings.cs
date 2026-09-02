namespace Big2.Core;

/// <summary>A remembered window rectangle, in device-independent units.</summary>
public readonly record struct WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized)
{
    /// <summary>
    /// Clamps against the display AS IT IS NOW, or returns null meaning "use the
    /// default".
    ///
    /// It REFUSES rather than salvaging. Dragging a window remembered on a
    /// monitor that is no longer attached back to the nearest corner sounds
    /// helpful and is not: the case this exists for is a window restored
    /// off-screen, where the game is running, focused, accepting input and
    /// completely invisible, and the only cure a player can find is deleting a
    /// file they do not know exists. Falling back to the default is always
    /// recoverable.
    ///
    /// Note the virtual screen's origin is NOT 0,0 -- a monitor left of or above
    /// the primary one has a negative origin -- so clamping to zero would drag
    /// every window off it and onto the primary.
    /// </summary>
    public WindowPlacement? ClampTo(double screenLeft, double screenTop, double screenWidth, double screenHeight,
                                    double minVisible = 96)
    {
        if (Width <= 0 || Height <= 0) return null;

        double right = screenLeft + screenWidth;
        double bottom = screenTop + screenHeight;

        double visibleW = Math.Min(Left + Width, right) - Math.Max(Left, screenLeft);
        double visibleH = Math.Min(Top + Height, bottom) - Math.Max(Top, screenTop);

        if (visibleW < minVisible || visibleH < minVisible) return null;

        // It is on screen enough to be found. Trim the size if the display has
        // shrunk, but do not move it.
        double w = Math.Min(Width, screenWidth);
        double h = Math.Min(Height, screenHeight);
        return this with { Width = w, Height = h };
    }
}

/// <summary>
/// Everything the game remembers between runs, in a plain-text file beside the
/// executable.
///
/// The path is <see cref="AppContext.BaseDirectory"/> -- NOT the current
/// directory, which is wherever the player happened to launch from, and NOT
/// Assembly.Location, which is empty for a single-file publish.
///
/// EVERY known setting is written on exit, not only the ones that changed: a key
/// absent from the file cannot be discovered by opening the file, which is most
/// of the point of using a text format.
/// </summary>
public sealed class Settings
{
    public const string FileName = "big2.ini";

    private const string Preamble =
        "Big 2 settings.\n" +
        "\n" +
        "Safe to edit by hand. Values are read back with the invariant culture,\n" +
        "so use a full stop for any decimal point regardless of your locale.\n" +
        "Delete this file to return everything to its defaults.";

    // ---------------------------------------------------------------- values

    public HandSort HandOrder { get; set; } = HandSort.Rank;

    public AnimationSpeed AnimationSpeed { get; set; } = AnimationSpeed.Normal;

    /// <summary>How well the three opponents play. See <see cref="PolicySkills"/>.</summary>
    public Difficulty Difficulty { get; set; } = Difficulty.Normal;

    /// <summary>Optional target; 0 means the series runs until the player stops.</summary>
    public int TargetScore { get; set; }

    public string[] SeatNames { get; set; } = { "You", "East", "North", "West" };

    public WindowPlacement? Window { get; set; }

    /// <summary>
    /// The running series. With no target score there is nothing else to end a
    /// series on, so closing the game would otherwise discard an arbitrarily
    /// long one.
    /// </summary>
    public int[] SeriesTotals { get; set; } = new int[Dealer.Seats];
    public int SeriesHands { get; set; }

    // ------------------------------------------------------------------- I/O

    public static string PathBesideExecutable() =>
        System.IO.Path.Combine(AppContext.BaseDirectory, FileName);

    public static Settings Load(string? path = null)
    {
        var ini = IniFile.Load(path ?? PathBesideExecutable());
        var s = new Settings
        {
            HandOrder = ini.GetEnum("HandOrder", HandSort.Rank),
            AnimationSpeed = ini.GetEnum("AnimationSpeed", AnimationSpeed.Normal),
            Difficulty = ini.GetEnum("Difficulty", Difficulty.Normal),
            TargetScore = Math.Max(0, ini.GetInt("TargetScore", 0)),
            SeriesHands = Math.Max(0, ini.GetInt("SeriesHands", 0)),
        };

        var names = (string[])s.SeatNames.Clone();
        for (int i = 0; i < names.Length; i++)
            names[i] = ini.GetString($"SeatName{i}") ?? names[i];
        s.SeatNames = names;

        var totals = new int[Dealer.Seats];
        for (int i = 0; i < totals.Length; i++)
            totals[i] = ini.GetInt($"SeriesTotal{i}", 0);
        s.SeriesTotals = totals;

        if (ini.GetString("WindowWidth") is not null)
        {
            s.Window = new WindowPlacement(
                ini.GetDouble("WindowLeft", 0),
                ini.GetDouble("WindowTop", 0),
                ini.GetDouble("WindowWidth", 0),
                ini.GetDouble("WindowHeight", 0),
                ini.GetBool("WindowMaximized", false));
        }

        return s;
    }

    /// <summary>
    /// Writes every known setting. Reloads first so keys written by a different
    /// version survive.
    /// </summary>
    public void Save(string? path = null)
    {
        path ??= PathBesideExecutable();
        var ini = IniFile.Load(path);

        ini.Set("HandOrder", HandOrder.ToString());
        ini.Set("AnimationSpeed", AnimationSpeed.ToString());
        ini.Set("Difficulty", Difficulty.ToString());
        ini.Set("TargetScore", TargetScore);

        for (int i = 0; i < SeatNames.Length; i++) ini.Set($"SeatName{i}", SeatNames[i]);
        for (int i = 0; i < SeriesTotals.Length; i++) ini.Set($"SeriesTotal{i}", SeriesTotals[i]);
        ini.Set("SeriesHands", SeriesHands);

        if (Window is { } w)
        {
            ini.Set("WindowLeft", w.Left);
            ini.Set("WindowTop", w.Top);
            ini.Set("WindowWidth", w.Width);
            ini.Set("WindowHeight", w.Height);
            ini.Set("WindowMaximized", w.Maximized);
        }

        string text = ini.Render(Preamble, new (string, (string, string?)[])[]
        {
            ("Game", new (string, string?)[]
            {
                ("HandOrder", "How the Sort button arranges your hand: Rank or Suit."),
                ("AnimationSpeed", "How fast cards move: Off, Fast, Normal or Slow."),
                ("Difficulty", "How well the opponents play: Easy, Normal or Hard."),
                ("TargetScore", "End the series when a player reaches this. 0 means it never ends on its own."),
            }),
            ("Players", new (string, string?)[]
            {
                ("SeatName0", "You, then the other three in play order: your right, across, your left."),
                ("SeatName1", null),
                ("SeatName2", null),
                ("SeatName3", null),
            }),
            ("Series", new (string, string?)[]
            {
                ("SeriesHands", "The series in progress. Lowest total wins; New Series clears these."),
                ("SeriesTotal0", null),
                ("SeriesTotal1", null),
                ("SeriesTotal2", null),
                ("SeriesTotal3", null),
            }),
            ("Window", new (string, string?)[]
            {
                ("WindowLeft", "Position and size in device-independent units, NOT physical pixels."),
                ("WindowTop", null),
                ("WindowWidth", null),
                ("WindowHeight", null),
                ("WindowMaximized", null),
            }),
        });

        try
        {
            File.WriteAllText(path, text);
        }
        catch (IOException)
        {
            // A settings file that cannot be written is not worth crashing over.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

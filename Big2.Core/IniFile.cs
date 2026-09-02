using System.Globalization;
using System.Text;

namespace Big2.Core;

/// <summary>
/// A plain-text settings file, written pretty and read dumb.
///
/// Four format rules, each carried from an earlier project in this series where
/// it cost real time:
///
/// 1. A HINT GOES ON ITS OWN LINE ABOVE THE KEY, never after the value. SkiCS
///    emitted "Scale=2   ; 1, 2 or 3" and its reader took everything after the
///    "=", so every number silently fell back to its default while the file
///    looked perfectly correct. It compounded -- each save appended another copy
///    of the hint -- and window position, size and zoom had been inert for days.
/// 2. THE READER STRIPS A TRAILING ";" ANYWAY, because a person editing by hand
///    will write one.
/// 3. INVARIANT CULTURE FOR EVERY NUMBER. Not theoretical: window positions are
///    NEGATIVE on a monitor left of or above the primary one, and Arabic locales
///    prefix a negative with an invisible directional mark (ar-EG emits U+061C)
///    that an invariant parse then rejects, silently falling the value back to
///    its default.
/// 4. UNKNOWN KEYS SURVIVE A ROUND TRIP, so a file from a newer build is not
///    emptied by an older one.
///
/// Parsing ignores sections, ordering, whitespace and case. Section headers are
/// written for a reader's benefit and mean nothing to the parser.
/// </summary>
public sealed class IniFile
{
    private readonly Dictionary<string, string> _values =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every key present, including ones this build does not understand.</summary>
    public IReadOnlyDictionary<string, string> Values => _values;

    public static IniFile Parse(string text)
    {
        var ini = new IniFile();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#' or '[') continue;

            int eq = line.IndexOf('=');
            if (eq <= 0) continue;

            string key = line[..eq].Trim();
            string value = line[(eq + 1)..];

            // Rule 2: strip a trailing comment even though we never write one.
            int semi = value.IndexOf(';');
            if (semi >= 0) value = value[..semi];

            ini._values[key] = value.Trim();
        }
        return ini;
    }

    public static IniFile Load(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : new IniFile();
        }
        catch (IOException)
        {
            return new IniFile();
        }
        catch (UnauthorizedAccessException)
        {
            return new IniFile();
        }
    }

    public void Set(string key, string value) => _values[key] = value;
    public void Set(string key, int value) => _values[key] = value.ToString(CultureInfo.InvariantCulture);
    public void Set(string key, bool value) => _values[key] = value ? "true" : "false";

    public void Set(string key, double value) =>
        _values[key] = value.ToString("R", CultureInfo.InvariantCulture);

    public string? GetString(string key) => _values.TryGetValue(key, out var v) && v.Length > 0 ? v : null;

    public int GetInt(string key, int fallback) =>
        GetString(key) is { } v && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
            ? n : fallback;

    public double GetDouble(string key, double fallback) =>
        GetString(key) is { } v && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double n)
            ? n : fallback;

    public bool GetBool(string key, bool fallback) => GetString(key) switch
    {
        null => fallback,
        var v when v.Equals("true", StringComparison.OrdinalIgnoreCase) => true,
        var v when v.Equals("false", StringComparison.OrdinalIgnoreCase) => false,
        _ => fallback,
    };

    public TEnum GetEnum<TEnum>(string key, TEnum fallback) where TEnum : struct, Enum =>
        GetString(key) is { } v && Enum.TryParse(v, ignoreCase: true, out TEnum n) ? n : fallback;

    /// <summary>
    /// Writes the file. <paramref name="sections"/> maps a section header to the
    /// keys under it, each with an optional hint written ABOVE the key. Keys this
    /// build does not know are preserved in a trailing section rather than lost.
    /// </summary>
    public string Render(string preamble, IReadOnlyList<(string Section, (string Key, string? Hint)[] Keys)> sections)
    {
        var sb = new StringBuilder();
        foreach (var line in preamble.Split('\n'))
            sb.Append("; ").AppendLine(line.TrimEnd());

        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (section, keys) in sections)
        {
            sb.AppendLine().Append('[').Append(section).AppendLine("]");
            foreach (var (key, hint) in keys)
            {
                if (!_values.TryGetValue(key, out var value)) continue;
                if (hint is not null) sb.Append("; ").AppendLine(hint);
                sb.Append(key).Append('=').AppendLine(value);
                written.Add(key);
            }
        }

        var unknown = _values.Keys.Where(k => !written.Contains(k)).OrderBy(k => k).ToArray();
        if (unknown.Length > 0)
        {
            sb.AppendLine().AppendLine("[Other]");
            sb.AppendLine("; Written by a different version of the game. Preserved rather than");
            sb.AppendLine("; discarded, so an older build does not empty a newer build's file.");
            foreach (var k in unknown) sb.Append(k).Append('=').AppendLine(_values[k]);
        }

        return sb.ToString();
    }
}

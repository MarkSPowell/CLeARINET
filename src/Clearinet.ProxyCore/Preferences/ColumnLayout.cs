using System.Globalization;

namespace Clearinet.ProxyCore.Preferences;

/// <summary>One remembered column: its key and, if it was given one, its width.</summary>
public sealed record ColumnLayoutEntry(string Key, double? Width);

/// <summary>
/// A list's remembered column order and widths, kept as a list preference
/// (see <see cref="PreferenceStore.SetListPref"/>): one entry per column, in
/// display order, written <c>width|key</c> (width empty when the column
/// sizes itself). Columns that come and go, such as a script's, keep their
/// place while they're away.
/// </summary>
public static class ColumnLayout
{
    private static readonly StringComparer KeyComparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>Reads entries written by <see cref="Format"/>. Malformed or repeated entries are skipped.</summary>
    public static IReadOnlyList<ColumnLayoutEntry> Parse(IEnumerable<string> entries)
    {
        var result = new List<ColumnLayoutEntry>();
        var seen = new HashSet<string>(KeyComparer);
        foreach (var entry in entries)
        {
            var bar = entry.IndexOf('|', StringComparison.Ordinal);
            if (bar < 0)
            {
                continue;
            }

            var key = entry[(bar + 1)..].Trim();
            if (key.Length == 0 || !seen.Add(key))
            {
                continue;
            }

            double? width = double.TryParse(entry[..bar], NumberStyles.Float, CultureInfo.InvariantCulture, out var w) && w > 0 && double.IsFinite(w)
                ? w
                : null;
            result.Add(new ColumnLayoutEntry(key, width));
        }

        return result;
    }

    /// <summary>The entries as list-preference values. <c>;</c> (the list separator) is dropped from keys.</summary>
    public static IEnumerable<string> Format(IEnumerable<ColumnLayoutEntry> entries) =>
        entries.Select(e =>
            (e.Width is > 0 ? Math.Round(e.Width.Value, 1).ToString(CultureInfo.InvariantCulture) : string.Empty) +
            "|" + e.Key.Replace(";", string.Empty, StringComparison.Ordinal));

    /// <summary>
    /// What to save: <paramref name="current"/> (the columns there now, in
    /// display order), plus each <paramref name="saved"/> entry for a column
    /// that isn't there now, placed after the column it followed before, so
    /// it comes back where it was.
    /// </summary>
    public static IReadOnlyList<ColumnLayoutEntry> Merge(IReadOnlyList<ColumnLayoutEntry> saved, IReadOnlyList<ColumnLayoutEntry> current)
    {
        var result = new List<ColumnLayoutEntry>();
        var seen = new HashSet<string>(KeyComparer);
        foreach (var entry in current)
        {
            if (seen.Add(entry.Key))
            {
                result.Add(entry);
            }
        }

        string? previous = null;
        foreach (var entry in saved)
        {
            if (!seen.Contains(entry.Key))
            {
                var at = previous is null ? 0 : result.FindIndex(e => KeyComparer.Equals(e.Key, previous)) + 1;
                result.Insert(at, entry);
                seen.Add(entry.Key);
            }

            previous = entry.Key;
        }

        return result;
    }

    /// <summary>
    /// The order to show columns in. <paramref name="current"/> is each
    /// column's key in today's display order; the result lists indexes into
    /// it, first column first. Columns in <paramref name="saved"/> take the
    /// saved order among themselves, in the places those columns occupy now;
    /// a column with no saved place stays where it is.
    /// </summary>
    public static IReadOnlyList<int> Arrange(IReadOnlyList<string> current, IReadOnlyList<string> saved)
    {
        var rank = new Dictionary<string, int>(KeyComparer);
        for (var i = 0; i < saved.Count; i++)
        {
            rank.TryAdd(saved[i], i);
        }

        var known = Enumerable.Range(0, current.Count)
            .Where(i => rank.ContainsKey(current[i]))
            .OrderBy(i => rank[current[i]])
            .ToList();

        var result = new List<int>(current.Count);
        var next = 0;
        for (var i = 0; i < current.Count; i++)
        {
            result.Add(rank.ContainsKey(current[i]) ? known[next++] : i);
        }

        return result;
    }
}

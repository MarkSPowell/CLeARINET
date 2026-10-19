namespace Violens.AppCore.Compare;

/// <summary>How a row of a side-by-side comparison differs.</summary>
public enum DiffKind
{
    /// <summary>The same line on both sides.</summary>
    Same,

    /// <summary>A line on each side, different.</summary>
    Changed,

    /// <summary>A line only on the left.</summary>
    Removed,

    /// <summary>A line only on the right.</summary>
    Added,
}

/// <summary>
/// One row of a side-by-side comparison. Line numbers are 1-based; a side
/// with no line has null for both. For <see cref="DiffKind.Changed"/> rows,
/// <see cref="TextDiff.ChangedRange"/> says which part of the line differs.
/// </summary>
public sealed record DiffRow(DiffKind Kind, int? LeftNumber, string? Left, int? RightNumber, string? Right);

/// <summary>
/// A comparison's rows, how many separate places differ, and whether the
/// texts were too different to line up in detail (then the differing middle
/// is shown as one block).
/// </summary>
public sealed record DiffResult(IReadOnlyList<DiffRow> Rows, int Differences, bool Simplified)
{
    /// <summary>True when both texts were the same.</summary>
    public bool Identical => Differences == 0;

    /// <summary>The index in <see cref="Rows"/> where each difference starts.</summary>
    public IReadOnlyList<int> DifferenceStarts()
    {
        var starts = new List<int>();
        for (var i = 0; i < Rows.Count; i++)
        {
            if (Rows[i].Kind != DiffKind.Same && (i == 0 || Rows[i - 1].Kind == DiffKind.Same))
            {
                starts.Add(i);
            }
        }

        return starts;
    }
}

/// <summary>
/// A line-by-line comparison of two texts, for showing side by side
/// (Compare Sessions). Lines are lined up with the shortest edit script
/// (Myers' O(ND) algorithm), after setting aside the lines the texts start
/// and end with in common. Removed and added lines next to each other are
/// paired up as changed lines.
/// </summary>
public static class TextDiff
{
    /// <summary>
    /// How many differing lines are worked through before giving up on
    /// lining them up in detail. Keeps a comparison of two very different
    /// large bodies quick, and its memory bounded (about MaxEdits² ints).
    /// </summary>
    public const int MaxEdits = 2000;

    private enum Op
    {
        Equal,
        Delete,
        Insert,
    }

    /// <summary>Compares <paramref name="left"/> and <paramref name="right"/> line by line.</summary>
    public static DiffResult Compare(string left, string right) => Compare(SplitLines(left), SplitLines(right));

    /// <summary>Compares two lists of lines.</summary>
    public static DiffResult Compare(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        // Lines as numbers, so the comparison loop compares ints.
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        var a = left.Select(line => Id(ids, line)).ToArray();
        var b = right.Select(line => Id(ids, line)).ToArray();

        var prefix = 0;
        while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < a.Length - prefix && suffix < b.Length - prefix &&
               a[a.Length - 1 - suffix] == b[b.Length - 1 - suffix])
        {
            suffix++;
        }

        var middleA = a[prefix..(a.Length - suffix)];
        var middleB = b[prefix..(b.Length - suffix)];

        var ops = new List<Op>(a.Length + b.Length);
        ops.AddRange(Enumerable.Repeat(Op.Equal, prefix));
        var middle = ShortestEdit(middleA, middleB, MaxEdits);
        var simplified = middle is null;
        if (middle is null)
        {
            ops.AddRange(Enumerable.Repeat(Op.Delete, middleA.Length));
            ops.AddRange(Enumerable.Repeat(Op.Insert, middleB.Length));
        }
        else
        {
            ops.AddRange(middle);
        }

        ops.AddRange(Enumerable.Repeat(Op.Equal, suffix));
        return BuildRows(ops, left, right, simplified);
    }

    /// <summary>
    /// The lines of <paramref name="text"/>, split at CRLF, LF or CR. A final
    /// line break doesn't start another (empty) line.
    /// </summary>
    public static IReadOnlyList<string> SplitLines(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is not ('\n' or '\r'))
            {
                continue;
            }

            lines.Add(text[start..i]);
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                i++;
            }

            start = i + 1;
        }

        if (start < text.Length)
        {
            lines.Add(text[start..]);
        }

        return lines;
    }

    /// <summary>
    /// Which part of a changed line differs: the length of the text both
    /// lines start with, then how much of each line follows before the text
    /// both end with. For "user=alice&amp;x=1" and "user=bob&amp;x=1" that's
    /// (5, 5, 3).
    /// </summary>
    public static (int Start, int LeftLength, int RightLength) ChangedRange(string left, string right)
    {
        var start = 0;
        while (start < left.Length && start < right.Length && left[start] == right[start])
        {
            start++;
        }

        var end = 0;
        while (end < left.Length - start && end < right.Length - start &&
               left[left.Length - 1 - end] == right[right.Length - 1 - end])
        {
            end++;
        }

        // Don't split a surrogate pair at either end.
        if (start > 0 && char.IsHighSurrogate(left[start - 1]))
        {
            start--;
        }

        if (end > 0 && char.IsLowSurrogate(left[left.Length - end]))
        {
            end--;
        }

        return (start, left.Length - start - end, right.Length - start - end);
    }

    private static int Id(Dictionary<string, int> ids, string line)
    {
        if (!ids.TryGetValue(line, out var id))
        {
            id = ids.Count;
            ids.Add(line, id);
        }

        return id;
    }

    /// <summary>
    /// Myers' greedy algorithm: the shortest list of keeps, deletes and
    /// inserts turning <paramref name="a"/> into <paramref name="b"/>, or
    /// null if that takes more than <paramref name="maxEdits"/> edits.
    /// </summary>
    private static List<Op>? ShortestEdit(int[] a, int[] b, int maxEdits)
    {
        int n = a.Length, m = b.Length;
        if (n == 0 || m == 0)
        {
            var ops = new List<Op>(n + m);
            ops.AddRange(Enumerable.Repeat(Op.Delete, n));
            ops.AddRange(Enumerable.Repeat(Op.Insert, m));
            return ops;
        }

        var max = n + m;
        var offset = max + 1;
        var v = new int[(2 * max) + 3];

        // trace[d] holds v as it was before step d, for diagonals -d..d.
        var trace = new List<int[]>();
        for (var d = 0; d <= max && d <= maxEdits; d++)
        {
            var snapshot = new int[(2 * d) + 1];
            Array.Copy(v, offset - d, snapshot, 0, snapshot.Length);
            trace.Add(snapshot);

            for (var k = -d; k <= d; k += 2)
            {
                var x = k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1])
                    ? v[offset + k + 1]
                    : v[offset + k - 1] + 1;
                var y = x - k;
                while (x < n && y < m && a[x] == b[y])
                {
                    x++;
                    y++;
                }

                v[offset + k] = x;
                if (x >= n && y >= m)
                {
                    return Backtrack(trace, d, n, m);
                }
            }
        }

        return null;
    }

    private static List<Op> Backtrack(List<int[]> trace, int edits, int n, int m)
    {
        var ops = new List<Op>(n + m);
        int x = n, y = m;
        for (var d = edits; d > 0; d--)
        {
            var previous = trace[d];
            var k = x - y;
            var previousK = k == -d || (k != d && previous[k - 1 + d] < previous[k + 1 + d]) ? k + 1 : k - 1;
            var previousX = previous[previousK + d];
            var previousY = previousX - previousK;

            while (x > previousX && y > previousY)
            {
                ops.Add(Op.Equal);
                x--;
                y--;
            }

            ops.Add(x == previousX ? Op.Insert : Op.Delete);
            x = previousX;
            y = previousY;
        }

        while (x > 0 && y > 0)
        {
            ops.Add(Op.Equal);
            x--;
            y--;
        }

        ops.Reverse();
        return ops;
    }

    private static DiffResult BuildRows(List<Op> ops, IReadOnlyList<string> left, IReadOnlyList<string> right, bool simplified)
    {
        var rows = new List<DiffRow>(Math.Max(left.Count, right.Count));
        var deleted = new List<int>();
        var inserted = new List<int>();
        int i = 0, j = 0, differences = 0;

        void Flush()
        {
            if (deleted.Count == 0 && inserted.Count == 0)
            {
                return;
            }

            differences++;
            var paired = Math.Min(deleted.Count, inserted.Count);
            for (var p = 0; p < paired; p++)
            {
                rows.Add(new DiffRow(DiffKind.Changed, deleted[p] + 1, left[deleted[p]], inserted[p] + 1, right[inserted[p]]));
            }

            foreach (var d in deleted.Skip(paired))
            {
                rows.Add(new DiffRow(DiffKind.Removed, d + 1, left[d], null, null));
            }

            foreach (var n in inserted.Skip(paired))
            {
                rows.Add(new DiffRow(DiffKind.Added, null, null, n + 1, right[n]));
            }

            deleted.Clear();
            inserted.Clear();
        }

        foreach (var op in ops)
        {
            switch (op)
            {
                case Op.Equal:
                    Flush();
                    rows.Add(new DiffRow(DiffKind.Same, i + 1, left[i], j + 1, right[j]));
                    i++;
                    j++;
                    break;
                case Op.Delete:
                    deleted.Add(i++);
                    break;
                default:
                    inserted.Add(j++);
                    break;
            }
        }

        Flush();
        return new DiffResult(rows, differences, simplified);
    }
}

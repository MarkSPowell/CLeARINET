using Avalonia.Media;
using Violens.ProxyCore.Compare;

namespace Violens.DesktopUi.ViewModels;

/// <summary>
/// One row of the Compare Sessions window: a line from each side (or a gap),
/// with line numbers and colours. In a changed row, the part of each line
/// that differs is split out (<see cref="LeftChanged"/>, <see cref="RightChanged"/>)
/// so it can be highlighted.
/// </summary>
public sealed class CompareRow
{
    // Immutable brushes: rows are built off the UI thread.
    public static readonly IBrush RemovedBrush = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse("#3D1F27"));
    public static readonly IBrush AddedBrush = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse("#173A2A"));
    public static readonly IBrush ChangedBrush = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse("#2E2A17"));
    public static readonly IBrush GapBrush = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse("#0E111C"));

    /// <summary>The differing part of a changed line.</summary>
    public static readonly IBrush HighlightBrush = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse("#6B5A1A"));

    public required DiffKind Kind { get; init; }
    public string LeftNumber { get; init; } = string.Empty;
    public string LeftBefore { get; init; } = string.Empty;
    public string LeftChanged { get; init; } = string.Empty;
    public string LeftAfter { get; init; } = string.Empty;
    public IBrush LeftBackground { get; init; } = Brushes.Transparent;
    public string RightNumber { get; init; } = string.Empty;
    public string RightBefore { get; init; } = string.Empty;
    public string RightChanged { get; init; } = string.Empty;
    public string RightAfter { get; init; } = string.Empty;
    public IBrush RightBackground { get; init; } = Brushes.Transparent;

    public static CompareRow From(DiffRow row)
    {
        var left = row.Left ?? string.Empty;
        var right = row.Right ?? string.Empty;
        var leftNumber = row.LeftNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        var rightNumber = row.RightNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

        switch (row.Kind)
        {
            case DiffKind.Changed:
                var (start, leftLength, rightLength) = TextDiff.ChangedRange(left, right);
                return new CompareRow
                {
                    Kind = row.Kind,
                    LeftNumber = leftNumber,
                    LeftBefore = left[..start],
                    LeftChanged = left.Substring(start, leftLength),
                    LeftAfter = left[(start + leftLength)..],
                    LeftBackground = ChangedBrush,
                    RightNumber = rightNumber,
                    RightBefore = right[..start],
                    RightChanged = right.Substring(start, rightLength),
                    RightAfter = right[(start + rightLength)..],
                    RightBackground = ChangedBrush,
                };
            case DiffKind.Removed:
                return new CompareRow
                {
                    Kind = row.Kind,
                    LeftNumber = leftNumber,
                    LeftBefore = left,
                    LeftBackground = RemovedBrush,
                    RightBackground = GapBrush,
                };
            case DiffKind.Added:
                return new CompareRow
                {
                    Kind = row.Kind,
                    LeftBackground = GapBrush,
                    RightNumber = rightNumber,
                    RightBefore = right,
                    RightBackground = AddedBrush,
                };
            default:
                return new CompareRow
                {
                    Kind = row.Kind,
                    LeftNumber = leftNumber,
                    LeftBefore = left,
                    RightNumber = rightNumber,
                    RightBefore = right,
                };
        }
    }
}

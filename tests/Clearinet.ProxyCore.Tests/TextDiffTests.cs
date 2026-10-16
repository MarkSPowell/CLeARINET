using Clearinet.ProxyCore.Compare;
using Xunit;

namespace Clearinet.ProxyCore.Tests;

/// <summary>The line comparison behind Compare Sessions (<see cref="TextDiff"/>).</summary>
public class TextDiffTests
{
    [Fact]
    public void IdenticalTextsHaveNoDifferences()
    {
        var result = TextDiff.Compare("a\nb\nc", "a\r\nb\r\nc");

        Assert.True(result.Identical);
        Assert.All(result.Rows, row => Assert.Equal(DiffKind.Same, row.Kind));
        Assert.Equal(3, result.Rows.Count);
    }

    [Fact]
    public void AChangedLineIsPairedWithItsReplacement()
    {
        var result = TextDiff.Compare("GET /a\nHost: x\nAccept: */*", "GET /b\nHost: x\nAccept: */*");

        Assert.Equal(1, result.Differences);
        Assert.Equal(new DiffRow(DiffKind.Changed, 1, "GET /a", 1, "GET /b"), result.Rows[0]);
        Assert.Equal(DiffKind.Same, result.Rows[1].Kind);
    }

    [Fact]
    public void AddedAndRemovedLinesLineUp()
    {
        var result = TextDiff.Compare("a\nb\nc\nd", "a\nc\nd\ne");

        Assert.Equal(
            new[]
            {
                new DiffRow(DiffKind.Same, 1, "a", 1, "a"),
                new DiffRow(DiffKind.Removed, 2, "b", null, null),
                new DiffRow(DiffKind.Same, 3, "c", 2, "c"),
                new DiffRow(DiffKind.Same, 4, "d", 3, "d"),
                new DiffRow(DiffKind.Added, null, null, 4, "e"),
            },
            result.Rows);
        Assert.Equal(2, result.Differences);
        Assert.Equal(new[] { 1, 4 }, result.DifferenceStarts());
    }

    [Fact]
    public void ExtraLinesInAChangedBlockAreAdded()
    {
        var result = TextDiff.Compare("x\nold\ny", "x\nnew 1\nnew 2\ny");

        Assert.Equal(DiffKind.Changed, result.Rows[1].Kind);
        Assert.Equal(new DiffRow(DiffKind.Added, null, null, 3, "new 2"), result.Rows[2]);
        Assert.Equal(1, result.Differences);
    }

    [Fact]
    public void EmptySides()
    {
        Assert.True(TextDiff.Compare(string.Empty, string.Empty).Identical);

        var added = TextDiff.Compare(string.Empty, "a\nb");
        Assert.All(added.Rows, row => Assert.Equal(DiffKind.Added, row.Kind));
        Assert.Equal(1, added.Differences);
    }

    [Fact]
    public void KeepsTheSameLinesInVeryDifferentTexts()
    {
        // Every line of each side, with the shared lines kept in order.
        var left = string.Join('\n', Enumerable.Range(0, 300).Select(i => i % 3 == 0 ? $"same {i}" : $"left {i}"));
        var right = string.Join('\n', Enumerable.Range(0, 300).Select(i => i % 3 == 0 ? $"same {i}" : $"right {i}"));

        var result = TextDiff.Compare(left, right);

        Assert.False(result.Simplified);
        Assert.Equal(100, result.Rows.Count(r => r.Kind == DiffKind.Same));
        Assert.Equal(TextDiff.SplitLines(left), result.Rows.Where(r => r.Left is not null).Select(r => r.Left!));
        Assert.Equal(TextDiff.SplitLines(right), result.Rows.Where(r => r.Right is not null).Select(r => r.Right!));
    }

    [Fact]
    public void TooManyEditsIsShownAsOneBlock()
    {
        var left = string.Join('\n', Enumerable.Range(0, TextDiff.MaxEdits + 10).Select(i => $"a{i}"));
        var right = string.Join('\n', Enumerable.Range(0, TextDiff.MaxEdits + 10).Select(i => $"b{i}"));

        var result = TextDiff.Compare("top\n" + left + "\nbottom", "top\n" + right + "\nbottom");

        Assert.True(result.Simplified);
        Assert.Equal(1, result.Differences);
        Assert.Equal(DiffKind.Same, result.Rows[0].Kind);
        Assert.Equal(DiffKind.Same, result.Rows[^1].Kind);
    }

    [Fact]
    public void SplitLinesHandlesEveryLineEnding()
    {
        Assert.Equal(new[] { "a", "b", "c", string.Empty, "d" }, TextDiff.SplitLines("a\r\nb\nc\r\rd\n"));
        Assert.Empty(TextDiff.SplitLines(string.Empty));
    }

    [Theory]
    [InlineData("user=alice&x=1", "user=bob&x=1", 5, 5, 3)]
    [InlineData("abc", "abc", 3, 0, 0)]
    [InlineData("abc", "abXc", 2, 0, 1)]
    [InlineData("", "new", 0, 0, 3)]
    public void ChangedRangeFindsTheDifferingMiddle(string left, string right, int start, int leftLength, int rightLength)
    {
        Assert.Equal((start, leftLength, rightLength), TextDiff.ChangedRange(left, right));
    }

    [Fact]
    public void ChangedRangeDoesntSplitASurrogatePair()
    {
        // 😀 and 😁 share their high surrogate.
        var (start, leftLength, rightLength) = TextDiff.ChangedRange("a😀b", "a😁b");

        Assert.Equal(1, start);
        Assert.Equal(2, leftLength);
        Assert.Equal(2, rightLength);
    }
}

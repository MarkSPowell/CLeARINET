using Violens.AppCore.Preferences;
using Violens.ProxyCore.Preferences;
using Xunit;

namespace Violens.AppCore.Tests;

/// <summary>The session list's remembered column order and widths (<see cref="ColumnLayout"/>).</summary>
public class ColumnLayoutTests
{
    [Fact]
    public void FormatAndParseRoundTrip()
    {
        ColumnLayoutEntry[] entries = [new("#", 50), new("URL", null), new("script:A|B", 120.44)];

        var parsed = ColumnLayout.Parse(ColumnLayout.Format(entries));

        Assert.Equal(new ColumnLayoutEntry[] { new("#", 50), new("URL", null), new("script:A|B", 120.4) }, parsed);
    }

    [Fact]
    public void FormatDropsTheListSeparatorFromKeys()
    {
        var formatted = Assert.Single(ColumnLayout.Format([new ColumnLayoutEntry("flag:a;b", null)]));

        Assert.Equal("|flag:ab", formatted);
    }

    [Fact]
    public void ParseSkipsMalformedRepeatedAndNonsenseWidths()
    {
        var parsed = ColumnLayout.Parse(["no bar", "|", "80|Time", "90|time", "-5|Status", "abc|Method", "NaN|Req"]);

        Assert.Equal(
            new ColumnLayoutEntry[] { new("Time", 80), new("Status", null), new("Method", null), new("Req", null) },
            parsed);
    }

    [Fact]
    public void PreferenceStoreKeepsTheEntries()
    {
        var store = PreferenceStore.CreateInMemory();
        store.SetListPref("violens.ui.sessionlist.columns", ColumnLayout.Format([new ColumnLayoutEntry("Time", 80), new ColumnLayoutEntry("Image Bloat", null)]));

        var parsed = ColumnLayout.Parse(store.GetListPref("violens.ui.sessionlist.columns", []));

        Assert.Equal(new[] { new ColumnLayoutEntry("Time", 80), new ColumnLayoutEntry("Image Bloat", null) }, parsed);
    }

    [Fact]
    public void ArrangeAppliesTheSavedOrder()
    {
        var order = ColumnLayout.Arrange(["#", "Time", "Status", "URL"], ["URL", "#", "Status", "Time"]);

        Assert.Equal(new[] { 3, 0, 2, 1 }, order);
    }

    [Fact]
    public void ArrangeLeavesColumnsWithNoSavedPlaceWhereTheyAre()
    {
        // "New" isn't in the saved layout: it keeps slot 1, and the others
        // take the saved order in the remaining slots.
        var order = ColumnLayout.Arrange(["#", "New", "Time", "URL"], ["URL", "Time", "#"]);

        Assert.Equal(new[] { 3, 1, 2, 0 }, order);
    }

    [Fact]
    public void ArrangeIgnoresSavedColumnsThatArentThere()
    {
        var order = ColumnLayout.Arrange(["#", "URL"], ["script:Gone", "URL", "#"]);

        Assert.Equal(new[] { 1, 0 }, order);
    }

    [Fact]
    public void MergeKeepsAnAbsentColumnsPlace()
    {
        // A script column was after Time; the script isn't loaded now, and
        // the user has since widened URL.
        ColumnLayoutEntry[] saved = [new("#", 50), new("Time", 80), new("script:Score", 60), new("URL", null)];
        ColumnLayoutEntry[] current = [new("#", 50), new("Time", 80), new("URL", 400)];

        var merged = ColumnLayout.Merge(saved, current);

        Assert.Equal(
            new ColumnLayoutEntry[] { new("#", 50), new("Time", 80), new("script:Score", 60), new("URL", 400) },
            merged);
    }

    [Fact]
    public void MergePutsAnAbsentFirstColumnFirst()
    {
        var merged = ColumnLayout.Merge([new("flag:x", 30), new("#", 50)], [new("#", 60)]);

        Assert.Equal(new ColumnLayoutEntry[] { new("flag:x", 30), new("#", 60) }, merged);
    }

    [Fact]
    public void MergeWithNothingSavedIsTheCurrentLayout()
    {
        ColumnLayoutEntry[] current = [new("#", 50), new("URL", null)];

        Assert.Equal(current, ColumnLayout.Merge([], current));
    }
}

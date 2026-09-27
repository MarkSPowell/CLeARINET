using Clearinet.Compatibility.Extensions;
using Xunit;
using ShimImporter = Clearinet.CompatShim.ISessionImporter;
using ShimProfferFormat = Clearinet.CompatShim.ProfferFormatAttribute;
using ShimProgress = Clearinet.CompatShim.ProgressCallbackEventArgs;
using ShimSession = Clearinet.CompatShim.Session;

namespace Clearinet.Compatibility.Tests;

/// <summary>
/// What File &gt; Import/Export via Extension offers in its format picker
/// (<see cref="ProfferedFormats.ChoicesFrom"/>), and which entry it
/// highlights (<see cref="ProfferedFormats.Preselect"/>). The dialog itself
/// is Avalonia UI and isn't covered here.
/// </summary>
public class FormatChoiceTests
{
    [ProfferFormat("Alpha", "The first format")]
    [ProfferFormat("Beta", "The second format")]
    private sealed class TwoFormatImporter : ISessionImporter
    {
        public IReadOnlyList<ImportedSession> ImportSessions(string importFormat, IReadOnlyDictionary<string, object> options, Action<ProgressCallbackEventArgs>? progress) => [];

        public void Dispose()
        {
        }
    }

    private sealed class UnlabelledImporter : ISessionImporter
    {
        public IReadOnlyList<ImportedSession> ImportSessions(string importFormat, IReadOnlyDictionary<string, object> options, Action<ProgressCallbackEventArgs>? progress) => [];

        public void Dispose()
        {
        }
    }

    [ShimProfferFormat("NetLog-ish", "A ported format", ".json;.gz")]
    private sealed class PortedImporter : ShimImporter
    {
        public ShimSession[]? ImportSessions(string sImportFormat, Dictionary<string, object> dictOptions, EventHandler<ShimProgress>? evtProgressNotifications) => null;

        public void Dispose()
        {
        }
    }

    [Fact]
    public void EachProfferedFormatIsItsOwnChoiceInLoadOrder()
    {
        var twoFormats = new TwoFormatImporter();
        var ported = new CompatShimImporterAdapter(new PortedImporter());

        var choices = ProfferedFormats.ChoicesFrom([twoFormats, ported]);

        Assert.Equal(new[] { "Alpha", "Beta", "NetLog-ish" }, choices.Select(c => c.DisplayName).ToArray());
        Assert.Same(twoFormats, choices[0].Handler);
        Assert.Same(twoFormats, choices[1].Handler);

        // The adapter is what gets called, but the label names the ported
        // extension's own assembly, not CLeARINET's.
        Assert.Same(ported, choices[2].Handler);
        Assert.Equal(typeof(PortedImporter).Assembly.GetName().Name, choices[2].Source);
        Assert.Contains(".json, .gz", choices[2].SourceLabel);
    }

    [Fact]
    public void AnImporterWithNoDeclaredFormatStillAppearsUnderItsTypeName()
    {
        var choice = Assert.Single(ProfferedFormats.ChoicesFrom([new UnlabelledImporter()]));

        Assert.Equal(nameof(UnlabelledImporter), choice.DisplayName);
        Assert.Equal(string.Empty, choice.Format.Name);
        Assert.False(choice.HasDescription);
    }

    [Fact]
    public void KeysAreDistinctAndStableAcrossInstances()
    {
        var first = ProfferedFormats.ChoicesFrom([new TwoFormatImporter()]);
        var second = ProfferedFormats.ChoicesFrom([new TwoFormatImporter()]);

        Assert.Equal(first.Select(c => c.Key), second.Select(c => c.Key));
        Assert.Equal(first.Count, first.Select(c => c.Key).Distinct().Count());
    }

    [Fact]
    public void PreselectPrefersTheRememberedChoiceThenTheFirst()
    {
        var choices = ProfferedFormats.ChoicesFrom([new TwoFormatImporter()]);

        Assert.Equal("Beta", ProfferedFormats.Preselect(choices, choices[1].Key)?.DisplayName);
        Assert.Equal("Alpha", ProfferedFormats.Preselect(choices, "no-longer-installed|Gamma")?.DisplayName);
        Assert.Equal("Alpha", ProfferedFormats.Preselect(choices, null)?.DisplayName);
        Assert.Null(ProfferedFormats.Preselect([], "anything"));
    }
}

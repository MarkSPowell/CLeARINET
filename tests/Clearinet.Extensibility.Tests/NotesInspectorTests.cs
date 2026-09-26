using Clearinet.Extensibility.Inspection;
using Clearinet.Extensibility.Inspection.Inspectors;
using Clearinet.ProxyCore;
using Clearinet.ProxyCore.Http;
using Clearinet.ProxyCore.Sessions;
using Xunit;

namespace Clearinet.Extensibility.Tests;

/// <summary>
/// The Notes tab: a session's Fiddler-style flags, such as the ones Eric
/// Lawrence's NetLog importer attaches.
/// </summary>
public class NotesInspectorTests
{
    private static Session SessionWith(IReadOnlyDictionary<string, string>? flags) =>
        new(
            1,
            "example.test",
            DateTimeOffset.UnixEpoch,
            new CapturedRequest("GET", "/", "HTTP/1.1", [], []),
            new CapturedResponse("HTTP/1.1", 200, "OK", [], []),
            SessionState.Done,
            flags);

    [Fact]
    public void AppearsOnlyOnTheRequestSideOfSessionsWithNotes()
    {
        var inspector = new NotesInspector();
        var withNotes = SessionWith(new Dictionary<string, string> { ["x-processinfo"] = "msedge:0" });

        Assert.True(inspector.CanInspect(new InspectorContext(withNotes, InspectorSide.Request)));
        Assert.False(inspector.CanInspect(new InspectorContext(withNotes, InspectorSide.Response)));
        Assert.False(inspector.CanInspect(new InspectorContext(SessionWith(null), InspectorSide.Request)));
        Assert.False(inspector.CanInspect(new InspectorContext(SessionWith(new Dictionary<string, string>()), InspectorSide.Request)));
    }

    [Fact]
    public void ListsNotesSortedWithExplanationsForKnownOnes()
    {
        var session = SessionWith(new Dictionary<string, string>
        {
            ["x-responsebodytransferlength"] = "42",
            ["some-extension.note"] = "custom value",
            ["X-Netlog-URLRequest-ID"] = "101",
        });

        var content = Assert.IsType<KeyValueContent>(new NotesInspector().Inspect(new InspectorContext(session, InspectorSide.Request)));

        Assert.Equal(
            new[] { "some-extension.note", "X-Netlog-URLRequest-ID", "x-responsebodytransferlength" },
            content.Rows.Select(r => r.Name).ToArray());
        Assert.Equal("custom value", content.Rows[0].Value);
        Assert.StartsWith("101  (", content.Rows[1].Value);
        Assert.Contains("didn't include the body", content.Rows[2].Value);
    }

    [Fact]
    public void IsOneOfTheBuiltInsAndSortsAfterHex()
    {
        var registry = InspectorRegistry.CreateDefault();
        var session = SessionWith(new Dictionary<string, string> { ["ui-comments"] = "hello" });

        var applicable = registry.GetApplicable(new InspectorContext(session, InspectorSide.Request));

        Assert.Equal(new[] { "Headers", "Raw", "Hex", "Notes" }, applicable.Select(i => i.DisplayName).ToArray());
    }
}

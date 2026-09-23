using Clearinet.Compatibility.FiddlerScript;
using Xunit;

namespace Clearinet.Compatibility.Tests;

/// <summary>
/// Exercises <see cref="FiddlerScriptPreferenceStore"/> against a real temp
/// file (never the real <c>%LocalAppData%\CLeARINET\</c> path -- see the
/// constructor's own <c>filePath</c> parameter, added specifically so tests
/// don't touch a real machine's app-data folder), cleaning up after itself
/// in a <c>finally</c> block the same way <c>SampleSessionExporterTests</c>
/// does for its own output file.
/// </summary>
public class FiddlerScriptPreferenceStoreTests
{
    private static string NewTempFilePath() =>
        Path.Combine(Path.GetTempPath(), $"clearinet-fiddlerscript-prefs-test-{Guid.NewGuid():N}.json");

    [Fact]
    public void GetOnAPreferenceThatWasNeverSetReturnsNull()
    {
        var path = NewTempFilePath();
        try
        {
            var store = new FiddlerScriptPreferenceStore(path);

            Assert.Null(store.Get("never.set"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SetThenGetOnTheSameInstanceRoundTrips()
    {
        var path = NewTempFilePath();
        try
        {
            var store = new FiddlerScriptPreferenceStore(path);

            store.Set("clearinet.example.pref", "hello");

            Assert.Equal("hello", store.Get("clearinet.example.pref"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ANonEphemeralPreferenceSurvivesANewInstanceAgainstTheSameFile()
    {
        var path = NewTempFilePath();
        try
        {
            new FiddlerScriptPreferenceStore(path).Set("clearinet.example.persisted", "true");

            var reloaded = new FiddlerScriptPreferenceStore(path);

            Assert.Equal("true", reloaded.Get("clearinet.example.persisted"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AnEphemeralPreferenceDoesNotSurviveANewInstance()
    {
        // Matches real Fiddler's own documented "fiddlerscript.ephemeral.*"
        // convention -- see BindPrefBinding's own remarks: kept for one
        // running FiddlerScriptRunner (one store instance), not written to
        // disk at all, so a fresh instance against the same file never
        // sees it.
        var path = NewTempFilePath();
        try
        {
            new FiddlerScriptPreferenceStore(path).Set("fiddlerscript.ephemeral.example", "should-not-persist");

            var reloaded = new FiddlerScriptPreferenceStore(path);

            Assert.Null(reloaded.Get("fiddlerscript.ephemeral.example"));
            // Nothing else was ever set, so an ephemeral-only store writes
            // no file at all.
            Assert.False(File.Exists(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AnEphemeralPreferenceDoesSurviveOnTheSameInstance()
    {
        var path = NewTempFilePath();
        try
        {
            var store = new FiddlerScriptPreferenceStore(path);
            store.Set("fiddlerscript.ephemeral.example", "still-here");

            Assert.Equal("still-here", store.Get("fiddlerscript.ephemeral.example"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AMissingFileIsTreatedAsAnEmptyStoreNotAnError()
    {
        var path = NewTempFilePath();

        var ex = Record.Exception(() => new FiddlerScriptPreferenceStore(path));

        Assert.Null(ex);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void ACorruptFileIsTreatedAsAnEmptyStoreNotAnError()
    {
        var path = NewTempFilePath();
        try
        {
            File.WriteAllText(path, "{ this is not valid json");

            var store = new FiddlerScriptPreferenceStore(path);

            Assert.Null(store.Get("anything"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

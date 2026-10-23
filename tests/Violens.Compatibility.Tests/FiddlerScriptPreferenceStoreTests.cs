using Violens.Compatibility.FiddlerScript;
using Xunit;

namespace Violens.Compatibility.Tests;

/// <summary>Against a temp file, never the real app-data folder.</summary>
public class FiddlerScriptPreferenceStoreTests
{
    private static string NewTempFilePath() =>
        Path.Combine(Path.GetTempPath(), $"violens-fiddlerscript-prefs-test-{Guid.NewGuid():N}.json");

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

            store.Set("violens.example.pref", "hello");

            Assert.Equal("hello", store.Get("violens.example.pref"));
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
            new FiddlerScriptPreferenceStore(path).Set("violens.example.persisted", "true");

            var reloaded = new FiddlerScriptPreferenceStore(path);

            Assert.Equal("true", reloaded.Get("violens.example.persisted"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AnEphemeralPreferenceDoesNotSurviveANewInstance()
    {
        // Fiddler's "fiddlerscript.ephemeral.*" convention: kept in memory, never
        // written, so a new store on the same file doesn't see it.
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

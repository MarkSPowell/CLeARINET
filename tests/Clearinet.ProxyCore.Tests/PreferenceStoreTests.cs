using System.Globalization;
using System.Text;
using Clearinet.ProxyCore.Preferences;
using Xunit;

namespace Clearinet.ProxyCore.Tests;

/// <summary>
/// Exercises <see cref="PreferenceStore"/> against real files in a fresh
/// temp folder per test -- never the real app-data folder. CI runs this on
/// both windows-latest and macos-latest, which is what backs the
/// Preferences Design doc's "Platform parity checklist": every row marked
/// "test on both legs" is one of the tests below.
///
/// Stores are built with a one-hour save delay and flushed explicitly, so
/// every write happens exactly when the test says (the one test of the
/// background save itself is the exception, and polls).
/// </summary>
public sealed class PreferenceStoreTests : IDisposable
{
    private static readonly TimeSpan ManualSaveOnly = TimeSpan.FromHours(1);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "clearinet-prefs-tests", Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_folder, PreferenceStore.DefaultFileName);

    private PreferenceStore NewStore(List<string>? log = null) =>
        new(FilePath, ManualSaveOnly, log is null ? null : new Action<string>(log.Add));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    [Fact]
    public void AMissingFileLoadsAsEmptyAndCreatesNothing()
    {
        using var store = NewStore();

        Assert.Null(store["clearinet.anything"]);
        Assert.Equal("fallback", store.GetStringPref("clearinet.anything", "fallback"));
        Assert.False(store.IsReadOnly);
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public void TypedValuesRoundTripThroughANewInstance()
    {
        using (var store = NewStore())
        {
            store.SetStringPref("clearinet.test.string", "hello");
            store.SetBoolPref("clearinet.test.bool", true);
            store.SetInt32Pref("clearinet.test.int", 8888);
            store.SetInt32Pref("clearinet.test.negative", -42);
            store.SetListPref("clearinet.config.test.list", ["a.exe", "b.exe"]);
            store.Flush();
        }

        using var reloaded = NewStore();

        Assert.Equal("hello", reloaded.GetStringPref("clearinet.test.string", ""));
        Assert.True(reloaded.GetBoolPref("clearinet.test.bool", false));
        Assert.Equal(8888, reloaded.GetInt32Pref("clearinet.test.int", 0));
        Assert.Equal(-42, reloaded.GetInt32Pref("clearinet.test.negative", 0));
        Assert.Equal(new[] { "a.exe", "b.exe" }, reloaded.GetListPref("clearinet.config.test.list", []).ToArray());
    }

    [Fact]
    public void DisposeSavesUnflushedChanges()
    {
        using (var store = NewStore())
        {
            store.SetBoolPref("clearinet.test.bool", true);
        }

        using var reloaded = NewStore();
        Assert.True(reloaded.GetBoolPref("clearinet.test.bool", false));
    }

    [Fact]
    public void TheBackgroundSaveWritesWithoutAnExplicitFlush()
    {
        using var store = new PreferenceStore(FilePath, TimeSpan.FromMilliseconds(10));
        store.SetStringPref("clearinet.test.string", "saved-in-background");

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!File.Exists(FilePath) && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(20);
        }

        Assert.Contains("saved-in-background", File.ReadAllText(FilePath));
    }

    [Fact]
    public void NamesAreCaseInsensitiveAndKeepTheirFirstCasing()
    {
        using (var store = NewStore())
        {
            store.SetStringPref("Clearinet.Test.Mixed", "one");
            store.SetStringPref("clearinet.test.mixed", "two");

            Assert.Equal("two", store["CLEARINET.TEST.MIXED"]);
            store.Flush();
        }

        var text = File.ReadAllText(FilePath);
        Assert.Contains("\"Clearinet.Test.Mixed\"", text);
        Assert.DoesNotContain("\"clearinet.test.mixed\"", text);
    }

    [Fact]
    public void NumbersAndBoolsAreCultureInvariant()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            // Arabic (Saudi Arabia) and German both differ from en-US in
            // number formatting; neither may leak into the file.
            foreach (var culture in new[] { "de-DE", "ar-SA" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);

                using (var store = NewStore())
                {
                    store.SetInt32Pref("clearinet.test.int", -1234567);
                    store.SetBoolPref("clearinet.test.bool", false);
                    store.Flush();
                }

                var text = File.ReadAllText(FilePath);
                Assert.Contains("\"-1234567\"", text);
                Assert.Contains("\"False\"", text);

                using var reloaded = NewStore();
                Assert.Equal(-1234567, reloaded.GetInt32Pref("clearinet.test.int", 0));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("False", false)]
    [InlineData(" true ", true)]
    public void BoolsParseCaseInsensitively(string stored, bool expected)
    {
        using var store = PreferenceStore.CreateInMemory();
        store.SetStringPref("clearinet.test.bool", stored);

        Assert.Equal(expected, store.GetBoolPref("clearinet.test.bool", !expected));
    }

    [Fact]
    public void UnparseableValuesReturnTheDefaultRatherThanThrowing()
    {
        using var store = PreferenceStore.CreateInMemory();
        store.SetStringPref("clearinet.test.value", "not-a-number");

        Assert.Equal(7, store.GetInt32Pref("clearinet.test.value", 7));
        Assert.True(store.GetBoolPref("clearinet.test.value", true));
    }

    [Fact]
    public void ListsAreTrimmedDedupedAndEmptyEntriesDropped()
    {
        using var store = PreferenceStore.CreateInMemory();
        store.SetStringPref("clearinet.config.processnames.browsers", " msedge.exe; ;chrome.exe;MSEDGE.EXE;firefox.exe; ");

        Assert.Equal(
            new[] { "msedge.exe", "chrome.exe", "firefox.exe" },
            store.GetListPref("clearinet.config.processnames.browsers", []).ToArray());
    }

    [Fact]
    public void AnExplicitlyEmptyListOverridesTheDefault()
    {
        using var store = PreferenceStore.CreateInMemory();
        store.SetListPref("clearinet.config.test.list", []);

        Assert.Empty(store.GetListPref("clearinet.config.test.list", ["default.exe"]));
        Assert.Equal(new[] { "default.exe" }, store.GetListPref("clearinet.config.test.unset", ["default.exe"]).ToArray());
    }

    [Fact]
    public void SetListPrefRejectsEntriesContainingTheSeparator()
    {
        using var store = PreferenceStore.CreateInMemory();

        Assert.Throws<ArgumentException>(() => store.SetListPref("clearinet.config.test.list", ["a;b"]));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("has space")]
    [InlineData(".leading")]
    [InlineData("trailing.")]
    [InlineData("tab\tinside")]
    public void InvalidNamesThrowOnWriteAndReadAsUnset(string name)
    {
        using var store = PreferenceStore.CreateInMemory();

        Assert.Throws<ArgumentException>(() => store.SetStringPref(name, "x"));
        Assert.Null(store[name]);
        Assert.Equal("default", store.GetStringPref(name, "default"));
    }

    [Fact]
    public void EphemeralPreferencesLiveInMemoryOnly()
    {
        using (var store = NewStore())
        {
            store.SetStringPref("fiddlerscript.ephemeral.example", "memory-only");
            Assert.Equal("memory-only", store["fiddlerscript.ephemeral.example"]);
            store.Flush();
        }

        Assert.False(File.Exists(FilePath));
        using var reloaded = NewStore();
        Assert.Null(reloaded["fiddlerscript.ephemeral.example"]);
    }

    [Fact]
    public void RemovePrefIsPersisted()
    {
        using (var store = NewStore())
        {
            store.SetStringPref("clearinet.test.keep", "yes");
            store.SetStringPref("clearinet.test.remove", "soon-gone");
            store.Flush();
            store.RemovePref("clearinet.test.remove");
            store.Flush();
        }

        using var reloaded = NewStore();
        Assert.Equal("yes", reloaded["clearinet.test.keep"]);
        Assert.Null(reloaded["clearinet.test.remove"]);
    }

    [Fact]
    public void TheFileIsTheSameBytesOnEveryPlatform()
    {
        using (var store = NewStore())
        {
            store.SetStringPref("clearinet.b", "2");
            store.SetStringPref("clearinet.a", "1");
            store.SetStringPref("Clearinet.C", "3");
            store.Flush();
        }

        var bytes = File.ReadAllBytes(FilePath);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "No UTF-8 BOM.");
        Assert.DoesNotContain((byte)'\r', bytes);

        const string expected =
            "{\n" +
            "  \"formatVersion\": 1,\n" +
            "  \"preferences\": {\n" +
            "    \"clearinet.a\": \"1\",\n" +
            "    \"clearinet.b\": \"2\",\n" +
            "    \"Clearinet.C\": \"3\"\n" +
            "  }\n" +
            "}\n";
        Assert.Equal(expected, Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void UnknownKeysAreKeptAcrossASave()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, """
            {
              "formatVersion": 1,
              "preferences": {
                "clearinet.from.a.newer.build": "keep-me",
                "someextension.setting": "keep-me-too"
              }
            }
            """);

        using (var store = NewStore())
        {
            store.SetStringPref("clearinet.test.mine", "mine");
            store.Flush();
        }

        var text = File.ReadAllText(FilePath);
        Assert.Contains("clearinet.from.a.newer.build", text);
        Assert.Contains("someextension.setting", text);
        Assert.Contains("clearinet.test.mine", text);
    }

    [Fact]
    public void HandEditedUnquotedValuesAreAccepted()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, """
            {
              // a comment someone added by hand
              "formatVersion": 1,
              "preferences": {
                "clearinet.proxy.port": 9999,
                "clearinet.proxy.port.auto": false,
              }
            }
            """);

        using var store = NewStore();

        Assert.Equal(9999, store.GetInt32Pref("clearinet.proxy.port", 0));
        Assert.False(store.GetBoolPref("clearinet.proxy.port.auto", true));
    }

    [Fact]
    public void ACorruptFileIsMovedAsideAndDefaultsAreUsed()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, "{ this is not valid json");
        var log = new List<string>();

        using var store = NewStore(log);

        Assert.Null(store["clearinet.anything"]);
        Assert.False(File.Exists(FilePath));
        var aside = Assert.Single(Directory.GetFiles(_folder, PreferenceStore.DefaultFileName + ".corrupt-*"));
        Assert.Equal("{ this is not valid json", File.ReadAllText(aside));
        Assert.NotEmpty(log);
    }

    [Theory]
    [InlineData("""{ "clearinet.proxy.port": "8888" }""")]
    [InlineData("""{ "formatVersion": "1", "preferences": { "clearinet.proxy.port": "8888" } }""")]
    [InlineData("""{ "formatVersion": 1, "preferences": [] }""")]
    [InlineData("[]")]
    public void TheWrongShapeCountsAsCorrupt(string contents)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, contents);

        using var store = NewStore();

        Assert.Null(store["clearinet.proxy.port"]);
        Assert.Single(Directory.GetFiles(_folder, PreferenceStore.DefaultFileName + ".corrupt-*"));
    }

    [Fact]
    public void ANewerFormatVersionLoadsReadOnlyAndIsNeverOverwritten()
    {
        Directory.CreateDirectory(_folder);
        var newer = """
            {
              "formatVersion": 2,
              "preferences": { "clearinet.proxy.port": "7777" }
            }
            """;
        File.WriteAllText(FilePath, newer);

        using (var store = NewStore())
        {
            Assert.True(store.IsReadOnly);
            Assert.Equal(7777, store.GetInt32Pref("clearinet.proxy.port", 0));

            store.SetInt32Pref("clearinet.proxy.port", 1234);
            Assert.Equal(1234, store.GetInt32Pref("clearinet.proxy.port", 0));
            store.Flush();
        }

        Assert.Equal(newer, File.ReadAllText(FilePath));
    }

    [Fact]
    public void TwoInstancesChangingDifferentNamesKeepBothChanges()
    {
        using var first = NewStore();
        using var second = NewStore();

        first.SetInt32Pref("clearinet.proxy.port", 9000);
        second.SetBoolPref("clearinet.ui.panels.extensions", true);
        first.Flush();
        second.Flush();

        using var reloaded = NewStore();
        Assert.Equal(9000, reloaded.GetInt32Pref("clearinet.proxy.port", 0));
        Assert.True(reloaded.GetBoolPref("clearinet.ui.panels.extensions", false));
    }

    [Fact]
    public void TwoInstancesChangingTheSameNameLastSaveWins()
    {
        using var first = NewStore();
        using var second = NewStore();

        first.SetInt32Pref("clearinet.proxy.port", 9000);
        second.SetInt32Pref("clearinet.proxy.port", 9001);
        second.Flush();
        first.Flush();

        using var reloaded = NewStore();
        Assert.Equal(9000, reloaded.GetInt32Pref("clearinet.proxy.port", 0));
    }

    [Fact]
    public void ARemovalInOneInstanceDoesNotEraseAnotherInstancesNewName()
    {
        using (var seed = NewStore())
        {
            seed.SetStringPref("clearinet.test.old", "x");
            seed.Flush();
        }

        using var first = NewStore();
        using var second = NewStore();

        first.RemovePref("clearinet.test.old");
        second.SetStringPref("clearinet.test.new", "y");
        second.Flush();
        first.Flush();

        using var reloaded = NewStore();
        Assert.Null(reloaded["clearinet.test.old"]);
        Assert.Equal("y", reloaded["clearinet.test.new"]);
    }

    [Fact]
    public void WatchersSeeMatchingChangesOnly()
    {
        using var store = PreferenceStore.CreateInMemory();
        var seen = new List<PrefChangeEventArgs>();
        store.AddWatcher("clearinet.ui.", (_, e) => seen.Add(e));

        store.SetBoolPref("clearinet.ui.panels.extensions", true);
        store.SetInt32Pref("clearinet.proxy.port", 8888);
        store.SetBoolPref("CLEARINET.UI.panels.extensions", true); // unchanged: no event

        var change = Assert.Single(seen);
        Assert.Equal("clearinet.ui.panels.extensions", change.PrefName);
        Assert.Null(change.OldValueString);
        Assert.Equal("True", change.ValueString);
        Assert.True(change.ValueBool);
    }

    [Fact]
    public async Task AWatcherMayWriteBackIntoTheStoreWithoutDeadlocking()
    {
        using var store = PreferenceStore.CreateInMemory();
        store.AddWatcher("clearinet.test.source", (sender, e) =>
            ((IPreferenceStore)sender!).SetStringPref("clearinet.test.mirror", e.ValueString ?? ""));

        var write = Task.Run(() => store.SetStringPref("clearinet.test.source", "echo"));

        var finished = await Task.WhenAny(write, Task.Delay(TimeSpan.FromSeconds(10))) == write;
        Assert.True(finished, "Re-entrant write deadlocked.");
        await write;
        Assert.Equal("echo", store["clearinet.test.mirror"]);
    }

    [Fact]
    public void AThrowingWatcherDoesNotStopOtherWatchersOrTheWrite()
    {
        var log = new List<string>();
        using var store = new PreferenceStore(FilePath, ManualSaveOnly, log.Add);
        var secondRan = false;
        store.AddWatcher("", (_, _) => throw new InvalidOperationException("boom"));
        store.AddWatcher("", (_, _) => secondRan = true);

        store.SetStringPref("clearinet.test.value", "x");

        Assert.True(secondRan);
        Assert.Equal("x", store["clearinet.test.value"]);
        Assert.Contains(log, line => line.Contains("boom"));
    }

    [Fact]
    public void RemovedWatchersStopReceivingChanges()
    {
        using var store = PreferenceStore.CreateInMemory();
        var count = 0;
        var watcher = store.AddWatcher("", (_, _) => count++);

        store.SetStringPref("clearinet.test.value", "1");
        store.RemoveWatcher(watcher);
        store.SetStringPref("clearinet.test.value", "2");

        Assert.Equal(1, count);
    }

    [Fact]
    public void ConcurrentReadsAndWritesAreSafe()
    {
        using var store = NewStore();

        Parallel.For(0, 64, i =>
        {
            store.SetInt32Pref($"clearinet.test.n{i % 8}", i);
            _ = store.GetInt32Pref($"clearinet.test.n{(i + 1) % 8}", -1);
            if (i % 16 == 0)
            {
                store.Flush();
            }
        });
        store.Flush();

        using var reloaded = NewStore();
        for (var n = 0; n < 8; n++)
        {
            Assert.Equal(store[$"clearinet.test.n{n}"], reloaded[$"clearinet.test.n{n}"]);
        }
    }
}

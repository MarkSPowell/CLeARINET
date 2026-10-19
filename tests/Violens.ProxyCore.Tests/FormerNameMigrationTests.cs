using Violens.ProxyCore.Preferences;
using Xunit;

namespace Violens.ProxyCore.Tests;

/// <summary>
/// The first-run carry-over from CLeARINET (the app's former name), against
/// throwaway folders standing in for the app-data and Documents roots.
/// </summary>
public sealed class FormerNameMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "violens-migration-" + Guid.NewGuid().ToString("N"));

    private string AppData => Path.Combine(_root, "AppData");
    private string Documents => Path.Combine(_root, "Documents");
    private string OldAppData => Path.Combine(AppData, "CLeARINET");
    private string NewAppData => Path.Combine(AppData, "VioLens");

    public FormerNameMigrationTests()
    {
        Directory.CreateDirectory(AppData);
        Directory.CreateDirectory(Documents);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private const string OldPreferences = """
        {
          "formatVersion": 1,
          "preferences": {
            "clearinet.proxy.port": "8899",
            "clearinet.ui.filter.text": "host:example.com",
            "ClearinetCSP.enabled": "true",
            "fiddler.something": "kept"
          }
        }
        """;

    private void WriteOld(string relative, string content)
    {
        var path = Path.Combine(OldAppData, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void NothingHappensWithoutAnOldFolder()
    {
        var result = FormerNameMigration.Run(AppData, Documents);

        Assert.False(result.DidAnything);
        Assert.False(Directory.Exists(NewAppData));
    }

    [Fact]
    public void CopiesSettingsAndRenamesPreferences()
    {
        WriteOld("preferences.json", OldPreferences);
        WriteOld("autoresponder-rules.json", "[]");
        WriteOld("fiddlerscript-prefs.json", "{}");
        WriteOld(Path.Combine("LegacyExtensionHost", "settings.txt"), "x");
        WriteOld("preferences.lock", "");

        var result = FormerNameMigration.Run(AppData, Documents);

        Assert.True(result.CopiedAppData);
        Assert.Equal(3, result.RenamedPreferences);
        Assert.True(File.Exists(Path.Combine(NewAppData, "autoresponder-rules.json")));
        Assert.True(File.Exists(Path.Combine(NewAppData, "fiddlerscript-prefs.json")));
        Assert.True(File.Exists(Path.Combine(NewAppData, "LegacyExtensionHost", "settings.txt")));
        Assert.False(File.Exists(Path.Combine(NewAppData, "preferences.lock")));

        // Copied, not moved, and the old folder says where things went.
        Assert.True(File.Exists(Path.Combine(OldAppData, "preferences.json")));
        Assert.Contains(NewAppData, File.ReadAllText(Path.Combine(OldAppData, FormerNameMigration.NoteFileName)));

        // The real store reads the renamed file.
        using var store = new PreferenceStore(Path.Combine(NewAppData, "preferences.json"));
        Assert.Equal("8899", store["violens.proxy.port"]);
        Assert.Equal("host:example.com", store["violens.ui.filter.text"]);
        Assert.Equal("true", store["ViolensCSP.enabled"]);
        Assert.Equal("kept", store["fiddler.something"]);
        Assert.Null(store["clearinet.proxy.port"]);
    }

    [Fact]
    public void NeverOverwritesAnExistingVioLensFolder()
    {
        WriteOld("preferences.json", OldPreferences);
        Directory.CreateDirectory(NewAppData);
        File.WriteAllText(Path.Combine(NewAppData, "preferences.json"), "mine");

        var result = FormerNameMigration.Run(AppData, Documents);

        Assert.False(result.CopiedAppData);
        Assert.Equal("mine", File.ReadAllText(Path.Combine(NewAppData, "preferences.json")));
    }

    [Fact]
    public void RunsOnlyOnce()
    {
        WriteOld("preferences.json", OldPreferences);
        Assert.True(FormerNameMigration.Run(AppData, Documents).CopiedAppData);

        // Even if the new folder goes away, the note stops a second copy.
        Directory.Delete(NewAppData, recursive: true);
        Assert.False(FormerNameMigration.Run(AppData, Documents).DidAnything);
    }

    [Fact]
    public void MovesALeftoverSystemProxyBackupSoItIsRestored()
    {
        WriteOld("preferences.json", OldPreferences);
        WriteOld("system-proxy-backup.json", "{\"backup\":1}");

        var result = FormerNameMigration.Run(AppData, Documents);

        Assert.Equal("system-proxy-backup.json", Assert.Single(result.MovedBackups));
        Assert.Equal("{\"backup\":1}", File.ReadAllText(Path.Combine(NewAppData, "system-proxy-backup.json")));
        Assert.False(File.Exists(Path.Combine(OldAppData, "system-proxy-backup.json")));

        // Moving the backup created the new folder; the settings still came too.
        Assert.True(result.CopiedAppData);
        Assert.True(File.Exists(Path.Combine(NewAppData, "preferences.json")));
    }

    [Fact]
    public void MovesABackupEvenAfterTheSettingsWereCopied()
    {
        WriteOld("preferences.json", OldPreferences);
        FormerNameMigration.Run(AppData, Documents);

        // The old app ran again and didn't shut down cleanly.
        WriteOld("system-proxy-backup-macos.json", "{}");
        var result = FormerNameMigration.Run(AppData, Documents);

        Assert.Equal("system-proxy-backup-macos.json", Assert.Single(result.MovedBackups));
        Assert.False(result.CopiedAppData);
    }

    [Fact]
    public void LeavesAnOldBackupWhenVioLensHasItsOwn()
    {
        WriteOld("system-proxy-backup.json", "old");
        Directory.CreateDirectory(NewAppData);
        File.WriteAllText(Path.Combine(NewAppData, "system-proxy-backup.json"), "new");

        var result = FormerNameMigration.Run(AppData, Documents);

        Assert.Empty(result.MovedBackups);
        Assert.Equal("new", File.ReadAllText(Path.Combine(NewAppData, "system-proxy-backup.json")));
    }

    [Fact]
    public void CopiesExtensionsFromDocuments()
    {
        var oldExtensions = Path.Combine(Documents, "CLeARINET", "Extensions");
        Directory.CreateDirectory(Path.Combine(oldExtensions, "licenses"));
        File.WriteAllText(Path.Combine(oldExtensions, "Mine.dll"), "dll");
        File.WriteAllText(Path.Combine(oldExtensions, "licenses", "Mine-LICENSE.txt"), "MIT");
        File.WriteAllText(Path.Combine(Documents, "CLeARINET", "capture.saz"), "not copied");

        var result = FormerNameMigration.Run(AppData, Documents);

        Assert.Equal("Extensions", Assert.Single(result.CopiedDocumentsFolders));
        Assert.True(File.Exists(Path.Combine(Documents, "VioLens", "Extensions", "Mine.dll")));
        Assert.True(File.Exists(Path.Combine(Documents, "VioLens", "Extensions", "licenses", "Mine-LICENSE.txt")));
        Assert.False(File.Exists(Path.Combine(Documents, "VioLens", "capture.saz")));
        Assert.True(File.Exists(Path.Combine(Documents, "CLeARINET", FormerNameMigration.NoteFileName)));
    }

    [Fact]
    public void LeavesAnUnreadablePreferencesFileAsItIs()
    {
        WriteOld("preferences.json", "{ not json");

        var log = new List<string>();
        var result = FormerNameMigration.Run(AppData, Documents, log.Add);

        Assert.True(result.CopiedAppData);
        Assert.Equal(0, result.RenamedPreferences);
        Assert.Equal("{ not json", File.ReadAllText(Path.Combine(NewAppData, "preferences.json")));
        Assert.Contains(log, line => line.Contains("doesn't parse"));
    }

    [Theory]
    [InlineData("clearinet.proxy.port", "violens.proxy.port")]
    [InlineData("CLEARINET.UI.panels.extensions", "VIOLENS.UI.panels.extensions")]
    [InlineData("ClearinetCSP.enabled", "ViolensCSP.enabled")]
    [InlineData("CLeARINET.x", "VioLens.x")]
    [InlineData("fiddler.ui.font", "fiddler.ui.font")]
    [InlineData("violens.proxy.port", "violens.proxy.port")]
    public void RenamesOnlyTheFormerPrefix(string before, string after) =>
        Assert.Equal(after, FormerNameMigration.RenamePreference(before));

    [Fact]
    public void AnAlreadyRenamedPreferenceWinsOverItsOldDuplicate()
    {
        WriteOld("preferences.json", """
            { "formatVersion": 1, "preferences": { "violens.proxy.port": "1", "clearinet.proxy.port": "2" } }
            """);

        FormerNameMigration.Run(AppData, Documents);

        using var store = new PreferenceStore(Path.Combine(NewAppData, "preferences.json"));
        Assert.Equal("1", store["violens.proxy.port"]);
    }
}

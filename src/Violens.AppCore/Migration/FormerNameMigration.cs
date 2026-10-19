using System.Text.Json;
using System.Text.Json.Nodes;

namespace Violens.AppCore.Migration;

/// <summary>
/// Carries a user's data over from the app's former name, CLeARINET, the
/// first time VioLens runs. Copies, never moves (apart from a leftover
/// system-proxy backup, below), and leaves a note listing what was copied.
///
/// <b>Only what CLeARINET itself wrote.</b> Another project with a similar
/// name uses folders called <c>Clearinet</c>, and Windows and macOS don't
/// tell <c>CLeARINET</c> and <c>Clearinet</c> apart, so a folder of that
/// name isn't necessarily ours. Nothing is copied unless the app-data folder holds
/// files only CLeARINET wrote (see <see cref="LooksLikeFormerInstall"/>), and
/// then only those files, never the whole folder. The note never suggests
/// deleting the folder, only the files it lists.
///
/// <list type="bullet">
/// <item>The app-data folder (<c>CLeARINET</c> to <c>VioLens</c>):
/// preferences, AutoResponder rules, FiddlerScript preferences and the
/// legacy host's settings. Preference names starting <c>clearinet</c> are
/// renamed to start <c>violens</c>. Only done when VioLens has no settings
/// yet, so it never overwrites anything VioLens has saved.</item>
/// <item>A system-proxy backup left behind by a CLeARINET that didn't shut
/// down cleanly is moved, not copied, so the crash recovery that runs at
/// startup finds it and puts the user's proxy settings back -- and only
/// once.</item>
/// <item>The Documents folder's <c>Extensions</c> and <c>LegacyExtensions</c>
/// folders, each only when VioLens doesn't have one yet, and only when the
/// app-data folder was CLeARINET's.</item>
/// </list>
///
/// The root certificate is handled separately, by <see cref="FormerRoots"/>,
/// because it has to wait until the new root is trusted.
/// </summary>
public static class FormerNameMigration
{
    /// <summary>The app-data and Documents folder name CLeARINET used.</summary>
    public const string FormerFolderName = "CLeARINET";

    /// <summary>VioLens's own folder name, under the same parents.</summary>
    public const string NewFolderName = "VioLens";

    /// <summary>Left in each old folder once its contents are copied; its presence also stops a second copy.</summary>
    public const string NoteFileName = "MOVED TO VIOLENS.txt";

    private const string SystemProxyBackupPattern = "system-proxy-backup*.json";
    private const string PreferencesFileName = "preferences.json";

    /// <summary>The app-data files CLeARINET wrote (besides system-proxy backups), copied when present.</summary>
    private static readonly string[] AppDataFiles = [PreferencesFileName, "autoresponder-rules.json", "fiddlerscript-prefs.json"];

    /// <summary>The app-data folder CLeARINET's legacy extension host kept its settings in.</summary>
    private const string LegacyHostFolder = "LegacyExtensionHost";

    private static readonly string[] DocumentsSubfolders = ["Extensions", "LegacyExtensions"];

    /// <summary>What a run did, for the log. Empty lists when there was nothing to carry over.</summary>
    public sealed record Result(
        bool CopiedAppData,
        int RenamedPreferences,
        IReadOnlyList<string> MovedBackups,
        IReadOnlyList<string> CopiedDocumentsFolders)
    {
        /// <summary>
        /// True when this computer had CLeARINET: its app-data folder holds
        /// CLeARINET's files, or a note from an earlier run. The trigger for
        /// <see cref="FormerRoots.Remove"/>.
        /// </summary>
        public bool FoundFormerInstall { get; init; }

        public bool DidAnything => CopiedAppData || MovedBackups.Count > 0 || CopiedDocumentsFolders.Count > 0;

        public override string ToString()
        {
            var parts = new List<string>();
            if (CopiedAppData)
            {
                parts.Add($"copied CLeARINET's settings ({RenamedPreferences} preference name(s) updated)");
            }

            if (MovedBackups.Count > 0)
            {
                parts.Add($"moved CLeARINET's system-proxy backup ({string.Join(", ", MovedBackups)}) so it can be restored");
            }

            if (CopiedDocumentsFolders.Count > 0)
            {
                parts.Add($"copied {string.Join(" and ", CopiedDocumentsFolders)} from Documents/{FormerFolderName}");
            }

            return parts.Count == 0 ? "Nothing to carry over from CLeARINET." : string.Join("; ", parts) + ".";
        }
    }

    /// <summary>Runs against the real per-user folders. Never throws: a failure is reported through <paramref name="log"/>.</summary>
    public static Result Run(Action<string>? log = null)
    {
        try
        {
            return Run(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                log);
        }
        catch (Exception ex)
        {
            log?.Invoke($"Couldn't carry settings over from CLeARINET: {ex.Message}");
            return new Result(false, 0, [], []);
        }
    }

    /// <summary>
    /// The same, against the given roots (the parents of the
    /// <c>CLeARINET</c> and <c>VioLens</c> folders). Either may be empty,
    /// which skips that half.
    /// </summary>
    public static Result Run(string appDataRoot, string documentsRoot, Action<string>? log = null)
    {
        var copiedAppData = false;
        var renamed = 0;
        var foundFormerInstall = false;
        var movedBackups = new List<string>();
        var copiedDocuments = new List<string>();

        if (!string.IsNullOrEmpty(appDataRoot))
        {
            var oldFolder = Path.Combine(appDataRoot, FormerFolderName);
            var newFolder = Path.Combine(appDataRoot, NewFolderName);

            if (Directory.Exists(oldFolder) && !SameFolder(oldFolder, newFolder)
                && (File.Exists(Path.Combine(oldFolder, NoteFileName)) || LooksLikeFormerInstall(oldFolder)))
            {
                foundFormerInstall = true;

                // Backups first, and whether or not the rest has been
                // copied before: the old app may have run (and crashed)
                // since.
                foreach (var backup in Directory.GetFiles(oldFolder, SystemProxyBackupPattern))
                {
                    var target = Path.Combine(newFolder, Path.GetFileName(backup));
                    if (File.Exists(target))
                    {
                        continue; // VioLens's own backup is newer; leave the old one be.
                    }

                    Directory.CreateDirectory(newFolder);
                    File.Move(backup, target);
                    movedBackups.Add(Path.GetFileName(backup));
                }

                // The backups just moved may have created the new folder,
                // so "new folder doesn't exist" can't be the test; "nothing
                // but backups in it" is.
                var newFolderEmpty = !Directory.Exists(newFolder)
                    || Directory.EnumerateFileSystemEntries(newFolder)
                        .All(entry => movedBackups.Contains(Path.GetFileName(entry), StringComparer.OrdinalIgnoreCase));

                if (newFolderEmpty && !File.Exists(Path.Combine(oldFolder, NoteFileName)))
                {
                    var copied = new List<string>();
                    Directory.CreateDirectory(newFolder);
                    foreach (var name in AppDataFiles)
                    {
                        var from = Path.Combine(oldFolder, name);
                        if (File.Exists(from))
                        {
                            File.Copy(from, Path.Combine(newFolder, name), overwrite: false);
                            copied.Add(name);
                        }
                    }

                    var legacyHost = Path.Combine(oldFolder, LegacyHostFolder);
                    if (Directory.Exists(legacyHost))
                    {
                        CopyFolder(legacyHost, Path.Combine(newFolder, LegacyHostFolder));
                        copied.Add(LegacyHostFolder + Path.DirectorySeparatorChar);
                    }

                    var preferences = Path.Combine(newFolder, PreferencesFileName);
                    if (File.Exists(preferences))
                    {
                        renamed = RenamePreferenceFile(preferences, log);
                    }

                    WriteNote(oldFolder, newFolder, copied);
                    copiedAppData = true;
                }
            }
        }

        if (foundFormerInstall && !string.IsNullOrEmpty(documentsRoot))
        {
            var oldFolder = Path.Combine(documentsRoot, FormerFolderName);
            var newFolder = Path.Combine(documentsRoot, NewFolderName);

            if (Directory.Exists(oldFolder) && !SameFolder(oldFolder, newFolder)
                && !File.Exists(Path.Combine(oldFolder, NoteFileName)))
            {
                foreach (var name in DocumentsSubfolders)
                {
                    var from = Path.Combine(oldFolder, name);
                    var to = Path.Combine(newFolder, name);
                    if (Directory.Exists(from) && !Directory.Exists(to))
                    {
                        CopyFolder(from, to);
                        copiedDocuments.Add(name + Path.DirectorySeparatorChar);
                    }
                }

                if (copiedDocuments.Count > 0)
                {
                    WriteNote(oldFolder, newFolder, copiedDocuments);
                }
            }
        }

        var result = new Result(copiedAppData, renamed, movedBackups, copiedDocuments) { FoundFormerInstall = foundFormerInstall };
        if (result.DidAnything)
        {
            log?.Invoke(result.ToString());
        }

        return result;
    }

    /// <summary>
    /// True when <paramref name="folder"/> holds files CLeARINET wrote: its
    /// AutoResponder rules, FiddlerScript preferences, legacy-host settings
    /// or system-proxy backup, or a <c>preferences.json</c> in its exact
    /// format with at least one of its <c>clearinet.</c> settings.
    /// </summary>
    public static bool LooksLikeFormerInstall(string folder)
    {
        if (File.Exists(Path.Combine(folder, "autoresponder-rules.json"))
            || File.Exists(Path.Combine(folder, "fiddlerscript-prefs.json"))
            || Directory.Exists(Path.Combine(folder, LegacyHostFolder))
            || Directory.GetFiles(folder, SystemProxyBackupPattern).Length > 0)
        {
            return true;
        }

        var preferences = Path.Combine(folder, PreferencesFileName);
        if (!File.Exists(preferences))
        {
            return false;
        }

        try
        {
            return JsonNode.Parse(File.ReadAllBytes(preferences)) is JsonObject document
                && document["formatVersion"] is JsonValue
                && document["preferences"] is JsonObject values
                && values.Any(pair => pair.Key.StartsWith("clearinet.", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// <c>clearinet.ui.filter.text</c> becomes <c>violens.ui.filter.text</c>,
    /// and an extension's <c>ClearinetCSP.enabled</c> becomes
    /// <c>ViolensCSP.enabled</c>. Anything else is returned unchanged.
    /// </summary>
    public static string RenamePreference(string name)
    {
        const string Former = "clearinet";
        if (!name.StartsWith(Former, StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        var prefix = name[..Former.Length];
        var replacement = prefix switch
        {
            "CLeARINET" => "VioLens",
            "CLEARINET" => "VIOLENS",
            "Clearinet" => "Violens",
            _ => "violens",
        };

        return replacement + name[Former.Length..];
    }

    /// <summary>
    /// Renames the preference names in a copied <c>preferences.json</c>, in
    /// place. A file that doesn't parse is left alone: the preference store
    /// has its own handling for a damaged file.
    /// </summary>
    internal static int RenamePreferenceFile(string path, Action<string>? log)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(
                File.ReadAllBytes(path),
                documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException ex)
        {
            log?.Invoke($"Copied CLeARINET's preferences.json without renaming its preferences: it doesn't parse ({ex.Message}).");
            return 0;
        }

        if (root is not JsonObject document || document["preferences"] is not JsonObject preferences)
        {
            return 0;
        }

        var renamed = new JsonObject();
        var count = 0;
        foreach (var (name, value) in preferences.ToList())
        {
            var newName = RenamePreference(name);
            if (newName != name)
            {
                count++;
            }

            // A name already in its new form wins over a renamed duplicate.
            if (!renamed.ContainsKey(newName) || newName == name)
            {
                renamed[newName] = value?.DeepClone();
            }
        }

        if (count == 0)
        {
            return 0;
        }

        document["preferences"] = renamed;
        var temp = path + ".migrating.tmp";
        File.WriteAllText(temp, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        File.Move(temp, path, overwrite: true);
        return count;
    }

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
        {
            var target = Path.Combine(to, Path.GetFileName(file));
            if (!File.Exists(target))
            {
                File.Copy(file, target);
            }
        }

        foreach (var directory in Directory.GetDirectories(from))
        {
            CopyFolder(directory, Path.Combine(to, Path.GetFileName(directory)));
        }
    }

    private static void WriteNote(string oldFolder, string newFolder, IReadOnlyList<string> copied)
    {
        var nl = Environment.NewLine;
        try
        {
            File.WriteAllText(
                Path.Combine(oldFolder, NoteFileName),
                "CLeARINET, the HTTP(S) inspection tool, is now called VioLens." + nl + nl
                + "VioLens copied these from this folder to " + newFolder + ":" + nl + nl
                + string.Concat(copied.Select(name => "    " + name + nl)) + nl
                + "VioLens doesn't use the copies here. Once you're happy that everything" + nl
                + "carried over, you can delete them. Nothing else in this folder was" + nl
                + "touched: another app with a similar name may use it too." + nl);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The copy itself worked; without the note it isn't repeated
            // anyway, because VioLens's own folder now has files in it.
        }
    }

    private static bool SameFolder(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
}

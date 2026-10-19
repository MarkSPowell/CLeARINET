using System.Text.Json;
using System.Text.Json.Nodes;

namespace Violens.ProxyCore;

/// <summary>
/// Carries a user's data over from the project's former name, CLeARINET,
/// the first time VioLens runs. Copies, never moves (apart from a leftover
/// system-proxy backup, below), so the old folders are left as they were,
/// with a note saying where everything went.
///
/// <list type="bullet">
/// <item>The app-data folder (<c>CLeARINET</c> to <see cref="ViolensPaths.AppFolderName"/>):
/// preferences, AutoResponder rules, FiddlerScript preferences and the
/// legacy host's settings. Preference names starting <c>clearinet</c> are
/// renamed to start <c>violens</c>. Only done when the new folder doesn't
/// exist yet, so it never overwrites anything VioLens has saved.</item>
/// <item>A system-proxy backup left behind by a CLeARINET that didn't shut
/// down cleanly is moved, not copied, so the crash recovery that runs at
/// startup finds it and puts the user's proxy settings back -- and only
/// once.</item>
/// <item>The Documents folder's <c>Extensions</c> and <c>LegacyExtensions</c>
/// folders, each only when VioLens doesn't have one yet.</item>
/// </list>
///
/// The root certificate is handled separately, by
/// <see cref="Certificates.CertificateAuthority.RemoveFormerRoots"/>,
/// because it has to wait until the new root is trusted.
/// </summary>
public static class FormerNameMigration
{
    /// <summary>The app-data and Documents folder name CLeARINET used.</summary>
    public const string FormerFolderName = "CLeARINET";

    /// <summary>Left in each old folder once its contents are copied; its presence also stops a second copy.</summary>
    public const string NoteFileName = "MOVED TO VIOLENS.txt";

    private const string SystemProxyBackupPattern = "system-proxy-backup*.json";
    private const string PreferencesFileName = "preferences.json";
    private static readonly string[] DocumentsSubfolders = ["Extensions", "LegacyExtensions"];

    /// <summary>What a run did, for the log. Empty lists when there was nothing to carry over.</summary>
    public sealed record Result(
        bool CopiedAppData,
        int RenamedPreferences,
        IReadOnlyList<string> MovedBackups,
        IReadOnlyList<string> CopiedDocumentsFolders)
    {
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
        var movedBackups = new List<string>();
        var copiedDocuments = new List<string>();

        if (!string.IsNullOrEmpty(appDataRoot))
        {
            var oldFolder = Path.Combine(appDataRoot, FormerFolderName);
            var newFolder = Path.Combine(appDataRoot, ViolensPaths.AppFolderName);

            if (Directory.Exists(oldFolder) && !SameFolder(oldFolder, newFolder))
            {
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
                    CopyFolder(oldFolder, newFolder, skip: name =>
                        name.Equals(NoteFileName, StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith("system-proxy-backup", StringComparison.OrdinalIgnoreCase)
                        || name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)
                        || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));

                    var preferences = Path.Combine(newFolder, PreferencesFileName);
                    if (File.Exists(preferences))
                    {
                        renamed = RenamePreferenceFile(preferences, log);
                    }

                    WriteNote(oldFolder, newFolder);
                    copiedAppData = true;
                }
            }
        }

        if (!string.IsNullOrEmpty(documentsRoot))
        {
            var oldFolder = Path.Combine(documentsRoot, FormerFolderName);
            var newFolder = Path.Combine(documentsRoot, ViolensPaths.AppFolderName);

            if (Directory.Exists(oldFolder) && !SameFolder(oldFolder, newFolder)
                && !File.Exists(Path.Combine(oldFolder, NoteFileName)))
            {
                foreach (var name in DocumentsSubfolders)
                {
                    var from = Path.Combine(oldFolder, name);
                    var to = Path.Combine(newFolder, name);
                    if (Directory.Exists(from) && !Directory.Exists(to))
                    {
                        CopyFolder(from, to, skip: _ => false);
                        copiedDocuments.Add(name);
                    }
                }

                if (copiedDocuments.Count > 0)
                {
                    WriteNote(oldFolder, newFolder);
                }
            }
        }

        var result = new Result(copiedAppData, renamed, movedBackups, copiedDocuments);
        if (result.DidAnything)
        {
            log?.Invoke(result.ToString());
        }

        return result;
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

    private static void CopyFolder(string from, string to, Func<string, bool> skip)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
        {
            var name = Path.GetFileName(file);
            if (!skip(name))
            {
                File.Copy(file, Path.Combine(to, name), overwrite: false);
            }
        }

        foreach (var directory in Directory.GetDirectories(from))
        {
            CopyFolder(directory, Path.Combine(to, Path.GetFileName(directory)), skip: _ => false);
        }
    }

    private static void WriteNote(string oldFolder, string newFolder)
    {
        try
        {
            File.WriteAllText(
                Path.Combine(oldFolder, NoteFileName),
                "CLeARINET is now called VioLens." + Environment.NewLine + Environment.NewLine
                + "What was in this folder has been copied to:" + Environment.NewLine
                + "    " + newFolder + Environment.NewLine + Environment.NewLine
                + "VioLens doesn't use this folder. Once you're happy that everything" + Environment.NewLine
                + "carried over, you can delete it." + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The copy itself worked; a missing note only means the copy
            // is offered again, and it never overwrites.
        }
    }

    private static bool SameFolder(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
}

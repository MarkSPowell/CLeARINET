using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Clearinet.LegacyExtensionHost.Tests;

/// <summary>
/// Locates the real, third-party Fiddler Classic extension DLLs this
/// project's tests exercise -- these are NOT part of the repo (see the
/// design doc's and both READMEs' "ask before redistributing" notes), so
/// every test that depends on them has to discover them at runtime and
/// skip gracefully, not fail the build, when they're absent (a fresh
/// checkout, CI, or a machine that's never had them copied in).
///
/// Discovery order mirrors <c>Clearinet.LegacyExtensionHost.Program</c>'s
/// own <c>DefaultExtensionsFolder</c> exactly (kept in sync deliberately --
/// see that property's own remarks), plus an environment-variable override
/// so this can point somewhere else (a CI cache, a non-default folder)
/// without editing test source.
/// </summary>
internal static class RealExtensionEnvironment
{
    private const string OverrideEnvironmentVariable = "CLEARINET_LEGACY_EXTENSIONS_DIR";

    /// <summary>
    /// The five real extension filenames every count-based assertion in
    /// this suite was built against (see the design doc's "Confirmed on a
    /// real machine: all five inspected extensions now load clean" entry).
    /// Tests that assert exact counts (AutoTampers, ExecActionHandlers,
    /// FiddlerExtensions) need exactly this set present, not merely "some
    /// DLLs" -- an extensions folder with only a subset would produce
    /// different, still-valid counts that these specific assertions don't
    /// cover.
    /// </summary>
    public static readonly IReadOnlyList<string> ExpectedFileNames = new[]
    {
        "AustralianImages.dll",
        "ContentBlock.dll",
        "JSFormat.dll",
        "SAZClipboard.dll",
        "Differ.dll",
    };

    public static string ExtensionsFolder =>
        Environment.GetEnvironmentVariable(OverrideEnvironmentVariable) is { Length: > 0 } overridePath
            ? overridePath
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CLeARINET", "LegacyExtensions");

    /// <summary>
    /// True only when every one of <see cref="ExpectedFileNames"/> is
    /// present (case-insensitively) in <see cref="ExtensionsFolder"/>.
    /// </summary>
    public static bool IsAvailable => MissingFileNames().Count == 0;

    /// <summary>
    /// Human-readable reason to log/skip on, when <see cref="IsAvailable"/>
    /// is false -- names the folder checked and the override variable, so a
    /// skipped test tells whoever's reading it exactly how to make it run.
    /// </summary>
    public static string UnavailableReason
    {
        get
        {
            var folder = ExtensionsFolder;
            if (!Directory.Exists(folder))
            {
                return $"Real extension DLLs not found: folder does not exist ({folder}). " +
                       $"Set the {OverrideEnvironmentVariable} environment variable to point at a folder containing " +
                       $"{string.Join(", ", ExpectedFileNames)}, or drop them into that default folder.";
            }

            var missing = MissingFileNames();
            return $"Real extension DLLs not found: {folder} is missing {string.Join(", ", missing)}. " +
                   $"These are real third-party binaries, not part of the repo -- see the design doc.";
        }
    }

    private static List<string> MissingFileNames()
    {
        var folder = ExtensionsFolder;
        if (!Directory.Exists(folder))
        {
            return ExpectedFileNames.ToList();
        }

        // Not Enumerable.ToHashSet(...) -- that overload isn't guaranteed
        // present on net48's System.Linq, unlike .NET 10's; the
        // HashSet<T>(IEnumerable<T>, IEqualityComparer<T>) constructor is.
        var present = new HashSet<string>(
            Directory.EnumerateFiles(folder, "*.dll", SearchOption.TopDirectoryOnly).Select(Path.GetFileName),
            StringComparer.OrdinalIgnoreCase);

        return ExpectedFileNames.Where(name => !present.Contains(name)).ToList();
    }

    /// <summary>
    /// Copies just the five <see cref="ExpectedFileNames"/> out of
    /// <see cref="ExtensionsFolder"/> into a fresh, isolated temp folder,
    /// and returns that folder's path. <see cref="RealExtensionLoadFixture"/>
    /// points <see cref="LegacyExtensionLoader"/> at this copy rather than
    /// <see cref="ExtensionsFolder"/> directly, so this suite's exact-count
    /// assertions ("exactly 5 mismatches", "nothing registered") stay
    /// correct regardless of anything else that folder accumulates over
    /// time -- in practice, retargeted copies from
    /// <c>Retarget-LegacyExtension.ps1</c> living right alongside the
    /// originals, since that script's whole point is to drop its output
    /// into the same everyday LegacyExtensions folder the real host reads
    /// from. Confirmed as a real collision, not a hypothetical one: this
    /// suite's "registers nothing" assertion failed for real once a
    /// retargeted `ContentBlock`/`SAZClipboard` pair ended up loaded
    /// alongside the five originals. Caller owns deleting the returned
    /// folder when done with it.
    /// </summary>
    public static string CopyExpectedFilesToIsolatedFolder()
    {
        var isolatedFolder = Path.Combine(Path.GetTempPath(), "clearinet-real-extensions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolatedFolder);

        var sourceFolder = ExtensionsFolder;
        foreach (var fileName in ExpectedFileNames)
        {
            File.Copy(Path.Combine(sourceFolder, fileName), Path.Combine(isolatedFolder, fileName));
        }

        return isolatedFolder;
    }
}

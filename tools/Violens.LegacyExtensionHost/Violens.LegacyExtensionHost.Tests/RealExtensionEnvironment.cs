using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Violens.LegacyExtensionHost.Tests;

/// <summary>
/// Finds the five third-party sample DLLs, which aren't in the repo, so
/// tests that need them skip when they're missing. Looks where
/// <c>Program.DefaultExtensionsFolder</c> does, or where an environment
/// variable says.
/// </summary>
internal static class RealExtensionEnvironment
{
    private const string OverrideEnvironmentVariable = "VIOLENS_LEGACY_EXTENSIONS_DIR";

    /// <summary>The five sample DLLs the count assertions expect, exactly.</summary>
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
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VioLens", "LegacyExtensions");

    /// <summary>
    /// True only when every one of <see cref="ExpectedFileNames"/> is
    /// present (case-insensitively) in <see cref="ExtensionsFolder"/>.
    /// </summary>
    public static bool IsAvailable => MissingFileNames().Count == 0;

    /// <summary>Why the samples aren't available: the folder checked and the variable to set.</summary>
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
    /// Copies just the five sample DLLs into a new temp folder and returns it,
    /// so other files in the real folder (retargeted copies, say) can't upset
    /// the exact-count assertions. The caller deletes it.
    /// </summary>
    public static string CopyExpectedFilesToIsolatedFolder()
    {
        var isolatedFolder = Path.Combine(Path.GetTempPath(), "violens-real-extensions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolatedFolder);

        var sourceFolder = ExtensionsFolder;
        foreach (var fileName in ExpectedFileNames)
        {
            File.Copy(Path.Combine(sourceFolder, fileName), Path.Combine(isolatedFolder, fileName));
        }

        return isolatedFolder;
    }
}

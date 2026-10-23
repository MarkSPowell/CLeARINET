using System;
using System.IO;
using System.Linq;

namespace Violens.LegacyExtensionHost.Tests;

/// <summary>
/// Finds a folder of extension DLLs already recompiled or retargeted
/// against <c>Violens.CompatShim</c>. Any set of files will do. Nothing in
/// the repo provides one (that would mean redistributing modified
/// third-party binaries), so the tests using it only run when a developer
/// points them at a local folder.
/// </summary>
internal static class PatchedExtensionEnvironment
{
    private const string EnvironmentVariable = "VIOLENS_LEGACY_PATCHED_EXTENSIONS_DIR";

    public static string ExtensionsFolder => Environment.GetEnvironmentVariable(EnvironmentVariable);

    public static bool IsAvailable
    {
        get
        {
            var folder = ExtensionsFolder;
            return !string.IsNullOrEmpty(folder)
                && Directory.Exists(folder)
                && Directory.EnumerateFiles(folder, "*.dll", SearchOption.TopDirectoryOnly).Any();
        }
    }

    public static string UnavailableReason =>
        $"No patched extensions to test against: set the {EnvironmentVariable} environment variable to a folder " +
        "containing at least one extension .dll recompiled or re-targeted against this shim's own assembly " +
        "identity (see AssemblyMismatch and the design doc's \"Don't get sued\" decision). Not required for " +
        "everyday development -- these tests exist for whenever a patched extension is actually in hand.";
}

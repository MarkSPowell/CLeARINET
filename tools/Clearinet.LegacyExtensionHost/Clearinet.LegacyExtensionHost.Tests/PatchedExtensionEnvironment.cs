using System;
using System.IO;
using System.Linq;

namespace Clearinet.LegacyExtensionHost.Tests;

/// <summary>
/// Locates a folder of extension DLLs that have already been recompiled or
/// re-targeted against this shim's own assembly identity
/// (<c>Clearinet.CompatShim</c>, not <c>Fiddler</c> -- see the design doc's
/// "Don't get sued" decision and <c>AssemblyMismatch</c>). Unlike
/// <see cref="RealExtensionEnvironment"/>, this doesn't assume any
/// particular set of files -- a patched extension could be any of the five
/// real samples, a subset, or something else entirely -- so there's no
/// fixed file-name list to check against, only "at least one .dll is
/// there."
///
/// Optional, and off by default: nothing in this repo ships a patched copy
/// of any real extension (that would mean redistributing a modified
/// third-party binary -- see the CompatShim README's own redistribution
/// note), so the tests that use this only run when a developer points it
/// at their own local folder of patched extensions.
/// </summary>
internal static class PatchedExtensionEnvironment
{
    private const string EnvironmentVariable = "CLEARINET_LEGACY_PATCHED_EXTENSIONS_DIR";

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

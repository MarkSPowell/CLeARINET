using System;
using System.IO;

namespace Violens.CompatShim;

/// <summary>
/// <c>GetPath(string)</c> and <c>bUseAESForSAZ</c>, used by ContentBlock and
/// SAZClipboard. Static, since they're only used statically.
/// </summary>
public static class CONFIG
{
    /// <summary>
    /// Used by ContentBlock. Fiddler's path names aren't known, so this returns
    /// a folder of that name under VioLens's LocalAppData folder.
    /// </summary>
    public static string GetPath(string pathName)
    {
        var basePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VioLens", "LegacyExtensionHost", pathName ?? string.Empty);
        Directory.CreateDirectory(basePath);
        return basePath;
    }

    /// <summary>metadata: field -- <c>Fiddler.CONFIG.bool bUseAESForSAZ</c> -- SAZClipboard.</summary>
    public static bool bUseAESForSAZ;
}

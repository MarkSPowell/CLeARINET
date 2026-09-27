using System;
using System.IO;

namespace Clearinet.CompatShim;

/// <summary>
/// Members confirmed by metadata (both from ContentBlock/SAZClipboard):
/// <c>static GetPath(string)</c>, <c>bUseAESForSAZ</c> field. Made a
/// <c>static class</c> since every reference is static-style access with no
/// visible constructor call anywhere in the five samples.
/// </summary>
public static class CONFIG
{
    /// <summary>
    /// metadata: <c>Fiddler.CONFIG.static string GetPath(string)</c> --
    /// ContentBlock. Real Fiddler's own path-name conventions (e.g. what
    /// string keys like "Captures" mean) aren't confirmed from metadata --
    /// this returns a CLeARINET-branded folder under LocalAppData keyed by
    /// whatever name is passed, a reasonable placeholder pending real
    /// evidence of what ContentBlock actually requests.
    /// </summary>
    public static string GetPath(string pathName)
    {
        var basePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CLeARINET", "LegacyExtensionHost", pathName ?? string.Empty);
        Directory.CreateDirectory(basePath);
        return basePath;
    }

    /// <summary>metadata: field -- <c>Fiddler.CONFIG.bool bUseAESForSAZ</c> -- SAZClipboard.</summary>
    public static bool bUseAESForSAZ;
}

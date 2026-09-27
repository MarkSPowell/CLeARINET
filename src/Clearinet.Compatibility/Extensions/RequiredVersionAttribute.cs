namespace Clearinet.Compatibility.Extensions;

/// <summary>
/// CLeARINET's own equivalent of Fiddler Classic's
/// <c>[assembly: Fiddler.RequiredVersion("2.2.8.6")]</c> -- an assembly-level
/// attribute an extension author sets in their own project (typically
/// <c>AssemblyInfo.cs</c>) to declare the minimum host version their
/// extension needs. Can't reference <c>Fiddler.RequiredVersion</c> itself
/// (a real Fiddler Classic type, unavailable here for the same
/// assembly-identity reason the rest of this namespace's remarks cover) --
/// this is a CLeARINET-native type with the same name and shape an author
/// ports their own attribute reference to, following this project's
/// source-compatibility (not binary-compatibility) approach throughout.
///
/// <see cref="ExtensionHost"/> reproduces Fiddler's own documented gating
/// behavior exactly, including the part that surprises people the first
/// time they hit it: "Assemblies which do not contain a RequiredVersion
/// attribute are silently ignored" (fiddlerbook.com/fiddler/dev/IFiddlerExtension.asp).
/// CLeARINET's own version is exposed as <c>Clearinet.ProxyCore.ClearinetVersion.Current</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class RequiredVersionAttribute(string minimumVersion) : Attribute
{
    /// <summary>The minimum CLeARINET version this extension needs, as a parseable <see cref="Version"/> string (e.g. <c>"0.1.0.0"</c>).</summary>
    public string MinimumVersion { get; } = minimumVersion;
}

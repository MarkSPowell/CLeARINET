namespace Violens.Compatibility.Extensions;

/// <summary>
/// The minimum VioLens version a VioLens extension needs, like Fiddler's
/// <c>[assembly: RequiredVersion("2.2.8.6")]</c>. As in Fiddler, an assembly
/// without it is silently ignored. Compared with
/// <c>Violens.ProxyCore.ViolensVersion.Current</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class RequiredVersionAttribute(string minimumVersion) : Attribute
{
    /// <summary>The minimum VioLens version this extension needs, as a parseable <see cref="Version"/> string (e.g. <c>"0.1.0.0"</c>).</summary>
    public string MinimumVersion { get; } = minimumVersion;
}

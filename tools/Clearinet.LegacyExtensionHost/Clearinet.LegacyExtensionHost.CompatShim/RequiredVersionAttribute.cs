using System;

namespace Clearinet.CompatShim;

/// <summary>
/// Ctor signature confirmed against all five real extensions inspected --
/// every one declares <c>[assembly: Fiddler.RequiredVersion("x.x.x.x")]</c>
/// with a single string argument (metadata: <c>Fiddler.RequiredVersionAttribute.void .ctor(string)</c>).
/// <see cref="MinimumVersion"/>'s property name/shape is this project's own
/// choice, not reflected from any real binary -- none of the five samples
/// call a getter back on this attribute, only construct it, so nothing
/// about how <see cref="LegacyExtensionHost.LegacyExtensionLoader"/> reads
/// it back afterward affects binary compatibility.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class RequiredVersionAttribute : Attribute
{
    public RequiredVersionAttribute(string minimumVersion)
    {
        MinimumVersion = minimumVersion;
    }

    public string MinimumVersion { get; }
}

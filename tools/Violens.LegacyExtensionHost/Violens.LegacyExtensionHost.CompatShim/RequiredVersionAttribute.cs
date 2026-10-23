using System;

namespace Violens.CompatShim;

/// <summary>
/// Every sample declares <c>[assembly: Fiddler.RequiredVersion("x.x.x.x")]</c>.
/// The <see cref="MinimumVersion"/> property is our own; extensions only
/// construct the attribute.
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

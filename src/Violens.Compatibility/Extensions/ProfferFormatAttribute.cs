namespace Violens.Compatibility.Extensions;

/// <summary>
/// Fiddler's <c>[ProfferFormat("Name", "Description")]</c>: a format an
/// importer or exporter handles. A class may have several.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ProfferFormatAttribute(string formatName, string description) : Attribute
{
    /// <summary>The format's short name, e.g. <c>"MyFormat"</c> -- shown in the Import/Export format picker and passed back as <c>importFormat</c>/<c>exportFormat</c> when the user picks it.</summary>
    public string FormatName { get; } = formatName;

    /// <summary>A short human-readable description of the format, shown alongside its name in the picker.</summary>
    public string Description { get; } = description;
}

namespace Clearinet.Compatibility.Extensions;

/// <summary>
/// CLeARINET's equivalent of Fiddler Classic's own <c>[ProfferFormat("Name",
/// "Description")]</c> attribute, decorating an <c>ISessionImporter</c>/
/// <c>ISessionExporter</c> implementation to declare which named format(s)
/// it handles (fiddlerbook.com/fiddler/dev/ISessionExport.asp) -- an
/// importer/exporter class can carry more than one of these (real Fiddler's
/// own SAZ exporter, for instance, proffers both "Session Archive (.saz)"
/// and a legacy alias), which is why this allows multiple.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ProfferFormatAttribute(string formatName, string description) : Attribute
{
    /// <summary>The format's short name, e.g. <c>"MyFormat"</c> -- shown in the Import/Export format picker and passed back as <c>importFormat</c>/<c>exportFormat</c> when the user picks it.</summary>
    public string FormatName { get; } = formatName;

    /// <summary>A short human-readable description of the format, shown alongside its name in the picker.</summary>
    public string Description { get; } = description;
}

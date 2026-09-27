namespace Clearinet.CompatShim;

/// <summary>
/// Fiddler's <c>ISessionImporter</c>, with its documented signature, so a
/// ported importer compiles unchanged. <c>dictOptions</c> may carry
/// <c>"Filename"</c> (a path) or <c>"Content"</c> (the data as a string);
/// with neither, an importer typically asks the user via
/// <see cref="Utilities.ObtainOpenFilename(string, string)"/>. Returning
/// null or an empty array means nothing was imported.
///
/// The host wraps each one in
/// <c>Clearinet.Compatibility.Extensions.CompatShimImporterAdapter</c>, which
/// turns the returned sessions into CLeARINET's own.
/// </summary>
public interface ISessionImporter : IDisposable
{
    Session[]? ImportSessions(
        string sImportFormat,
        Dictionary<string, object> dictOptions,
        EventHandler<ProgressCallbackEventArgs>? evtProgressNotifications);
}

/// <summary>Progress from a long-running import or export. Set <see cref="Cancel"/> to ask it to stop.</summary>
public class ProgressCallbackEventArgs : EventArgs
{
    /// <param name="flCompletionRatio">From 0 (just started) to 1 (done).</param>
    /// <param name="sProgressText">What's happening, for display.</param>
    public ProgressCallbackEventArgs(float flCompletionRatio, string sProgressText)
    {
        CompletionRatio = flCompletionRatio;
        ProgressText = sProgressText ?? string.Empty;
    }

    /// <summary>The ratio passed in, 0 to 1.</summary>
    public float CompletionRatio { get; }

    /// <summary>The ratio as a whole percentage, clamped to 0-100.</summary>
    public int PercentComplete => (int)Math.Round(Math.Clamp(CompletionRatio, 0f, 1f) * 100);

    public string ProgressText { get; }

    public bool Cancel { get; set; }
}

/// <summary>
/// Declares a format an importer or exporter handles, as in Fiddler:
/// <c>[ProfferFormat("NetLog JSON", "description", ".json;.gz")]</c>. The
/// optional third argument lists file extensions, semicolon-separated.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ProfferFormatAttribute : Attribute
{
    public ProfferFormatAttribute(string sFormatName, string sDescription)
        : this(sFormatName, sDescription, string.Empty)
    {
    }

    public ProfferFormatAttribute(string sFormatName, string sDescription, string sExtensions)
    {
        FormatName = sFormatName;
        FormatDescription = sDescription;
        Extensions = sExtensions ?? string.Empty;
    }

    public string FormatName { get; }

    public string FormatDescription { get; }

    /// <summary>Semicolon-separated extensions, e.g. <c>.json;.gz</c>, or empty.</summary>
    public string Extensions { get; }
}

/// <summary>
/// The minimum Fiddler version an extension says it needs, as in
/// <c>[assembly: RequiredVersion("4.6.0.0")]</c>. Checked against
/// <see cref="CompatShimHost.FiddlerApiLevel"/>. As in Fiddler, an assembly
/// without it isn't treated as an extension at all.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = false)]
public sealed class RequiredVersionAttribute : Attribute
{
    public RequiredVersionAttribute(string version) => RequiredVersion = version;

    public string RequiredVersion { get; }
}

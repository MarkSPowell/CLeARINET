namespace Violens.Compatibility.Extensions;

/// <summary>
/// Progress for an import or export, which the host can cancel. Fiddler's
/// <c>PercentComplete</c> string is left out; its format isn't documented.
/// </summary>
public sealed class ProgressCallbackEventArgs(float completionRatio, string progressText)
{
    /// <summary>How complete the operation is, from 0.0 to 1.0.</summary>
    public float CompletionRatio { get; } = completionRatio;

    /// <summary>A short human-readable status string (e.g. "Importing session 4 of 12...").</summary>
    public string ProgressText { get; } = progressText;

    /// <summary>Set by the callback's own handler to request cancellation; the importer/exporter checks this between items and stops early if set.</summary>
    public bool Cancel { get; set; }
}

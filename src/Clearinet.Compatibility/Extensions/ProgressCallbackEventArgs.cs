namespace Clearinet.Compatibility.Extensions;

/// <summary>
/// CLeARINET's equivalent of Fiddler Classic's own progress-callback type,
/// passed to an <c>ISessionImporter</c>/<c>ISessionExporter</c> so a
/// long-running import/export can report progress and be cancelled
/// mid-operation (fiddlerbook.com/fiddler/dev/ISessionExport.asp).
///
/// Deliberately simplified from real Fiddler's own shape: this drops the
/// separate <c>PercentComplete: string</c> member real Fiddler is documented
/// as also exposing alongside a numeric completion ratio -- that string
/// member's exact formatting/semantics couldn't be pinned down from the
/// public docs available while designing this (the fiddlerbook.com page
/// documents the type's existence and purpose but not every member's exact
/// contract), and a redundant, unverified string duplicate of
/// <see cref="CompletionRatio"/> wasn't worth guessing at. Flagged here as a
/// deliberate deviation, not an oversight -- see the .NET Extension
/// Compatibility Design doc's own section on this type.
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

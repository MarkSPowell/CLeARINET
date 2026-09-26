using System.Reflection;

namespace Clearinet.Compatibility.Extensions;

/// <summary>A format an importer or exporter offers: name, description, and optional semicolon-separated file extensions.</summary>
public sealed record ProfferedFormat(string Name, string Description, string Extensions = "");

/// <summary>
/// Reads the formats an importer/exporter offers, whichever kind it is: a
/// CLeARINET-native one (its own <see cref="ProfferFormatAttribute"/>s) or a
/// ported, Fiddler-shaped one wrapped in <see cref="CompatShimImporterAdapter"/>.
/// </summary>
public static class ProfferedFormats
{
    public static IReadOnlyList<ProfferedFormat> Of(object importerOrExporter)
    {
        ArgumentNullException.ThrowIfNull(importerOrExporter);

        if (importerOrExporter is CompatShimImporterAdapter adapter)
        {
            return adapter.Formats;
        }

        // GetCustomAttributes (plural): ProfferFormatAttribute allows several,
        // and the singular form throws when more than one is present.
        return importerOrExporter.GetType()
            .GetCustomAttributes<ProfferFormatAttribute>()
            .Select(a => new ProfferedFormat(a.FormatName, a.Description))
            .ToList();
    }

    /// <summary>
    /// Every (extension, format) pair on offer, in load order: one entry per
    /// proffered format, so an extension offering two formats appears twice.
    /// An importer/exporter that declares no format at all still gets one
    /// entry, with an empty format name, the same thing it was always
    /// called with before there was a picker.
    /// </summary>
    public static IReadOnlyList<FormatChoice> ChoicesFrom(IEnumerable<object> importersOrExporters)
    {
        ArgumentNullException.ThrowIfNull(importersOrExporters);

        var choices = new List<FormatChoice>();
        foreach (var handler in importersOrExporters)
        {
            var implementation = handler is CompatShimImporterAdapter adapter ? adapter.Inner : handler;
            var source = implementation.GetType().Assembly.GetName().Name ?? implementation.GetType().Name;
            var formats = Of(handler);
            if (formats.Count == 0)
            {
                choices.Add(new FormatChoice(handler, new ProfferedFormat(string.Empty, string.Empty), source, implementation.GetType().Name));
                continue;
            }

            foreach (var format in formats)
            {
                choices.Add(new FormatChoice(handler, format, source, implementation.GetType().Name));
            }
        }

        return choices;
    }

    /// <summary>
    /// The choice to highlight first: the one whose <see cref="FormatChoice.Key"/>
    /// matches <paramref name="rememberedKey"/> (the last one picked), or the
    /// first. Null only when there are no choices.
    /// </summary>
    public static FormatChoice? Preselect(IReadOnlyList<FormatChoice> choices, string? rememberedKey) =>
        choices.FirstOrDefault(c => string.Equals(c.Key, rememberedKey, StringComparison.Ordinal)) ?? choices.FirstOrDefault();
}

/// <summary>
/// One entry in the Import/Export via Extension format picker: which loaded
/// importer or exporter (<see cref="Handler"/>, the object to call) and
/// which of its formats. UI-neutral, so any host can show these however it
/// likes; the desktop app shows them in FormatPickerWindow.
/// </summary>
/// <param name="Handler">The <see cref="ISessionImporter"/> or <see cref="ISessionExporter"/> to call.</param>
/// <param name="Format">The format to pass it. <see cref="ProfferedFormat.Name"/> is empty if the extension declared none.</param>
/// <param name="Source">The extension's assembly name, e.g. <c>FiddlerImportNetlog</c>.</param>
/// <param name="TypeName">The implementing class's name, shown when the format has no name of its own.</param>
public sealed record FormatChoice(object Handler, ProfferedFormat Format, string Source, string TypeName)
{
    /// <summary>What the picker shows as the entry's title.</summary>
    public string DisplayName => string.IsNullOrEmpty(Format.Name) ? TypeName : Format.Name;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Format.Description);

    /// <summary>Where it comes from, plus the file types it handles when the extension says.</summary>
    public string SourceLabel => string.IsNullOrEmpty(Format.Extensions)
        ? $"From {Source}"
        : $"From {Source} · {Format.Extensions.Replace(";", ", ", StringComparison.Ordinal)}";

    /// <summary>
    /// Stable across runs (assembly name plus format name), so the picker can
    /// remember the last choice in preferences. Not the handler instance,
    /// which is new every run.
    /// </summary>
    public string Key => $"{Source}|{Format.Name}";
}

/// <summary>
/// Lets a ported, Fiddler-shaped importer (<see cref="Clearinet.CompatShim.ISessionImporter"/>)
/// plug into CLeARINET's own import path unchanged: File > Import calls this
/// like any native <see cref="ISessionImporter"/>, and this calls the
/// ported code with Fiddler's argument shapes, then converts the Fiddler-
/// shaped sessions it returns (see <see cref="Clearinet.CompatShim.ShimSessionConverter"/>).
///
/// Progress is forwarded both ways: each Fiddler-shaped progress event
/// becomes a native one, and a <c>Cancel</c> set by the host is copied
/// back so the ported code sees it.
/// </summary>
public sealed class CompatShimImporterAdapter : ISessionImporter
{
    private readonly Clearinet.CompatShim.ISessionImporter _inner;

    public CompatShimImporterAdapter(Clearinet.CompatShim.ISessionImporter inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        Formats = inner.GetType()
            .GetCustomAttributes<Clearinet.CompatShim.ProfferFormatAttribute>()
            .Select(a => new ProfferedFormat(a.FormatName, a.FormatDescription, a.Extensions))
            .ToList();
    }

    /// <summary>The ported importer itself.</summary>
    public Clearinet.CompatShim.ISessionImporter Inner => _inner;

    public IReadOnlyList<ProfferedFormat> Formats { get; }

    public IReadOnlyList<ImportedSession> ImportSessions(
        string importFormat,
        IReadOnlyDictionary<string, object> options,
        Action<ProgressCallbackEventArgs>? progress)
    {
        var fiddlerOptions = new Dictionary<string, object>(options ?? new Dictionary<string, object>());

        EventHandler<Clearinet.CompatShim.ProgressCallbackEventArgs>? onProgress = null;
        if (progress is not null)
        {
            onProgress = (_, e) =>
            {
                var native = new ProgressCallbackEventArgs(e.CompletionRatio, e.ProgressText);
                progress(native);
                e.Cancel = native.Cancel;
            };
        }

        var sessions = _inner.ImportSessions(importFormat, fiddlerOptions, onProgress);
        if (sessions is null || sessions.Length == 0)
        {
            return [];
        }

        var importedAt = DateTimeOffset.Now;
        return sessions
            .Where(s => s is not null)
            .Select(s => Clearinet.CompatShim.ShimSessionConverter.ToImportedSession(s, importedAt))
            .ToList();
    }

    public void Dispose() => _inner.Dispose();
}

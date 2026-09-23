namespace Clearinet.Extensibility.Inspection;

/// <summary>
/// A session inspector: turns one side of a captured session into
/// structured, UI-framework-neutral content a host renders. This is the
/// seam Fiddler Classic's <c>Inspector2</c> filled by handing back a live
/// WinForms control -- workable there because Classic's own shell was
/// WinForms too, but a dead end for CLeARINET, whose whole point (tenet 4
/// in the project plan) is one proxy core serving more than one UI
/// front-end. "Data in, rendering out" means an inspector never
/// references a UI toolkit at all: it inspects and returns an
/// <see cref="InspectorContent"/> value, and a small, fixed set of
/// renderers the host ships (one per <see cref="InspectorContent"/> case)
/// turns that into pixels.
///
/// This first cut only has first-party, in-process implementations (see
/// <see cref="InspectorRegistry"/>). A third-party inspector plugin --
/// tiered Beta in the Fiddler Feature Inventory's "Custom inspectors" row,
/// not part of this MVP slice -- would implement this exact same
/// interface and need no UI framework reference at all, which is also
/// what will make it safe to load into an isolated
/// <c>AssemblyLoadContext</c> without dragging Avalonia along for the ride.
/// </summary>
public interface IInspector
{
    /// <summary>
    /// Stable identity, never shown to the user -- for persisting "last
    /// selected tab" and, later, collision detection between first- and
    /// third-party inspectors. Convention: <c>"clearinet.&lt;name&gt;"</c>
    /// for built-ins.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// The tab label. Deliberately matches Fiddler Classic's own inspector
    /// names ("Headers", "Raw", "Hex", ...) where one applies -- tenet 1 is
    /// usage compatibility, and a tab name is the one part of this API a
    /// user actually reads.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Controls tab order within one side's inspector list. Lower sorts
    /// first; ties break on <see cref="DisplayName"/> for a stable order
    /// regardless of registration order.
    /// </summary>
    int SortOrder { get; }

    /// <summary>
    /// Whether this inspector has anything useful to say about
    /// <paramref name="context"/> -- e.g. a future JSON inspector
    /// declining a binary image body. Called before <see cref="Inspect"/>
    /// so a host can decide which tabs to show at all; an inspector that
    /// always applies (Headers, Raw, Hex) just returns true.
    /// </summary>
    bool CanInspect(InspectorContext context);

    /// <summary>
    /// Produces this inspector's content for <paramref name="context"/>.
    /// Only called right after <see cref="CanInspect"/> returned true for
    /// the same context. Should not throw for merely malformed input --
    /// return <see cref="ErrorContent"/> instead, so one bad inspector
    /// (first- or, eventually, third-party) can't take the whole detail
    /// pane down.
    /// </summary>
    InspectorContent Inspect(InspectorContext context);
}

namespace Violens.CompatShim;

/// <summary>
/// <c>UI</c> (used by all five samples), <c>Prefs</c> (ContentBlock,
/// JSFormat) and <c>Log</c> (JSFormat). <see cref="UI"/> is settable so the
/// host can assign its window at startup.
/// </summary>
public static class FiddlerApplication
{
    public static frmViewer UI { get; set; }

    public static IFiddlerPreferences Prefs { get; set; } = new InMemoryFiddlerPreferences();

    public static Logger Log { get; set; } = new Logger();
}

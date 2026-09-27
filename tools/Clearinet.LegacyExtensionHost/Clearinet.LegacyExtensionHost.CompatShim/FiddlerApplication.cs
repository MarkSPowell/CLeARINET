namespace Clearinet.CompatShim;

/// <summary>
/// Members confirmed by metadata, all <c>static</c>: <c>get_UI()</c> →
/// <see cref="frmViewer"/> (all five samples), <c>get_Prefs()</c> →
/// <see cref="IFiddlerPreferences"/> (ContentBlock, JSFormat),
/// <c>get_Log()</c> → <see cref="Logger"/> (JSFormat).
///
/// <see cref="UI"/> has a settable backing field even though only a getter
/// was confirmed at any extension call site -- something has to assign it,
/// and that's the legacy host process's own <c>Program.cs</c> at startup,
/// not extension code.
/// </summary>
public static class FiddlerApplication
{
    public static frmViewer UI { get; set; }

    public static IFiddlerPreferences Prefs { get; set; } = new InMemoryFiddlerPreferences();

    public static Logger Log { get; set; } = new Logger();
}

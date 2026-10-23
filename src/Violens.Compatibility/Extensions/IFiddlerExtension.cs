namespace Violens.Compatibility.Extensions;

/// <summary>
/// Fiddler's <c>IFiddlerExtension</c>, written from the public reference
/// (fiddlerbook.com/fiddler/dev/IFiddlerExtension.asp). This is a separate
/// type from Fiddler's, so compiled Fiddler extensions can't bind to it;
/// an extension's source ports by switching the interface and recompiling.
/// See the .NET Extension Compatibility Design.
/// </summary>
public interface IFiddlerExtension
{
    /// <summary>Fiddler's <c>OnLoad()</c>: called once the app is ready, on the UI thread.</summary>
    void OnLoad();

    /// <summary>Fiddler's <c>OnBeforeUnload()</c> -- called while VioLens is shutting down or the extension is being unloaded.</summary>
    void OnBeforeUnload();
}

namespace Clearinet.Compatibility.Extensions;

/// <summary>
/// Fiddler Classic's own root extension contract, reproduced member-for-member from
/// the public interface reference (https://fiddlerbook.com/fiddler/dev/IFiddlerExtension.asp)
/// -- not from any compiled extension's decompiled IL. This project has a copy of a
/// real, closed-source compiled extension (SAZClipboard.dll) on hand, and confirmed
/// (via plain string/metadata inspection, never disassembly -- see the Extension
/// Compatibility Design doc) that it references Fiddler's own actual
/// <c>Fiddler.IFiddlerExtension</c> type. That matters: .NET requires the exact same
/// assembly-qualified type identity for an interface to be recognized as "the same
/// one," so an existing compiled Fiddler Classic extension .dll can NOT bind against
/// this interface no matter how precisely its shape matches -- this is a fresh,
/// independently-authored type, not Fiddler's own real one.
///
/// What this interface family IS for: an engineer who has an existing Fiddler Classic
/// extension's *source* (the main porting goal -- see the Project Plan's
/// compatibility review) can port it here by swapping the base
/// interface/using directive and recompiling against Clearinet.Compatibility, with
/// every method name, parameter shape, and (see
/// <see cref="Clearinet.Compatibility.FiddlerScript.Exchange"/>) session-object member
/// name already familiar. Source-level compatibility, not binary compatibility --
/// see the design doc for the full distinction and what a binary-compatibility shim
/// would actually require.
/// </summary>
public interface IFiddlerExtension
{
    /// <summary>
    /// Fiddler's <c>OnLoad()</c> -- called once CLeARINET's own UI/proxy is fully
    /// available, the point at which it's safe for an extension to add its own UI or
    /// start doing work. Not wired into any real startup sequence yet -- see the
    /// design doc's own "what's not built yet": this pass proves the interface
    /// contract itself, not a folder-scan-and-load host that calls it automatically.
    /// </summary>
    void OnLoad();

    /// <summary>Fiddler's <c>OnBeforeUnload()</c> -- called while CLeARINET is shutting down or the extension is being unloaded.</summary>
    void OnBeforeUnload();
}

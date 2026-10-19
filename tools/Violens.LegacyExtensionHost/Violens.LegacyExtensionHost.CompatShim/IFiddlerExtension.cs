namespace Violens.CompatShim;

/// <summary>
/// Signatures reproduced from fiddlerbook.com/fiddler/dev/IFiddlerExtension.asp
/// (public docs), same source already used for the source-compatible
/// <c>Violens.Compatibility.Extensions.IFiddlerExtension</c> -- see that
/// type's own remarks. Every one of the five real extensions inspected for
/// this shim implements this interface.
/// </summary>
public interface IFiddlerExtension
{
    void OnLoad();
    void OnBeforeUnload();
}

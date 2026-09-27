using Clearinet.Compatibility.FiddlerScript;

namespace Clearinet.Compatibility.Extensions;

/// <summary>
/// Fiddler Classic's <c>IAutoTamper3</c> -- the request-side equivalent of
/// <see cref="IAutoTamper2.OnPeekAtResponseHeaders"/>. Reproduced member-for-member
/// from https://fiddlerbook.com/fiddler/dev/IFiddlerExtension.asp; see
/// <see cref="IFiddlerExtension"/>'s own remarks for the source-vs-binary
/// compatibility distinction this namespace is built around.
/// </summary>
public interface IAutoTamper3 : IAutoTamper2
{
    /// <summary>Fiddler's <c>OnPeekAtRequestHeaders(Session oSession)</c>.</summary>
    void OnPeekAtRequestHeaders(Exchange oSession);
}

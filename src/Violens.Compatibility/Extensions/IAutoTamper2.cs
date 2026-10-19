using Violens.Compatibility.FiddlerScript;

namespace Violens.Compatibility.Extensions;

/// <summary>
/// Fiddler Classic's <c>IAutoTamper2</c> -- adds the same "headers only, body not
/// read yet" checkpoint FiddlerScript's own <c>OnPeekAtResponseHeaders</c> exposes
/// (see <see cref="Violens.Compatibility.FiddlerScript.FiddlerScriptHost.InvokeOnPeekAtResponseHeaders"/>'s
/// own remarks). Reproduced member-for-member from
/// https://fiddlerbook.com/fiddler/dev/IFiddlerExtension.asp; see
/// <see cref="IFiddlerExtension"/>'s own remarks for the source-vs-binary
/// compatibility distinction this namespace is built around. Neither this nor
/// FiddlerScript's own peek handler is wired into
/// <c>Violens.ProxyCore.Proxy.InterceptingProxyListener</c> yet.
/// </summary>
public interface IAutoTamper2 : IAutoTamper
{
    /// <summary>Fiddler's <c>OnPeekAtResponseHeaders(Session oSession)</c>.</summary>
    void OnPeekAtResponseHeaders(Exchange oSession);
}

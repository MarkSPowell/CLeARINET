using Violens.Compatibility.FiddlerScript;

namespace Violens.Compatibility.Extensions;

/// <summary>Fiddler's <c>IAutoTamper2</c>: adds a look at response headers before the body is read.</summary>
public interface IAutoTamper2 : IAutoTamper
{
    /// <summary>Fiddler's <c>OnPeekAtResponseHeaders(Session oSession)</c>.</summary>
    void OnPeekAtResponseHeaders(Exchange oSession);
}

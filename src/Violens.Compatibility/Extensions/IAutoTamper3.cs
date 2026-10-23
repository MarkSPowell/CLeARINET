using Violens.Compatibility.FiddlerScript;

namespace Violens.Compatibility.Extensions;

/// <summary>Fiddler's <c>IAutoTamper3</c>: adds a look at request headers before the body is read.</summary>
public interface IAutoTamper3 : IAutoTamper2
{
    /// <summary>Fiddler's <c>OnPeekAtRequestHeaders(Session oSession)</c>.</summary>
    void OnPeekAtRequestHeaders(Exchange oSession);
}

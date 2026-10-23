namespace Violens.CompatShim;

/// <summary>Fiddler's <c>IFiddlerExtension</c>, from the public docs. All five samples implement it.</summary>
public interface IFiddlerExtension
{
    void OnLoad();
    void OnBeforeUnload();
}

namespace Violens.CompatShim;

/// <summary>
/// <c>headers</c>, the string indexer and <c>FailSession(int, string, string)</c>,
/// used by ContentBlock and SAZClipboard.
/// </summary>
public sealed class ClientChatter
{
    public HTTPRequestHeaders headers { get; set; } = new HTTPRequestHeaders();

    /// <summary>metadata: getter only confirmed (<c>get_Item</c>) -- likely delegates to <see cref="headers"/>.</summary>
    public string this[string headerName] => headers[headerName];

    /// <summary>Used by ContentBlock; the parameters' meaning is a guess.</summary>
    public void FailSession(int statusCode, string statusText, string body)
    {
        // Does nothing: the session bridge has no way to fail a session yet, so
        // an extension calling this can't affect the real traffic.
    }
}

using System.Collections.Generic;
using System.Linq;

namespace Violens.CompatShim;

/// <summary>
/// The constructors, <c>ContainsHost</c> and both <c>AssignFromString</c>
/// overloads, used by ContentBlock. It also calls <see cref="ToString"/>
/// to fill its "Edit Blocked Host List" box. Hosts are separated by
/// semicolons: ContentBlock's prompt asks for a semicolon-delimited list,
/// and its "Block this Host" action appends <c>"; host"</c> to
/// <see cref="ToString"/> and parses the result.
/// </summary>
public sealed class HostList
{
    private readonly List<string> _hosts = new();

    public HostList()
    {
    }

    public HostList(string semicolonSeparatedHosts)
    {
        AssignFromString(semicolonSeparatedHosts);
    }

    /// <summary>metadata: <c>Fiddler.HostList.bool ContainsHost(string)</c>.</summary>
    public bool ContainsHost(string host) =>
        host != null && _hosts.Any(h => string.Equals(h, host, System.StringComparison.OrdinalIgnoreCase));

    /// <summary>metadata: <c>Fiddler.HostList.bool AssignFromString(string)</c>.</summary>
    public bool AssignFromString(string semicolonSeparatedHosts)
    {
        string error = null;
        return AssignFromString(semicolonSeparatedHosts, ref error);
    }

    /// <summary>The <c>ref string</c> is assumed to be an error message.</summary>
    public bool AssignFromString(string semicolonSeparatedHosts, ref string errorMessage)
    {
        _hosts.Clear();
        if (string.IsNullOrWhiteSpace(semicolonSeparatedHosts))
        {
            return true;
        }

        foreach (var part in semicolonSeparatedHosts.Split(';'))
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0)
            {
                _hosts.Add(trimmed);
            }
        }
        return true;
    }

    /// <summary>Joined with <c>"; "</c>, the format ContentBlock appends to.</summary>
    public override string ToString() => string.Join("; ", _hosts);
}

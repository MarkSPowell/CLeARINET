using System.Collections.Generic;
using System.Linq;

namespace Clearinet.CompatShim;

/// <summary>
/// Members confirmed by metadata (all from ContentBlock): parameterless
/// ctor, <c>ctor(string)</c>, <c>ContainsHost(string)</c>,
/// <c>AssignFromString(string, ref string)</c> and its
/// <c>AssignFromString(string)</c> overload. <see cref="ToString"/> is not
/// in that confirmed-by-metadata list -- it's inferred from an observed
/// runtime symptom instead: ContentBlock's "Edit Blocked Host List" prompt
/// showed the literal text <c>Clearinet.Fiddler.HostList</c> as its default
/// value, which is exactly what <see cref="object.ToString"/>'s
/// fully-qualified-type-name fallback produces when a type doesn't override
/// it -- strong evidence the real call site does
/// <c>oBlockedHosts.ToString()</c> to seed the editor with the current
/// list.
///
/// The delimiter is semicolon, not comma -- confirmed two ways from
/// ContentBlock's own IL, not guessed: its "Edit Blocked Host List" prompt
/// literally reads <c>"Enter semicolon-delimited block list."</c>
/// (<c>miEditBlockedHosts_Click</c>), and its "Block this Host" context-menu
/// action (<c>BlockAHost</c>) appends a new entry via
/// <c>string.Format("{0}; {1}", hlBlockedHosts, sHost)</c> -- i.e. it
/// literally calls this class's <see cref="ToString"/> and expects to be
/// able to tack "; host" onto the end and feed the result straight back into
/// <see cref="AssignFromString(string)"/>. Splitting/joining on comma (this
/// class's original, unconfirmed guess) meant a real semicolon-delimited
/// entry like <c>"google.com"</c> got stored as the single literal host
/// <c>"google.com;"</c>, which <see cref="ContainsHost"/> would never match
/// against an actual request's <c>"google.com"</c> -- so the block rule
/// silently never fired. <see cref="ToString"/> joins with <c>"; "</c>
/// specifically (not bare <c>";"</c>) to match <c>BlockAHost</c>'s own
/// format exactly, since that's the one real call site observed building a
/// string this class then has to parse back.
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

    /// <summary>
    /// metadata: <c>Fiddler.HostList.bool AssignFromString(string, ref string)</c>
    /// -- the <c>ref string</c> is presumed to be an out-style error message,
    /// a reasonable but unconfirmed reading of the signature alone. The
    /// delimiter itself (see the class remarks) IS confirmed: semicolon, not
    /// comma.
    /// </summary>
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

    /// <summary>
    /// Not confirmed by metadata (see the class remarks) -- joins the
    /// current hosts with <c>"; "</c>, matching <c>ContentBlocker.BlockAHost</c>'s
    /// own <c>"{0}; {1}"</c> format exactly, so this round-trips cleanly
    /// through <see cref="AssignFromString(string)"/> either way (it splits
    /// on ';' and trims each part regardless of surrounding whitespace).
    /// </summary>
    public override string ToString() => string.Join("; ", _hosts);
}

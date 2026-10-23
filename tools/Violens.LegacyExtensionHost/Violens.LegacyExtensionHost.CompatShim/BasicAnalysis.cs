using System.Collections.Generic;
using System.Text;

namespace Violens.CompatShim;

/// <summary>
/// <c>ComputeBasicStatistics(Session[], bool, ref Dictionary&lt;string, long&gt;, ref long)</c>,
/// used by Differ. Only the signature is known; the meaning of each
/// parameter is a guess.
/// </summary>
public static class BasicAnalysis
{
    public static string ComputeBasicStatistics(Session[] sessions, bool groupByHost, ref Dictionary<string, long> breakdown, ref long total)
    {
        // Diagnostic only.
        FiddlerApplication.Log.LogFormat(
            "BasicAnalysis.ComputeBasicStatistics: called with {0} session(s), groupByHost={1}.",
            new object[] { sessions?.Length ?? -1, groupByHost });

        breakdown ??= new Dictionary<string, long>();
        total = 0;

        if (sessions == null)
        {
            return string.Empty;
        }

        foreach (var session in sessions)
        {
            var key = groupByHost ? (session.host ?? "(unknown host)") : "(all sessions)";
            var size = session.responseBodyBytes?.LongLength ?? 0;
            breakdown[key] = breakdown.TryGetValue(key, out var existing) ? existing + size : size;
            total += size;
        }

        var sb = new StringBuilder();
        sb.Append(sessions.Length).Append(" session(s), ").Append(total).Append(" byte(s) total.");
        return sb.ToString();
    }
}

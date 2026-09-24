using System.Collections.Generic;
using System.Text;

namespace Clearinet.CompatShim;

/// <summary>
/// metadata: <c>Fiddler.BasicAnalysis.static string ComputeBasicStatistics(
/// Fiddler.Session[], bool, ref System.Collections.Generic.Dictionary`2&lt;string, long&gt;, ref long)</c>
/// -- Differ. Parameter/return semantics beyond the confirmed types
/// (presumably: sessions to analyze, some grouping flag, an
/// out-by-convention breakdown dictionary, an out-by-convention total) are
/// a reasonable reading of the shape, not confirmed from metadata alone.
/// </summary>
public static class BasicAnalysis
{
    public static string ComputeBasicStatistics(Session[] sessions, bool groupByHost, ref Dictionary<string, long> breakdown, ref long total)
    {
        // Diagnostic only -- see this class's own visibility note in the
        // design doc while chasing the Differ.dll NullReferenceException.
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

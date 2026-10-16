using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Clearinet.ProxyCore.Sessions;

namespace Clearinet.DesktopUi.Controls;

/// <summary>
/// Colours and names for the timing phases, close to the ones browsers'
/// developer tools use, so the waterfall reads the way people expect.
/// Mid-tone colours, readable on both light and dark backgrounds.
/// </summary>
public static class TimingPalette
{
    public static IBrush Blocked { get; } = new ImmutableSolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
    public static IBrush Dns { get; } = new ImmutableSolidColorBrush(Color.FromRgb(0x1E, 0x9C, 0x8F));
    public static IBrush Connect { get; } = new ImmutableSolidColorBrush(Color.FromRgb(0xF2, 0x99, 0x4A));
    public static IBrush Ssl { get; } = new ImmutableSolidColorBrush(Color.FromRgb(0xA2, 0x59, 0xD9));
    public static IBrush Send { get; } = new ImmutableSolidColorBrush(Color.FromRgb(0x5C, 0x8D, 0xC4));
    public static IBrush Wait { get; } = new ImmutableSolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x5A));
    public static IBrush Receive { get; } = new ImmutableSolidColorBrush(Color.FromRgb(0x1A, 0x73, 0xE8));

    /// <summary>A response CLeARINET made itself (AutoResponder, an extension, a 502).</summary>
    public static IBrush Local { get; } = new ImmutableSolidColorBrush(Color.FromRgb(0xB0, 0x8D, 0x57));

    /// <summary>The phases that apply, in order: (HAR name, label, brush, milliseconds).</summary>
    public static IEnumerable<(string Name, string Label, IBrush Brush, double Milliseconds)> Phases(SessionTimings timings)
    {
        foreach (var (name, ms) in timings.Phases)
        {
            if (ms < 0)
            {
                continue;
            }

            yield return name switch
            {
                "blocked" => (name, "Queueing", Blocked, ms),
                "dns" => (name, "DNS lookup", Dns, ms),
                "connect" => (name, timings.Proxy is null ? "Initial connection" : "Connection (through proxy)", Connect, ms),
                "ssl" => (name, "TLS handshake", Ssl, ms),
                "send" => (name, "Request sent", Send, ms),
                "wait" => (name, timings.AnsweredLocally ? "Answered by CLeARINET" : "Waiting for server (TTFB)", timings.AnsweredLocally ? Local : Wait, ms),
                _ => (name, "Content download", Receive, ms),
            };
        }
    }

    /// <summary>"0.4 ms", "12 ms", "1.25 s".</summary>
    public static string Format(double milliseconds) => milliseconds switch
    {
        < 1 => milliseconds.ToString("0.0", CultureInfo.InvariantCulture) + " ms",
        < 1000 => milliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms",
        < 60_000 => (milliseconds / 1000).ToString("0.00", CultureInfo.InvariantCulture) + " s",
        _ => TimeSpan.FromMilliseconds(milliseconds).ToString(@"m\:ss", CultureInfo.InvariantCulture) + " min",
    };

    /// <summary>The breakdown shown when hovering a waterfall bar.</summary>
    public static string Describe(Session session)
    {
        if (session.Timings is not { } timings)
        {
            return "No timing recorded for this session.";
        }

        var lines = Phases(timings).Select(p => $"{p.Label}: {Format(p.Milliseconds)}").ToList();
        lines.Add($"Total: {Format(timings.Total)}");
        if (timings.Paused > 0)
        {
            lines.Add($"(of which paused at a breakpoint: {Format(timings.Paused)})");
        }

        return string.Join(Environment.NewLine, lines);
    }
}

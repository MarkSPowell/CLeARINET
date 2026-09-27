using Clearinet.ProxyCore.Http;
using Clearinet.ProxyCore.Sessions;
using Clearinet.SampleExtension;
using Xunit;

namespace Clearinet.SampleExtension.Tests;

/// <summary>
/// <see cref="Session"/> is public-constructible (see
/// <c>ISessionImporter.cs</c>'s own corrected remarks on why that's true
/// but still not the right way to hand a session to <c>SessionStore</c>)
/// -- exporting is the one place that's actually fine to lean on directly,
/// since export only ever reads sessions that already have a real,
/// already-assigned <see cref="Session.Id"/>. This test just invents one;
/// nothing downstream of <see cref="SampleSessionExporter"/> re-derives it.
/// </summary>
public class SampleSessionExporterTests
{
    private static Session BuildSession(int id) => new(
        id,
        "api.example.com",
        DateTimeOffset.Now,
        new CapturedRequest("GET", "/widgets", "HTTP/1.1", [], []),
        new CapturedResponse("HTTP/1.1", 200, "OK", [], []));

    [Fact]
    public void ExportSessions_WritesOneLinePerSessionAndReturnsTrue()
    {
        using var exporter = new SampleSessionExporter();
        var outputPath = Path.Combine(AppContext.BaseDirectory, SampleSessionExporter.OutputFileName);
        File.Delete(outputPath);

        try
        {
            var sessions = new[] { BuildSession(1), BuildSession(2) };

            var succeeded = exporter.ExportSessions(
                SampleSessionExporter.FormatName, sessions, new Dictionary<string, object>(), progress: null);

            Assert.True(succeeded);
            Assert.True(File.Exists(outputPath));
            var lines = File.ReadAllLines(outputPath);
            // One header line plus one line per session.
            Assert.Equal(3, lines.Length);
            Assert.Contains("2 session(s)", lines[0]);
            Assert.Contains("#1 200 GET https://api.example.com/widgets", lines[1]);
            Assert.Contains("#2 200 GET https://api.example.com/widgets", lines[2]);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void ExportSessions_InvokesProgressCallbackPerSession()
    {
        using var exporter = new SampleSessionExporter();
        var outputPath = Path.Combine(AppContext.BaseDirectory, SampleSessionExporter.OutputFileName);
        File.Delete(outputPath);

        try
        {
            var progressCount = 0;
            exporter.ExportSessions(
                SampleSessionExporter.FormatName,
                [BuildSession(1)],
                new Dictionary<string, object>(),
                progress: _ => progressCount++);

            Assert.True(progressCount >= 1);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }
}

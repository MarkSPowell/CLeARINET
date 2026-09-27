using Clearinet.SampleExtension;
using Xunit;

namespace Clearinet.SampleExtension.Tests;

public class SampleSessionImporterTests
{
    [Fact]
    public void ImportSessions_ReturnsTwoFixedSessionsWithTheExpectedShape()
    {
        using var importer = new SampleSessionImporter();

        var sessions = importer.ImportSessions(SampleSessionImporter.FormatName, new Dictionary<string, object>(), progress: null);

        Assert.Equal(2, sessions.Count);
        Assert.All(sessions, s => Assert.Equal("sample.clearinet.invalid", s.Host));

        Assert.Equal("GET", sessions[0].Request.Method);
        Assert.Equal(200, sessions[0].Response.StatusCode);

        Assert.Equal("POST", sessions[1].Request.Method);
        Assert.Equal(201, sessions[1].Response.StatusCode);
    }

    [Fact]
    public void ImportSessions_InvokesProgressCallbackForEachStep()
    {
        using var importer = new SampleSessionImporter();
        var progressMessages = new List<string>();

        importer.ImportSessions(
            SampleSessionImporter.FormatName,
            new Dictionary<string, object>(),
            progress: args => progressMessages.Add(args.ProgressText));

        Assert.True(progressMessages.Count >= 2);
        Assert.Contains(progressMessages, m => m.Contains("session 1"));
        Assert.Contains(progressMessages, m => m.Contains("session 2"));
    }
}

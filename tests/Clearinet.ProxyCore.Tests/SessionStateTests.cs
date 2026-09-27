using Xunit;

namespace Clearinet.ProxyCore.Tests;

public class SessionStateTests
{
    [Fact]
    public void Created_IsTheFirstDefinedState()
    {
        Assert.Equal(0, (int)SessionState.Created);
    }

    [Fact]
    public void AllDocumentedStatesArePresent()
    {
        // Mirrors the state names documented for Fiddler Classic's
        // Session.state property (see the project plan). If this test
        // starts failing because the enum changed, that's fine -- update
        // the expected list along with the design note that changed it.
        var expected = new[]
        {
            "Created",
            "ReadingRequest",
            "AutoTamperRequestBefore",
            "HandTamperRequest",
            "AutoTamperRequestAfter",
            "SendingRequest",
            "ReadingResponse",
            "AutoTamperResponseBefore",
            "HandTamperResponse",
            "AutoTamperResponseAfter",
            "SendingResponse",
            "Done",
            "Aborted",
        };

        var actual = Enum.GetNames<SessionState>();

        Assert.Equal(expected, actual);
    }
}

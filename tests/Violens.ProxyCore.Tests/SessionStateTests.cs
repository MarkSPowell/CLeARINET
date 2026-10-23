using Xunit;

namespace Violens.ProxyCore.Tests;

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
        // Fiddler Classic's Session.state names. Update this list if the enum
        // changes on purpose.
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

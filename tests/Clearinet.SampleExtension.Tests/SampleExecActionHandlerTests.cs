using Clearinet.SampleExtension;
using Xunit;

namespace Clearinet.SampleExtension.Tests;

public class SampleExecActionHandlerTests
{
    [Fact]
    public void OnExecAction_RecognizedCommand_ReturnsTrueAndIncrementsCount()
    {
        var handler = new SampleExecActionHandler();

        var handled = handler.OnExecAction(SampleExecActionHandler.RecognizedCommand);

        Assert.True(handled);
        Assert.Equal(1, handler.RecognizedCallCount);
    }

    [Fact]
    public void OnExecAction_IsCaseInsensitive()
    {
        var handler = new SampleExecActionHandler();

        var handled = handler.OnExecAction("SAMPLE.PING");

        Assert.True(handled);
        Assert.Equal(1, handler.RecognizedCallCount);
    }

    [Fact]
    public void OnExecAction_UnrecognizedCommand_ReturnsFalseAndDoesNotIncrementCount()
    {
        var handler = new SampleExecActionHandler();

        var handled = handler.OnExecAction("something.else");

        Assert.False(handled);
        Assert.Equal(0, handler.RecognizedCallCount);
    }
}

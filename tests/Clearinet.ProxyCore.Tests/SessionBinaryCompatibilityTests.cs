using Clearinet.ProxyCore.Http;
using Clearinet.ProxyCore.Sessions;
using Xunit;

namespace Clearinet.ProxyCore.Tests;

/// <summary>
/// See Clearinet.Compatibility.Tests' BinaryCompatibilityTests: extensions
/// built against an earlier CLeARINET call the exact constructor signatures
/// they were compiled with. Adding <see cref="Session.Flags"/> changed the
/// primary constructor's signature, so the original one is kept alongside.
/// </summary>
public class SessionBinaryCompatibilityTests
{
    [Fact]
    public void SessionKeepsItsOriginalSixArgumentConstructor()
    {
        var constructor = typeof(Session).GetConstructor(
            [typeof(int), typeof(string), typeof(DateTimeOffset), typeof(CapturedRequest), typeof(CapturedResponse), typeof(SessionState)]);

        Assert.NotNull(constructor);
    }
}

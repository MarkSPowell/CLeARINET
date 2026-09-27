using System.Reflection;
using Clearinet.Compatibility.Extensions;
using Clearinet.ProxyCore.Http;
using Xunit;

namespace Clearinet.Compatibility.Tests;

/// <summary>
/// Extensions are compiled separately from CLeARINET and installed as
/// .dlls, so a public signature they were built against must keep existing,
/// or they fail at run time with MissingMethodException. That happened once:
/// adding an optional parameter to <see cref="ImportedSession"/>'s primary
/// constructor removed the four-argument one that already-built extensions
/// call. These tests pin the signatures extensions are known to use.
/// </summary>
public class BinaryCompatibilityTests
{
    [Fact]
    public void ImportedSessionKeepsItsOriginalFourArgumentConstructor()
    {
        var constructor = typeof(ImportedSession).GetConstructor(
            [typeof(string), typeof(DateTimeOffset), typeof(CapturedRequest), typeof(CapturedResponse)]);

        Assert.NotNull(constructor);
    }

    [Fact]
    public void TheOriginalConstructorLeavesFlagsNull()
    {
        var request = new CapturedRequest("GET", "/", "HTTP/1.1", [], []);
        var response = new CapturedResponse("HTTP/1.1", 200, "OK", [], []);

        var session = (ImportedSession)typeof(ImportedSession)
            .GetConstructor([typeof(string), typeof(DateTimeOffset), typeof(CapturedRequest), typeof(CapturedResponse)])!
            .Invoke(["a.test", DateTimeOffset.UnixEpoch, request, response]);

        Assert.Null(session.Flags);
        Assert.Equal("a.test", session.Host);
    }
}

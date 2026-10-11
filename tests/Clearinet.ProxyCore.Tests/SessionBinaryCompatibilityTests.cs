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

    [Fact]
    public void SessionKeepsItsSevenArgumentConstructorAndDeconstruction()
    {
        var constructor = typeof(Session).GetConstructor(
            [typeof(int), typeof(string), typeof(DateTimeOffset), typeof(CapturedRequest), typeof(CapturedResponse), typeof(SessionState),
             typeof(IReadOnlyDictionary<string, string>)]);
        Assert.NotNull(constructor);

        var deconstruct = typeof(Session).GetMethods().Where(m => m.Name == "Deconstruct").Select(m => m.GetParameters().Length);
        Assert.Contains(6, deconstruct);
        Assert.Contains(7, deconstruct);
    }

    [Fact]
    public void SessionStoreKeepsItsFiveArgumentAdd()
    {
        var add = typeof(SessionStore).GetMethod(
            "Add",
            [typeof(string), typeof(DateTimeOffset), typeof(CapturedRequest), typeof(CapturedResponse), typeof(IReadOnlyDictionary<string, string>)]);

        Assert.NotNull(add);
    }

    [Fact]
    public void OlderConstructorsMakeHttpsSessions()
    {
        var request = new CapturedRequest("GET", "/", "HTTP/1.1", [("Host", "example.test")], []);
        var response = new CapturedResponse("HTTP/1.1", 200, "OK", [], []);

        Assert.Equal("https", new Session(1, "example.test", DateTimeOffset.UtcNow, request, response, SessionState.Done).Scheme);
        Assert.Equal("https", new Session(1, "example.test", DateTimeOffset.UtcNow, request, response, SessionState.Done, null).Scheme);
        Assert.Equal("https", new SessionStore().Add("example.test", DateTimeOffset.UtcNow, request, response, null).Scheme);
    }
}

using System;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Clearinet.LegacyExtensionHost.Tests;

/// <summary>
/// Optional: exercises <see cref="LegacyExtensionLoader"/> against a folder
/// of extension DLLs a developer has already recompiled or re-targeted
/// against this shim's own assembly identity (see
/// <see cref="PatchedExtensionEnvironment"/>). Skips gracefully (an inert
/// pass, not a failure) when <c>CLEARINET_LEGACY_PATCHED_EXTENSIONS_DIR</c>
/// isn't set -- there's nothing in this repo to point it at by default,
/// since shipping a patched copy of a real extension would mean
/// redistributing a modified third-party binary (see the CompatShim
/// README's own redistribution note).
///
/// This is where the pre-"Don't get sued" version of this test suite's
/// assertions moved to: "does a correctly-targeted extension actually load
/// and run, not just construct" is still a real, valuable thing to test --
/// it's just no longer true of the five original samples unmodified. See
/// <see cref="RealExtensionLoadingTests"/> for what's asserted about those
/// five instead now, and the design doc's "Don't get sued" decision for
/// why.
/// </summary>
[Collection(PatchedExtensionsCollection.Name)]
public sealed class PatchedExtensionTests
{
    private readonly PatchedExtensionLoadFixture _fixture;
    private readonly ITestOutputHelper _output;

    public PatchedExtensionTests(PatchedExtensionLoadFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public void Load_AgainstPatchedExtensionFolder_ProducesNoLoadErrorsOrMismatches()
    {
        if (!SkipIfUnavailable())
        {
            return;
        }

        Assert.Empty(_fixture.Loader.AssemblyMismatches);
        Assert.True(_fixture.Loader.LoadErrors.Count == 0,
            "Expected zero load errors against correctly re-targeted extensions; got: " +
            string.Join(" | ", _fixture.Loader.LoadErrors));
    }

    [Fact]
    public void Load_AgainstPatchedExtensionFolder_RegistersAtLeastOneExtension()
    {
        if (!SkipIfUnavailable())
        {
            return;
        }

        Assert.NotEmpty(_fixture.Loader.FiddlerExtensions);
    }

    [Fact]
    public void Load_AgainstPatchedExtensionFolder_OnLoadRanWithoutThrowing()
    {
        if (!SkipIfUnavailable())
        {
            return;
        }

        var thrown = _fixture.CapturedLog.Where(line => line.Contains("threw:")).ToList();
        Assert.True(thrown.Count == 0, "Expected no extension OnLoad()/OnBeforeUnload() to throw; got: " + string.Join(" | ", thrown));
    }

    /// <summary>
    /// The closest thing to a real functional check this project's
    /// clean-room policy allows -- see the design doc's own remarks on
    /// this, carried over unchanged from before "Don't get sued": feeds
    /// each registered <see cref="Clearinet.CompatShim.IAutoTamper"/> a well-formed
    /// synthetic session through every hook real Fiddler Classic would
    /// have called it with, entirely through that interface's own public
    /// contract, and checks nothing throws. Trivially passes (an empty
    /// loop) if whatever's patched doesn't happen to implement
    /// IAutoTamper -- that's fine, not every extension does.
    /// </summary>
    [Fact]
    public void RegisteredAutoTampers_HandleAWellFormedSessionWithoutThrowing()
    {
        if (!SkipIfUnavailable())
        {
            return;
        }

        foreach (var autoTamper in _fixture.Loader.AutoTampers)
        {
            var typeName = autoTamper.GetType().FullName;

            RunHookAndReport(typeName, nameof(Clearinet.CompatShim.IAutoTamper.AutoTamperRequestBefore),
                () => autoTamper.AutoTamperRequestBefore(TestSessions.CreateWellFormed(1)));
            RunHookAndReport(typeName, nameof(Clearinet.CompatShim.IAutoTamper.AutoTamperRequestAfter),
                () => autoTamper.AutoTamperRequestAfter(TestSessions.CreateWellFormed(2)));
            RunHookAndReport(typeName, nameof(Clearinet.CompatShim.IAutoTamper.AutoTamperResponseBefore),
                () => autoTamper.AutoTamperResponseBefore(TestSessions.CreateWellFormed(3)));
            RunHookAndReport(typeName, nameof(Clearinet.CompatShim.IAutoTamper.AutoTamperResponseAfter),
                () => autoTamper.AutoTamperResponseAfter(TestSessions.CreateWellFormed(4)));
            RunHookAndReport(typeName, nameof(Clearinet.CompatShim.IAutoTamper.OnBeforeReturningError),
                () => autoTamper.OnBeforeReturningError(TestSessions.CreateWellFormed(5)));
        }
    }

    private static void RunHookAndReport(string typeName, string hookName, Action call)
    {
        try
        {
            call();
        }
        catch (Exception ex)
        {
            Assert.Fail($"{typeName}.{hookName} threw against a well-formed synthetic session: {ex}");
        }
    }

    private bool SkipIfUnavailable()
    {
        if (_fixture.IsAvailable)
        {
            return true;
        }

        _output.WriteLine("SKIPPED (inert pass): " + _fixture.UnavailableReason);
        return false;
    }
}

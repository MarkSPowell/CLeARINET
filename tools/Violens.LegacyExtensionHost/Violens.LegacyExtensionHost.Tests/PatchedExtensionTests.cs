using System;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Violens.LegacyExtensionHost.Tests;

/// <summary>
/// Loads a folder of retargeted extension DLLs (see
/// <see cref="PatchedExtensionEnvironment"/>) and checks they load and run.
/// Passes without doing anything unless
/// <c>VIOLENS_LEGACY_PATCHED_EXTENSIONS_DIR</c> is set.
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
    /// Runs a synthetic session through every hook of each loaded
    /// <see cref="Violens.CompatShim.IAutoTamper"/> and checks nothing throws.
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

            RunHookAndReport(typeName, nameof(Violens.CompatShim.IAutoTamper.AutoTamperRequestBefore),
                () => autoTamper.AutoTamperRequestBefore(TestSessions.CreateWellFormed(1)));
            RunHookAndReport(typeName, nameof(Violens.CompatShim.IAutoTamper.AutoTamperRequestAfter),
                () => autoTamper.AutoTamperRequestAfter(TestSessions.CreateWellFormed(2)));
            RunHookAndReport(typeName, nameof(Violens.CompatShim.IAutoTamper.AutoTamperResponseBefore),
                () => autoTamper.AutoTamperResponseBefore(TestSessions.CreateWellFormed(3)));
            RunHookAndReport(typeName, nameof(Violens.CompatShim.IAutoTamper.AutoTamperResponseAfter),
                () => autoTamper.AutoTamperResponseAfter(TestSessions.CreateWellFormed(4)));
            RunHookAndReport(typeName, nameof(Violens.CompatShim.IAutoTamper.OnBeforeReturningError),
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

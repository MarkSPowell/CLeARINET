using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Clearinet.LegacyExtensionHost.Tests;

public sealed class SelfContainedLoaderTests
{
    /// <summary>
    /// Always runs (no real DLLs needed) -- a missing/empty folder should
    /// be a logged, non-fatal no-op, matching real Fiddler's own tolerance
    /// for an empty extensions folder (a fresh install has one).
    /// </summary>
    [Fact]
    public void Load_WithMissingFolder_ProducesNoErrorsAndNoExtensions()
    {
        var missingFolder = Path.Combine(Path.GetTempPath(), "clearinet-legacy-tests-" + System.Guid.NewGuid());
        var loader = new LegacyExtensionLoader(new[] { missingFolder });

        loader.Load();

        Assert.Empty(loader.LoadErrors);
        Assert.Empty(loader.FiddlerExtensions);
        Assert.Empty(loader.AutoTampers);
        Assert.Empty(loader.ExecActionHandlers);
    }

    /// <summary>
    /// A real folder with zero .dll files in it should behave the same as
    /// a missing one -- no errors, nothing registered.
    /// </summary>
    [Fact]
    public void Load_WithEmptyFolder_ProducesNoErrorsAndNoExtensions()
    {
        var emptyFolder = Path.Combine(Path.GetTempPath(), "clearinet-legacy-tests-empty-" + System.Guid.NewGuid());
        Directory.CreateDirectory(emptyFolder);
        try
        {
            var loader = new LegacyExtensionLoader(new[] { emptyFolder });

            loader.Load();

            Assert.Empty(loader.LoadErrors);
            Assert.Empty(loader.FiddlerExtensions);
        }
        finally
        {
            Directory.Delete(emptyFolder, recursive: true);
        }
    }
}

/// <summary>
/// Exercises <see cref="LegacyExtensionLoader"/> against the five real,
/// UNMODIFIED extension DLLs (skipped gracefully when they aren't present
/// locally -- see <see cref="RealExtensionEnvironment"/>). Since the
/// design doc's "Don't get sued" decision, this shim's own assembly
/// identity is deliberately no longer named `Fiddler` (see
/// <see cref="AssemblyMismatch"/>), so every one of these five -- all
/// compiled against the real `Fiddler` assembly -- is now EXPECTED to be
/// reported as a mismatch rather than loaded. That's the thing these tests
/// actually lock in: not "these five load clean" (that milestone belonged
/// to the retired approach -- see <see cref="PatchedExtensionTests"/> for
/// its replacement), but "an unmodified real extension is detected and
/// explained clearly, never silently ignored or left to a confusing CLR
/// exception."
/// </summary>
[Collection(RealExtensionsCollection.Name)]
public sealed class RealExtensionLoadingTests
{
    private readonly RealExtensionLoadFixture _fixture;
    private readonly ITestOutputHelper _output;

    public RealExtensionLoadingTests(RealExtensionLoadFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public void Load_AgainstUnmodifiedRealExtensionFolder_ReportsAllFiveAsAssemblyMismatches()
    {
        if (!SkipIfUnavailable())
        {
            return;
        }

        // Confirmed on Mark's machine: all five real samples' own metadata
        // references assembly "Fiddler" (RequiredVersion 2.4.2.5 across the
        // board -- see the design doc's version-spread finding), which no
        // longer matches this host's own compat assembly identity.
        Assert.Equal(5, _fixture.Loader.AssemblyMismatches.Count);
        Assert.All(_fixture.Loader.AssemblyMismatches, m => Assert.Equal("Fiddler", m.ExpectedAssemblyName));

        // Fully qualified rather than a `using Clearinet.CompatShim;` up top:
        // this file only has one call site that needs the shim's own type at
        // all, and spelling it out here makes it obvious at the call site
        // which assembly's identity is actually being asserted on.
        var hostAssemblyName = typeof(Clearinet.CompatShim.FiddlerApplication).Assembly.GetName();
        Assert.All(_fixture.Loader.AssemblyMismatches, m => Assert.Equal(hostAssemblyName.Name, m.ActualAssemblyName));
    }

    [Fact]
    public void Load_AgainstUnmodifiedRealExtensionFolder_RegistersNothing()
    {
        if (!SkipIfUnavailable())
        {
            return;
        }

        // Nothing here was ever run through Assembly.LoadFrom (see
        // LegacyExtensionLoader.LoadOne's own remarks) -- a mismatched
        // extension is detected and stopped before that point, so none of
        // these should have anything in them.
        Assert.Empty(_fixture.Loader.FiddlerExtensions);
        Assert.Empty(_fixture.Loader.AutoTampers);
        Assert.Empty(_fixture.Loader.ExecActionHandlers);
        Assert.Empty(_fixture.MainForm.mnuTools.MenuItems);
        Assert.Empty(_fixture.MainForm.mnuRules.MenuItems);
    }

    [Fact]
    public void Load_AgainstUnmodifiedRealExtensionFolder_MismatchMessagesExplainWhatToDo()
    {
        if (!SkipIfUnavailable())
        {
            return;
        }

        // Not just "detected" -- the whole point of building this instead
        // of just letting the CLR's own bind failure happen is that the
        // message actually tells a technical user (or the extension's own
        // author) what to do about it. Loosely worded assertions on
        // purpose (not pinning the exact sentence), since the message text
        // itself isn't the regression-sensitive part -- these specific
        // pieces of information being present is.
        //
        // The message deliberately stops at "recompile against this host's
        // own assembly" and does NOT walk a reader through retargeting a
        // compiled third-party binary's own metadata (see
        // AssemblyMismatch.ToDiagnosticMessage's own remarks, and Mark's
        // liability/professionalism call on this) -- so this also locks in
        // that omission as intentional, not just checks what's present.
        foreach (var mismatch in _fixture.Loader.AssemblyMismatches)
        {
            var message = mismatch.ToDiagnosticMessage();
            Assert.Contains(mismatch.FileName, message);
            Assert.Contains("Fiddler", message);
            Assert.Contains(mismatch.ActualAssemblyName, message);
            Assert.Contains("recompile", message, System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("AssemblyRef", message);
            Assert.DoesNotContain("ildasm", message, System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ilasm", message, System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("dnSpy", message, System.StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Load_AgainstUnmodifiedRealExtensionFolder_LoadErrorsAlsoRecordEachMismatch()
    {
        if (!SkipIfUnavailable())
        {
            return;
        }

        // The Log tab (see Program.cs) reads LoadErrors, not
        // AssemblyMismatches directly -- so a mismatch needs to show up
        // there too, not just in the richer structured list, or it's
        // invisible to anyone not also checking the pop-up dialog.
        Assert.Equal(_fixture.Loader.AssemblyMismatches.Count, _fixture.Loader.LoadErrors.Count);
    }

    [Fact]
    public void Load_AgainstUnmodifiedRealExtensionFolder_OnLoadRanWithoutAnExtensionThrowing()
    {
        if (!SkipIfUnavailable())
        {
            return;
        }

        // Nothing should even attempt OnLoad() here (see the mismatch
        // tests above), so this should trivially hold -- kept as an
        // explicit regression guard rather than assumed, the same way it
        // was before this shim's identity changed.
        var thrown = _fixture.CapturedLog.Where(line => line.Contains("threw:")).ToList();
        Assert.True(thrown.Count == 0, "Expected no extension OnLoad()/OnBeforeUnload() to throw; got: " + string.Join(" | ", thrown));
    }

    private bool SkipIfUnavailable()
    {
        if (_fixture.IsAvailable)
        {
            return true;
        }

        _output.WriteLine("SKIPPED (inert pass, not a true xunit skip -- this repo doesn't depend on Xunit.SkippableFact): " + _fixture.UnavailableReason);
        return false;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using Clearinet.CompatShim;
using Xunit;

namespace Clearinet.LegacyExtensionHost.Tests;

/// <summary>
/// Loads the five real extension DLLs exactly once per test run (not once
/// per [Fact]) and shares the result across every test class in the
/// <see cref="RealExtensionsCollection"/> collection -- loading real,
/// unmodified third-party code and running its real <c>OnLoad()</c> hooks
/// isn't free, and repeating it per-test buys nothing since
/// <see cref="LegacyExtensionLoader"/> and <see cref="frmViewer"/> are both
/// freshly constructed here regardless.
///
/// Runs its whole setup on a dedicated STA thread (via
/// <see cref="StaThreadRunner"/>) because it constructs a real
/// <see cref="frmViewer"/> and sets <see cref="FiddlerApplication.UI"/> to
/// it before calling <see cref="LegacyExtensionLoader.Load"/> -- this
/// mirrors <c>Clearinet.LegacyExtensionHost.Program.Main</c>'s own
/// sequencing exactly, since several real extensions' <c>OnLoad()</c>
/// reach into <c>FiddlerApplication.UI</c>'s menu fields immediately.
///
/// Loads from an isolated copy of just the five expected files (see
/// <see cref="RealExtensionEnvironment.CopyExpectedFilesToIsolatedFolder"/>),
/// not <see cref="RealExtensionEnvironment.ExtensionsFolder"/> directly --
/// that folder is also the real, live LegacyExtensions folder the actual
/// host reads from and Mark drops retargeted extensions into for manual
/// testing, so pointing this suite's exact-count assertions at it directly
/// meant they'd fail the moment anything else legitimately ended up in
/// there. Implements <see cref="IDisposable"/> purely to clean that
/// temp copy up afterward.
/// </summary>
public sealed class RealExtensionLoadFixture : IDisposable
{
    public bool IsAvailable { get; }
    public string UnavailableReason { get; }

    public frmViewer MainForm { get; private set; }
    public LegacyExtensionLoader Loader { get; private set; }
    public IReadOnlyList<string> CapturedLog { get; private set; }

    private readonly string _isolatedFolder;

    public RealExtensionLoadFixture()
    {
        IsAvailable = RealExtensionEnvironment.IsAvailable;
        UnavailableReason = RealExtensionEnvironment.UnavailableReason;

        if (!IsAvailable)
        {
            return;
        }

        _isolatedFolder = RealExtensionEnvironment.CopyExpectedFilesToIsolatedFolder();

        StaThreadRunner.Run(() =>
        {
            var log = new List<string>();
            var mainForm = new frmViewer();
            FiddlerApplication.UI = mainForm;

            var loader = new LegacyExtensionLoader(new[] { _isolatedFolder }, log.Add);
            loader.Load();

            MainForm = mainForm;
            Loader = loader;
            CapturedLog = log;
        });
    }

    public void Dispose()
    {
        if (_isolatedFolder is null || !Directory.Exists(_isolatedFolder))
        {
            return;
        }

        try
        {
            Directory.Delete(_isolatedFolder, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup of a temp folder -- not worth failing the
            // test run over a locked/in-use file on the way out.
        }
        catch (UnauthorizedAccessException)
        {
            // Same reasoning as the IOException case above.
        }
    }
}

[CollectionDefinition(Name)]
public sealed class RealExtensionsCollection : ICollectionFixture<RealExtensionLoadFixture>
{
    public const string Name = "RealExtensions";
}

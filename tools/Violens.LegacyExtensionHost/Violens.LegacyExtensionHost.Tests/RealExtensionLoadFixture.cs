using System;
using System.Collections.Generic;
using System.IO;
using Violens.CompatShim;
using Xunit;

namespace Violens.LegacyExtensionHost.Tests;

/// <summary>
/// Loads the five sample DLLs once per test run, from an isolated copy (see
/// <see cref="RealExtensionEnvironment.CopyExpectedFilesToIsolatedFolder"/>),
/// on an STA thread, after setting <see cref="FiddlerApplication.UI"/> as
/// the host does, since some extensions' OnLoad uses its menus straight
/// away. Disposing deletes the copy.
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

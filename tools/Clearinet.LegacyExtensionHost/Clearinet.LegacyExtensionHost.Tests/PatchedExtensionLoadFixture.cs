using System;
using System.Collections.Generic;
using Clearinet.CompatShim;
using Xunit;

namespace Clearinet.LegacyExtensionHost.Tests;

/// <summary>
/// Same shape and sequencing as <see cref="RealExtensionLoadFixture"/> --
/// see that class's own remarks for why setup runs once per test run, on a
/// dedicated STA thread -- pointed at <see cref="PatchedExtensionEnvironment"/>
/// instead of the fixed five real samples.
/// </summary>
public sealed class PatchedExtensionLoadFixture
{
    public bool IsAvailable { get; }
    public string UnavailableReason { get; }

    public frmViewer MainForm { get; private set; }
    public LegacyExtensionLoader Loader { get; private set; }
    public IReadOnlyList<string> CapturedLog { get; private set; }

    public PatchedExtensionLoadFixture()
    {
        IsAvailable = PatchedExtensionEnvironment.IsAvailable;
        UnavailableReason = PatchedExtensionEnvironment.UnavailableReason;

        if (!IsAvailable)
        {
            return;
        }

        StaThreadRunner.Run(() =>
        {
            var log = new List<string>();
            var mainForm = new frmViewer();
            FiddlerApplication.UI = mainForm;

            var loader = new LegacyExtensionLoader(new[] { PatchedExtensionEnvironment.ExtensionsFolder }, log.Add);
            loader.Load();

            MainForm = mainForm;
            Loader = loader;
            CapturedLog = log;
        });
    }
}

[CollectionDefinition(Name)]
public sealed class PatchedExtensionsCollection : ICollectionFixture<PatchedExtensionLoadFixture>
{
    public const string Name = "PatchedExtensions";
}

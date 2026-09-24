using System;

namespace Clearinet.LegacyExtensionHost;

/// <summary>
/// Reports that a scanned extension `.dll`'s own metadata references an
/// assembly it expects to be able to bind to (by the project's own
/// convention, always named literally "Fiddler" for a real Fiddler
/// Classic extension -- see <see cref="FiddlerAssemblyReferenceInspector"/>)
/// that doesn't match this host's own compat assembly identity
/// (<c>Clearinet.CompatShim</c>, not <c>Fiddler</c> -- see the design doc's
/// "Don't get sued" decision for why). Surfaced instead of just letting the
/// CLR's own bind failure happen and produce a generic
/// <see cref="System.IO.FileNotFoundException"/>-flavored message deep
/// inside a stack trace.
/// </summary>
public sealed class AssemblyMismatch
{
    public AssemblyMismatch(string fileName, string expectedAssemblyName, Version expectedAssemblyVersion, string actualAssemblyName, Version actualAssemblyVersion)
    {
        FileName = fileName;
        ExpectedAssemblyName = expectedAssemblyName;
        ExpectedAssemblyVersion = expectedAssemblyVersion;
        ActualAssemblyName = actualAssemblyName;
        ActualAssemblyVersion = actualAssemblyVersion;
    }

    /// <summary>The extension `.dll`'s own file name (not a full path).</summary>
    public string FileName { get; }

    /// <summary>The assembly name this extension's own metadata references (e.g. "Fiddler").</summary>
    public string ExpectedAssemblyName { get; }

    /// <summary>The version that reference declares.</summary>
    public Version ExpectedAssemblyVersion { get; }

    /// <summary>This host's own compat assembly's actual name (e.g. "Clearinet.CompatShim").</summary>
    public string ActualAssemblyName { get; }

    /// <summary>This host's own compat assembly's actual version.</summary>
    public Version ActualAssemblyVersion { get; }

    /// <summary>
    /// A complete, human-readable explanation of what's wrong and why --
    /// deliberately restrained about what to do next. States the one
    /// remediation this host actually stands behind (recompiling against
    /// this host's own compatibility assembly, for whoever holds the
    /// extension's source) and stops there, rather than walking a reader
    /// through retargeting a compiled third-party binary's own metadata
    /// (the earlier version of this message did, e.g. naming
    /// <c>ildasm</c>/<c>ilasm</c>/dnSpy directly) -- see the CLeARINET
    /// .NET Extension Compatibility Design doc's "Don't get sued" decision,
    /// which this message exists to reflect: the whole point of dropping
    /// this host's assembly identity was to stop presenting itself as the
    /// genuine Fiddler assembly, and a diagnostic that then hands out
    /// step-by-step instructions for making a third-party binary bind to
    /// it anyway sits uncomfortably close to undoing that on its own.
    /// </summary>
    public string ToDiagnosticMessage() =>
        $"{FileName}: this extension expects an assembly named \"{ExpectedAssemblyName}\" " +
        $"(version {ExpectedAssemblyVersion}), which this host does not provide. This host's own " +
        $"compatibility assembly is named \"{ActualAssemblyName}\" (version {ActualAssemblyVersion}) " +
        "instead, so this extension cannot be loaded as distributed. This is intentional, not a defect " +
        "(see the CLeARINET .NET Extension Compatibility Design doc's \"Don't get sued\" decision). " +
        $"If you have this extension's source code, it can be recompiled to reference " +
        $"\"{ActualAssemblyName}\" directly.";
}

using System;

namespace Violens.LegacyExtensionHost;

/// <summary>
/// An extension DLL that references the <c>Fiddler</c> assembly, which this
/// host's shim (<c>Violens.CompatShim</c>) isn't. Reported plainly rather
/// than left to a confusing CLR bind error.
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

    /// <summary>This host's own compat assembly's actual name (e.g. "Violens.CompatShim").</summary>
    public string ActualAssemblyName { get; }

    /// <summary>This host's own compat assembly's actual version.</summary>
    public Version ActualAssemblyVersion { get; }

    /// <summary>
    /// Explains the problem and the fix we support: recompiling against this
    /// host's assembly. It deliberately doesn't explain how to patch someone
    /// else's binary, which would undo the point of not calling ourselves
    /// Fiddler.
    /// </summary>
    public string ToDiagnosticMessage() =>
        $"{FileName}: this extension expects an assembly named \"{ExpectedAssemblyName}\" " +
        $"(version {ExpectedAssemblyVersion}), which this host does not provide. This host's own " +
        $"compatibility assembly is named \"{ActualAssemblyName}\" (version {ActualAssemblyVersion}) " +
        "instead, so this extension cannot be loaded as distributed. This is intentional, not a defect " +
        "(see the VioLens .NET Extension Compatibility Design doc's \"Don't get sued\" decision). " +
        $"If you have this extension's source code, it can be recompiled to reference " +
        $"\"{ActualAssemblyName}\" directly.";
}

using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Clearinet.LegacyExtensionHost;

/// <summary>
/// Reads a candidate extension `.dll`'s own <c>AssemblyRef</c> metadata
/// table (ECMA-335 II.22.5) directly, via <see cref="PEReader"/>/
/// <see cref="MetadataReader"/> -- the same "read the reference tables,
/// never the IL method bodies" methodology this whole project's clean-room
/// research has used throughout (see the design doc's own metadata-reading
/// sections), just via the purpose-built .NET API for it instead of a
/// hand-rolled byte parser. Crucially, this never loads or executes the
/// candidate assembly (no <see cref="System.Reflection.Assembly.LoadFrom"/>
/// call here) -- it's a pure metadata read of the file's bytes, so it's
/// safe to run on a `.dll` this host has no intention of actually loading.
/// </summary>
internal static class FiddlerAssemblyReferenceInspector
{
    /// <summary>
    /// Looks for an <c>AssemblyRef</c> row named, case-insensitively,
    /// "Fiddler" -- the one real, historical Fiddler Classic assembly name
    /// every sample this project inspected declares. Returns
    /// <see langword="false"/> if the file isn't a readable .NET assembly
    /// at all, or references nothing by that name (which is a normal,
    /// non-error outcome -- plenty of `.dll`s dropped into this folder by
    /// mistake won't be Fiddler Classic extensions at all).
    /// </summary>
    public static bool TryFindFiddlerReference(string dllPath, out string name, out Version version)
    {
        name = null;
        version = null;

        try
        {
            using var stream = File.OpenRead(dllPath);
            using var peReader = new PEReader(stream);

            if (!peReader.HasMetadata)
            {
                return false;
            }

            var metadataReader = peReader.GetMetadataReader();
            foreach (var handle in metadataReader.AssemblyReferences)
            {
                var reference = metadataReader.GetAssemblyReference(handle);
                var referenceName = metadataReader.GetString(reference.Name);
                if (string.Equals(referenceName, "Fiddler", StringComparison.OrdinalIgnoreCase))
                {
                    name = referenceName;
                    version = reference.Version;
                    return true;
                }
            }

            return false;
        }
        catch (BadImageFormatException)
        {
            // Not a readable .NET assembly (native DLL, corrupt file,
            // etc.) -- not this inspector's problem to report; the normal
            // Assembly.LoadFrom attempt downstream will surface a sensible
            // error for that case instead.
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}

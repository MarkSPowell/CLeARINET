using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Violens.LegacyExtensionHost;

/// <summary>
/// Reads a DLL's AssemblyRef table with <see cref="MetadataReader"/>,
/// without loading or running it.
/// </summary>
internal static class FiddlerAssemblyReferenceInspector
{
    /// <summary>
    /// Finds a reference to an assembly named "Fiddler". False if the file
    /// isn't a .NET assembly or has no such reference.
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

using Clearinet.Compatibility.Extensions;

// ExtensionHost silently skips any assembly with no RequiredVersionAttribute
// at all -- reproducing real Fiddler's own documented "Assemblies which do
// not contain a RequiredVersion attribute are silently ignored" behavior --
// so this is the one thing that makes this .dll visible to CLeARINET at all
// once it's dropped into ExtensionHost.DefaultExtensionsFolder. "0.1.0.0"
// matches ClearinetVersion.Current exactly at the time this sample was
// written; if a later CLeARINET build bumps that version, this line is the
// one place to raise the minimum to match, the same way a real ported
// extension's own AssemblyInfo.cs would need updating.
[assembly: RequiredVersion("0.1.0.0")]

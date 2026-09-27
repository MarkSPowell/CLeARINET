using System.Runtime.CompilerServices;

// Lets Clearinet.ProxyCore.Tests reach a handful of internal members
// directly (currently just WinInetSystemProxy.MergeBypassList) instead of
// only testing through this assembly's public surface. Kept to one small,
// explicit attribute rather than a project-wide convention, since almost
// everything else in this assembly is already tested through its public
// API.
[assembly: InternalsVisibleTo("Clearinet.ProxyCore.Tests")]

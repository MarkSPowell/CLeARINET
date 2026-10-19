using System.Runtime.CompilerServices;

// ProxyCore's public types are what other apps build on (see the Embedding
// ProxyCore guide), so they're kept to what an app needs: everything else
// is internal. VioLens's own assemblies still use some of those internals
// (the session filter, Find and statistics, the AutoResponder rules file,
// the proxy page's host name), so they're listed here. Anything added to
// this list is reaching past the public surface on purpose.
[assembly: InternalsVisibleTo("Violens.ProxyCore.Tests")]
[assembly: InternalsVisibleTo("Violens.AppCore")]
[assembly: InternalsVisibleTo("Violens.AppCore.Tests")]
[assembly: InternalsVisibleTo("Violens.Compatibility")]
[assembly: InternalsVisibleTo("Violens.Extensibility")]
[assembly: InternalsVisibleTo("Violens.DesktopUi")]

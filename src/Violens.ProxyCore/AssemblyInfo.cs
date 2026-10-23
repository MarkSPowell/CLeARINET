using System.Runtime.CompilerServices;

// Only what embedding apps need is public. VioLens's own assemblies use
// a few internals, listed here.
[assembly: InternalsVisibleTo("Violens.ProxyCore.Tests")]
[assembly: InternalsVisibleTo("Violens.AppCore")]
[assembly: InternalsVisibleTo("Violens.AppCore.Tests")]
[assembly: InternalsVisibleTo("Violens.Compatibility")]
[assembly: InternalsVisibleTo("Violens.Extensibility")]
[assembly: InternalsVisibleTo("Violens.DesktopUi")]

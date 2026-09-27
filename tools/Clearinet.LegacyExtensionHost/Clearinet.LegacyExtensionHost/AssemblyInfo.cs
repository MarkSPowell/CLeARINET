using System.Runtime.CompilerServices;

// Lets Clearinet.LegacyExtensionHost.Tests exercise SessionBridgeServer/
// SessionBridgeRunner/SessionMapping directly -- these stay `internal`
// (genuine implementation details of this host, not part of any public
// surface either the CompatShim or another project needs) rather than
// widened to `public` just so tests can reach them. See
// SessionBridgeRunnerTests and SessionBridgeServerTests for what this
// enables.
[assembly: InternalsVisibleTo("Clearinet.LegacyExtensionHost.Tests")]

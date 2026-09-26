// The only file CLeARINET adds to Eric Lawrence's FiddlerImportNetlog. See
// ../README.md for the whole port (port.ps1 also deletes each upstream
// `using Fiddler;` line; nothing else changes).
//
// Imports the Fiddler-shaped compatibility layer for every file, and lets
// qualified names such as `Fiddler.Parser.ParseRequest(...)` and
// `[assembly: Fiddler.RequiredVersion("4.6.0.0")]` keep compiling as written.
// The alias exists only inside this extension's own project; CLeARINET's
// own assemblies and namespaces never use the name (see the .NET Extension
// Compatibility Design doc, "Don't get sued").

global using Clearinet.CompatShim;
global using Fiddler = Clearinet.CompatShim;

using System.Reflection;
using System.Runtime.Loader;
using Clearinet.ProxyCore;
using Clearinet.ProxyCore.Extensions;

namespace Clearinet.Compatibility.Extensions;

/// <summary>
/// The disk/reflection/<see cref="AssemblyLoadContext"/> half of the
/// extension-host split -- see <see cref="LoadedExtensionSet"/>'s own
/// remarks for the pure, testable half this class feeds. Scans one or more
/// folders for <c>.dll</c> files, loads each into its own isolated,
/// collectible <see cref="AssemblyLoadContext"/>, applies Fiddler Classic's
/// own documented <c>RequiredVersion</c> gating
/// (fiddlerbook.com/fiddler/dev/IFiddlerExtension.asp -- see
/// <see cref="RequiredVersionAttribute"/>'s own remarks), and constructs one
/// instance of every concrete, parameterless-constructible type it finds
/// implementing any of this namespace's extension interfaces.
///
/// Scoped deliberately as "load once at startup, unload once at shutdown" --
/// no live hot-reload for compiled extensions, unlike
/// <see cref="Clearinet.Compatibility.FiddlerScript.FiddlerScriptRunner"/>'s
/// own <c>Reload()</c>. Real Fiddler Classic doesn't hot-reload compiled
/// extensions either (only FiddlerScript's own <c>CustomRules.js</c> gets
/// that treatment), so this isn't a cut corner -- it's matching the thing
/// being reproduced.
///
/// <b>Not thread-safe.</b> <see cref="Load"/>/<see cref="Unload"/> are meant
/// to be called once each, from a composition root's own startup/shutdown
/// sequence (see the .NET Extension Compatibility Design doc's wiring
/// section) -- never concurrently with each other or with the proxy running.
/// </summary>
public sealed class ExtensionHost
{
    private readonly IReadOnlyList<string> _extensionFolders;
    private readonly Action<string> _log;
    private readonly List<(string Folder, bool Existed, int DllFilesFound)> _scanResults = new();
    private readonly List<string> _loadErrors = new();
    private readonly List<AssemblyLoadContext> _loadContexts = new();
    private readonly List<IFiddlerExtension> _fiddlerExtensions = new();
    private readonly List<IAutoTamper> _autoTampers = new();
    private readonly List<IRequestInspector2> _requestInspectors = new();
    private readonly List<IResponseInspector2> _responseInspectors = new();
    private readonly List<ISessionImporter> _importers = new();
    private readonly List<ISessionExporter> _exporters = new();
    private readonly List<IHandleExecAction> _execActionHandlers = new();
    private readonly List<Clearinet.CompatShim.IFiddlerExtension> _shimExtensions = new();
    private readonly List<Clearinet.CompatShim.IAutoTamper> _shimAutoTampers = new();

    /// <param name="extensionFolders">
    /// One or more folders to scan for <c>.dll</c> files, non-recursively --
    /// matching real Fiddler's own documented convention of a flat
    /// <c>Scripts</c> folder rather than a recursive search
    /// (fiddlerbook.com/fiddler/dev/IFiddlerExtension.asp names
    /// <c>%Program Files%\Fiddler2\Scripts</c> and <c>%USERPROFILE%\My
    /// Documents\Fiddler2\Scripts</c> specifically). A caller that wants
    /// CLeARINET's own equivalent folder(s) can use
    /// <see cref="DefaultExtensionsFolder"/> rather than hand-rolling a
    /// Fiddler-branded path.
    /// </param>
    /// <param name="log">Where a load failure or a hook throwing gets logged -- see <see cref="LoadErrors"/> for load-time failures specifically. Defaults to a no-op.</param>
    public ExtensionHost(IEnumerable<string> extensionFolders, Action<string>? log = null)
    {
        _extensionFolders = extensionFolders.ToList();
        _log = log ?? (_ => { });
    }

    /// <summary>
    /// CLeARINET's own equivalent of Fiddler's <c>My Documents\Fiddler2\Scripts</c>
    /// -- a CLeARINET-branded folder under the user's own Documents folder,
    /// not a copy of Fiddler's path (this project ships no Fiddler-branded
    /// paths or names anywhere -- see the clean-room policy the Project Plan
    /// describes). Callers are free to pass their own folder list to the
    /// constructor instead; this is offered only as a sensible default.
    ///
    /// <b>This is not necessarily the literal <c>%USERPROFILE%\Documents</c>
    /// path.</b> <see cref="Environment.SpecialFolder.MyDocuments"/> resolves
    /// through the Windows shell's own registered Documents folder, and
    /// OneDrive's "Known Folder Move" feature commonly redirects that to
    /// somewhere under <c>%USERPROFILE%\OneDrive\Documents</c> instead --
    /// on a machine where that's happened (a repo cloned to
    /// <c>...\OneDrive\Documents\GitHub\...</c> is a strong sign it has),
    /// this property follows the redirection, so the real folder to drop a
    /// compiled extension's <c>.dll</c> into is wherever THIS property
    /// actually resolves to, not necessarily the literal, unredirected
    /// path. <c>ExtensionHost.Load()</c> logs the exact folder it scanned
    /// (and how many <c>.dll</c> files it found there) for exactly this
    /// reason -- check that line before assuming an extension failed to
    /// load for any more interesting reason.
    /// </summary>
    public static string DefaultExtensionsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CLeARINET", "Extensions");

    /// <summary>
    /// The <c>Extensions</c> folder next to the app itself, where the Windows
    /// installer puts the optional extensions picked during setup. Scanned
    /// after <see cref="DefaultExtensionsFolder"/>, so a copy the user put in
    /// their own folder wins over the installed one of the same file name
    /// (see <see cref="Load"/>).
    /// </summary>
    public static string BundledExtensionsFolder =>
        Path.Combine(AppContext.BaseDirectory, "Extensions");

    /// <summary>
    /// One entry per folder <see cref="Load"/> was given, in order: whether
    /// that folder existed, and how many <c>.dll</c> files were found in it
    /// (0 for a folder that didn't exist at all). Populated by
    /// <see cref="Load"/>; empty before that's been called. Exists so a UI
    /// can show "found the right folder but nothing was in it" as a
    /// different, more specific state than "an extension loaded" or "an
    /// extension failed to load" -- exactly the gap that made an empty or
    /// wrong extensions folder indistinguishable from a successful "nothing
    /// to load" run before this was added (see <see cref="DefaultExtensionsFolder"/>'s
    /// own remarks on the OneDrive-redirected-Documents case that motivated
    /// it).
    /// </summary>
    public IReadOnlyList<(string Folder, bool Existed, int DllFilesFound)> ScanResults => _scanResults;

    /// <summary>Every problem hit while scanning -- a missing/unparseable/too-old <c>RequiredVersion</c>, a type that couldn't be constructed, an assembly that failed to load outright. Never includes an assembly silently skipped for having no <see cref="RequiredVersionAttribute"/> at all -- that case is intentionally silent, matching real Fiddler's own documented behavior (see this class's own remarks).</summary>
    public IReadOnlyList<string> LoadErrors => _loadErrors;

    /// <summary>Every loaded extension implementing <see cref="IAutoTamper"/> (or a descendant: <see cref="IAutoTamper2"/>/<see cref="IAutoTamper3"/>), in discovery order.</summary>
    public IReadOnlyList<IAutoTamper> AutoTampers => _autoTampers;

    /// <summary>Every loaded extension implementing <see cref="IRequestInspector2"/>.</summary>
    public IReadOnlyList<IRequestInspector2> RequestInspectors => _requestInspectors;

    /// <summary>Every loaded extension implementing <see cref="IResponseInspector2"/>.</summary>
    public IReadOnlyList<IResponseInspector2> ResponseInspectors => _responseInspectors;

    /// <summary>Every loaded extension implementing <see cref="ISessionImporter"/>.</summary>
    public IReadOnlyList<ISessionImporter> Importers => _importers;

    /// <summary>Every loaded extension implementing <see cref="ISessionExporter"/>.</summary>
    public IReadOnlyList<ISessionExporter> Exporters => _exporters;

    /// <summary>Every loaded extension implementing <see cref="IHandleExecAction"/>.</summary>
    public IReadOnlyList<IHandleExecAction> ExecActionHandlers => _execActionHandlers;

    /// <summary>
    /// Wraps <see cref="AutoTampers"/> in a fresh <see cref="LoadedExtensionSet"/>
    /// for handing to <c>InterceptingProxyListener</c> as an
    /// <see cref="IExtensionAutoTamperHost"/> -- the composition root's usual
    /// next call after <see cref="Load"/>.
    /// </summary>
    public IExtensionAutoTamperHost CreateAutoTamperHost() => new LoadedExtensionSet(_autoTampers, _log);

    /// <summary>
    /// Ported Fiddler-shaped extensions that implement
    /// <see cref="Clearinet.CompatShim.IAutoTamper"/>, in load order.
    /// </summary>
    public IReadOnlyList<Clearinet.CompatShim.IAutoTamper> ShimAutoTampers => _shimAutoTampers;

    /// <summary>
    /// Every loaded ported Fiddler-shaped extension
    /// (<see cref="Clearinet.CompatShim.IFiddlerExtension"/>), in load order.
    /// </summary>
    public IReadOnlyList<Clearinet.CompatShim.IFiddlerExtension> ShimExtensions => _shimExtensions;

    /// <summary>
    /// Runs <see cref="ShimAutoTampers"/> against live traffic with one
    /// session object per request -- see <see cref="ShimAutoTamperSet"/>.
    /// Handed to the proxy listener alongside <see cref="CreateAutoTamperHost"/>.
    /// </summary>
    public IExtensionSessionHost CreateSessionHost() => new ShimAutoTamperSet(_shimAutoTampers, _log);

    /// <summary>
    /// Scans every configured folder, loads and gates every <c>.dll</c> found
    /// (non-recursively, silently skipping any subfolder), skips a <c>.dll</c>
    /// whose file name was already loaded from an earlier folder (so one
    /// extension installed in two places loads once, from the first), constructs one
    /// instance of every applicable type, and calls <see cref="IFiddlerExtension.OnLoad"/>
    /// on each one. Safe to call when a folder doesn't exist -- that's not
    /// itself a load error, since an extensions folder with nothing in it
    /// yet is the common case, not a misconfiguration.
    /// </summary>
    public void Load()
    {
        var loadedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in _extensionFolders)
        {
            if (!Directory.Exists(folder))
            {
                // Logged, not silent -- a wrong or unexpectedly-redirected
                // folder (see DefaultExtensionsFolder's own remarks on
                // OneDrive's Documents redirection) used to look identical
                // to "nothing loaded because there's nothing to load," with
                // no way to tell the two apart from the console alone.
                _log($"Extensions folder not found, nothing to scan there: {folder}");
                _scanResults.Add((folder, Existed: false, DllFilesFound: 0));
                continue;
            }

            var dllPaths = Directory.EnumerateFiles(folder, "*.dll", SearchOption.TopDirectoryOnly).ToList();
            _log($"Scanning {folder}: found {dllPaths.Count} .dll file(s).");
            _scanResults.Add((folder, Existed: true, DllFilesFound: dllPaths.Count));
            foreach (var dllPath in dllPaths)
            {
                if (!loadedFileNames.Add(Path.GetFileName(dllPath)))
                {
                    _log($"Skipped {dllPath}: a {Path.GetFileName(dllPath)} from an earlier folder is already loaded.");
                    continue;
                }

                LoadOne(dllPath);
            }
        }

        foreach (var extension in _fiddlerExtensions)
        {
            RunSafely(extension, nameof(IFiddlerExtension.OnLoad), extension.OnLoad);
        }

        foreach (var extension in _shimExtensions)
        {
            RunSafely(extension, nameof(Clearinet.CompatShim.IFiddlerExtension.OnLoad), extension.OnLoad);
        }
    }

    /// <summary>
    /// Calls <see cref="IFiddlerExtension.OnBeforeUnload"/> on every loaded
    /// extension, then unloads every extension's <see cref="AssemblyLoadContext"/>.
    /// The unload itself is best-effort and asynchronous by .NET's own design
    /// -- an <see cref="AssemblyLoadContext"/> only actually unloads once
    /// every reference to its types/instances is dropped and collected, which
    /// this call alone can't force; it starts that process rather than
    /// completing it synchronously. Fine for this project's own "unload at
    /// app shutdown" scope, where nothing depends on the memory being
    /// reclaimed before the process itself exits.
    /// </summary>
    public void Unload()
    {
        foreach (var extension in _fiddlerExtensions)
        {
            RunSafely(extension, nameof(IFiddlerExtension.OnBeforeUnload), extension.OnBeforeUnload);
        }

        foreach (var extension in _shimExtensions)
        {
            RunSafely(extension, nameof(Clearinet.CompatShim.IFiddlerExtension.OnBeforeUnload), extension.OnBeforeUnload);
        }

        foreach (var context in _loadContexts)
        {
            context.Unload();
        }
    }

    private void LoadOne(string dllPath)
    {
        var fileName = Path.GetFileName(dllPath);
        ExtensionLoadContext? context = null;
        try
        {
            var resolver = new AssemblyDependencyResolver(dllPath);
            context = new ExtensionLoadContext(fileName, resolver);
            var assembly = context.LoadFromAssemblyPath(dllPath);

            var requiredVersion = assembly.GetCustomAttribute<RequiredVersionAttribute>();
            if (requiredVersion is null)
            {
                // Not a CLeARINET-native extension -- but it may be a ported
                // Fiddler Classic one, built against the Fiddler-shaped
                // compatibility layer (Clearinet.CompatShim). Its
                // RequiredVersion names a Fiddler version, so it's checked
                // against the Fiddler API level that layer stands in for,
                // not against CLeARINET's own version.
                var fiddlerRequiredVersion = assembly.GetCustomAttribute<Clearinet.CompatShim.RequiredVersionAttribute>();
                if (fiddlerRequiredVersion is not null)
                {
                    if (!Version.TryParse(fiddlerRequiredVersion.RequiredVersion, out var fiddlerMinimum))
                    {
                        _loadErrors.Add($"{fileName}: RequiredVersion \"{fiddlerRequiredVersion.RequiredVersion}\" isn't a parseable version -- skipped.");
                        context.Unload();
                        return;
                    }

                    if (Clearinet.CompatShim.CompatShimHost.FiddlerApiLevel < fiddlerMinimum)
                    {
                        _loadErrors.Add($"{fileName}: needs Fiddler API {fiddlerMinimum}, newer than the {Clearinet.CompatShim.CompatShimHost.FiddlerApiLevel.ToString(2)}.x this build's compatibility layer covers -- skipped.");
                        context.Unload();
                        return;
                    }

                    LoadTypes(assembly, fileName, context);
                    return;
                }

                // Silently ignored -- reproducing real Fiddler's own
                // documented behavior exactly (see this class's own
                // remarks). Not an error: a stray, unrelated .dll sitting in
                // the extensions folder is expected, not a misconfiguration.
                context.Unload();
                return;
            }

            if (!Version.TryParse(requiredVersion.MinimumVersion, out var minimumVersion))
            {
                _loadErrors.Add($"{fileName}: RequiredVersion \"{requiredVersion.MinimumVersion}\" isn't a parseable version -- skipped.");
                context.Unload();
                return;
            }

            if (ClearinetVersion.Current < minimumVersion)
            {
                _loadErrors.Add($"{fileName}: requires CLeARINET {minimumVersion} or later, this build is {ClearinetVersion.Current} -- skipped.");
                context.Unload();
                return;
            }

            LoadTypes(assembly, fileName, context);
        }
        catch (Exception ex)
        {
            _loadErrors.Add($"{fileName}: failed to load -- {ex.Message}");
            context?.Unload();
        }
    }

    /// <summary>
    /// Instantiates and registers every extension type in an assembly that
    /// already passed its RequiredVersion check (either kind -- see
    /// <see cref="LoadOne"/>), keeping its load context if anything was
    /// found and unloading it otherwise.
    /// </summary>
    private void LoadTypes(Assembly assembly, string fileName, AssemblyLoadContext context)
    {
        var typesLoaded = false;
        foreach (var type in GetLoadableTypes(assembly))
        {
            if (type.IsAbstract || type.IsInterface || type.GetConstructor(Type.EmptyTypes) is null)
            {
                continue;
            }

            if (!ImplementsAnyExtensionInterface(type))
            {
                continue;
            }

            object instance;
            try
            {
                instance = Activator.CreateInstance(type)!;
            }
            catch (Exception ex)
            {
                _loadErrors.Add($"{fileName}: couldn't construct {type.FullName} -- {ex.Message}");
                continue;
            }

            Register(instance);
            typesLoaded = true;
        }

        if (typesLoaded)
        {
            _loadContexts.Add(context);
        }
        else
        {
            // Had a valid RequiredVersion but nothing usable inside it --
            // still worth surfacing, unlike the no-attribute-at-all case
            // above, since this one DID declare itself an extension.
            _loadErrors.Add($"{fileName}: declared RequiredVersion but no recognized extension type was found inside it.");
            context.Unload();
        }
    }

    private void Register(object instance)
    {
        // One instance can implement several of these interfaces at once
        // (real Fiddler's own SampleExtension does exactly this, per
        // IHandleExecAction's own remarks) -- every applicable list gets the
        // SAME instance, never a separate one per interface.
        if (instance is IFiddlerExtension fiddlerExtension)
        {
            _fiddlerExtensions.Add(fiddlerExtension);
        }

        if (instance is IAutoTamper autoTamper)
        {
            _autoTampers.Add(autoTamper);
        }

        if (instance is IRequestInspector2 requestInspector)
        {
            _requestInspectors.Add(requestInspector);
        }

        if (instance is IResponseInspector2 responseInspector)
        {
            _responseInspectors.Add(responseInspector);
        }

        if (instance is ISessionImporter importer)
        {
            _importers.Add(importer);
        }

        // A ported Fiddler-shaped importer joins the same list, wrapped so
        // File > Import can't tell it apart from a native one.
        if (instance is Clearinet.CompatShim.ISessionImporter fiddlerShapedImporter)
        {
            _importers.Add(new CompatShimImporterAdapter(fiddlerShapedImporter));
        }

        if (instance is ISessionExporter exporter)
        {
            _exporters.Add(exporter);
        }

        if (instance is IHandleExecAction execActionHandler)
        {
            _execActionHandlers.Add(execActionHandler);
        }

        // Ported Fiddler-shaped extensions: OnLoad/OnBeforeUnload, and live
        // traffic hooks through CreateSessionHost.
        if (instance is Clearinet.CompatShim.IFiddlerExtension shimExtension)
        {
            _shimExtensions.Add(shimExtension);
        }

        if (instance is Clearinet.CompatShim.IAutoTamper shimAutoTamper)
        {
            _shimAutoTampers.Add(shimAutoTamper);
        }
    }

    private static bool ImplementsAnyExtensionInterface(Type type) =>
        typeof(IFiddlerExtension).IsAssignableFrom(type) ||
        typeof(IAutoTamper).IsAssignableFrom(type) ||
        typeof(IRequestInspector2).IsAssignableFrom(type) ||
        typeof(IResponseInspector2).IsAssignableFrom(type) ||
        typeof(ISessionImporter).IsAssignableFrom(type) ||
        typeof(Clearinet.CompatShim.ISessionImporter).IsAssignableFrom(type) ||
        typeof(Clearinet.CompatShim.IFiddlerExtension).IsAssignableFrom(type) ||
        typeof(ISessionExporter).IsAssignableFrom(type) ||
        typeof(IHandleExecAction).IsAssignableFrom(type);

    /// <summary>
    /// <see cref="Assembly.GetExportedTypes"/> throws <see cref="ReflectionTypeLoadException"/>
    /// whole-assembly if even one exported type can't be resolved (a missing
    /// dependency, commonly) -- this recovers the types that DID load fine
    /// instead of losing every type in the assembly over one bad one,
    /// matching the same "one bad thing doesn't take down everything else"
    /// posture the rest of this class follows.
    /// </summary>
    /// <summary>
    /// The assembly's public types, skipping any that can't be loaded. A
    /// type whose base class lives in an assembly that isn't available (an
    /// extension's Avalonia view, when run somewhere without Avalonia, such
    /// as a headless test) makes <see cref="Assembly.GetExportedTypes"/>
    /// fail outright; <see cref="Assembly.GetTypes"/> instead reports the
    /// types it could load, so the extension's other types still register.
    /// </summary>
    private IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetExportedTypes();
        }
        catch (Exception ex) when (ex is ReflectionTypeLoadException or FileNotFoundException or FileLoadException or TypeLoadException)
        {
            try
            {
                return assembly.GetTypes().Where(t => t.IsPublic || t.IsNestedPublic);
            }
            catch (ReflectionTypeLoadException partialLoad)
            {
                foreach (var reason in partialLoad.LoaderExceptions.Where(e => e is not null).Select(e => e!.Message).Distinct())
                {
                    _log($"{assembly.GetName().Name}: some types couldn't be loaded and were skipped -- {reason}");
                }

                return partialLoad.Types.Where(t => t is not null && (t.IsPublic || t.IsNestedPublic)).Cast<Type>();
            }
        }
    }

    private void RunSafely(object extension, string hookName, Action run)
    {
        try
        {
            run();
        }
        catch (Exception ex)
        {
            _log($"[Extension] {extension.GetType().FullName}.{hookName} threw: {ex.Message}");
        }
    }

    /// <summary>
    /// One isolated, collectible <see cref="AssemblyLoadContext"/> per loaded
    /// extension .dll, so a bad or conflicting extension can never collide
    /// with another one's dependencies -- each gets its own load context
    /// rather than everything piling into the default one.
    /// <see cref="AssemblyDependencyResolver"/> lets an extension ship its
    /// own dependency .dll's alongside itself (in the same folder, or a
    /// <c>.deps.json</c>-described layout) and have them resolve correctly.
    /// </summary>
    private sealed class ExtensionLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public ExtensionLoadContext(string name, AssemblyDependencyResolver resolver)
            : base(name, isCollectible: true)
        {
            _resolver = resolver;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // An assembly the host itself already has loaded -- above all
            // Clearinet.Compatibility, which defines the interfaces an
            // extension implements -- must be shared, never loaded a second
            // time from next to the extension. A second copy would define a
            // second, different ISessionImporter/IAutoTamper type, and
            // Register()'s `is` checks would silently fail to match. Falling
            // through to the default context (return null) shares it. This
            // only matters when an extension's build output (and its
            // .deps.json) sits in the extensions folder with its own copy of
            // Clearinet.Compatibility.dll -- an easy mistake to make when
            // installing a ported extension.
            if (Default.Assemblies.Any(a => AssemblyName.ReferenceMatchesDefinition(assemblyName, a.GetName())))
            {
                return null;
            }

            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is not null ? LoadFromAssemblyPath(path) : null;
        }
    }
}

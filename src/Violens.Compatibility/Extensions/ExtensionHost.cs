using System.Reflection;
using System.Runtime.Loader;
using Violens.ProxyCore;
using Violens.ProxyCore.Extensions;

namespace Violens.Compatibility.Extensions;

/// <summary>
/// Loads compiled extensions: scans folders for <c>.dll</c>s, loads each into
/// its own collectible <see cref="AssemblyLoadContext"/>, applies Fiddler's
/// <c>RequiredVersion</c> gating, and creates one instance of each extension
/// type. Loaded once at startup and unloaded at shutdown; like Fiddler, no
/// hot reload. Not thread-safe.
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
    private readonly List<Violens.CompatShim.IFiddlerExtension> _shimExtensions = new();
    private readonly List<Violens.CompatShim.IAutoTamper> _shimAutoTampers = new();

    /// <param name="extensionFolders">
    /// Folders to scan for <c>.dll</c>s, not recursively, like Fiddler's flat
    /// Scripts folder.
    /// </param>
    /// <param name="log">Where load failures and hook errors are logged.</param>
    public ExtensionHost(IEnumerable<string> extensionFolders, Action<string>? log = null)
    {
        _extensionFolders = extensionFolders.ToList();
        _log = log ?? (_ => { });
    }

    /// <summary>
    /// The user's VioLens extensions folder under Documents. Documents may be
    /// redirected (OneDrive often moves it), so this isn't always
    /// <c>%USERPROFILE%\Documents</c>; <see cref="Load"/> logs the folder it
    /// actually scanned.
    /// </summary>
    public static string DefaultExtensionsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Violens.ProxyCore.ProxyEngine.Identity.ProductName, "Extensions");

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
    /// For each folder given, whether it existed and how many <c>.dll</c>s it
    /// held, so "nothing in the folder" can be told apart from "nothing loaded".
    /// </summary>
    public IReadOnlyList<(string Folder, bool Existed, int DllFilesFound)> ScanResults => _scanResults;

    /// <summary>Problems found while loading. Assemblies without a <c>RequiredVersion</c> are skipped silently, as in Fiddler.</summary>
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

    /// <summary>The loaded <see cref="AutoTampers"/>, for the proxy listener.</summary>
    public IExtensionAutoTamperHost CreateAutoTamperHost() => new LoadedExtensionSet(_autoTampers, _log);

    /// <summary>
    /// Ported Fiddler-shaped extensions that implement
    /// <see cref="Violens.CompatShim.IAutoTamper"/>, in load order.
    /// </summary>
    public IReadOnlyList<Violens.CompatShim.IAutoTamper> ShimAutoTampers => _shimAutoTampers;

    /// <summary>
    /// Every loaded ported Fiddler-shaped extension
    /// (<see cref="Violens.CompatShim.IFiddlerExtension"/>), in load order.
    /// </summary>
    public IReadOnlyList<Violens.CompatShim.IFiddlerExtension> ShimExtensions => _shimExtensions;

    /// <summary>
    /// Runs <see cref="ShimAutoTampers"/> against live traffic with one
    /// session object per request -- see <see cref="ShimAutoTamperSet"/>.
    /// Handed to the proxy listener alongside <see cref="CreateAutoTamperHost"/>.
    /// </summary>
    public IExtensionSessionHost CreateSessionHost() => new ShimAutoTamperSet(_shimAutoTampers, _log);

    /// <summary>
    /// Scans each folder (not subfolders), loads and gates each <c>.dll</c>,
    /// skipping a file name already loaded from an earlier folder, creates the
    /// extensions and calls <see cref="IFiddlerExtension.OnLoad"/>. A missing
    /// folder isn't an error.
    /// </summary>
    public void Load()
    {
        var loadedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in _extensionFolders)
        {
            if (!Directory.Exists(folder))
            {
                // Logged, so a wrong or redirected folder can be told apart from an
                // empty one.
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
            RunSafely(extension, nameof(Violens.CompatShim.IFiddlerExtension.OnLoad), extension.OnLoad);
        }
    }

    /// <summary>
    /// Calls <see cref="IFiddlerExtension.OnBeforeUnload"/> on each extension,
    /// then starts unloading their load contexts (which completes only once
    /// nothing references them).
    /// </summary>
    public void Unload()
    {
        foreach (var extension in _fiddlerExtensions)
        {
            RunSafely(extension, nameof(IFiddlerExtension.OnBeforeUnload), extension.OnBeforeUnload);
        }

        foreach (var extension in _shimExtensions)
        {
            RunSafely(extension, nameof(Violens.CompatShim.IFiddlerExtension.OnBeforeUnload), extension.OnBeforeUnload);
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
                // Not a VioLens extension, but maybe a ported Fiddler one: its
                // RequiredVersion is a Fiddler version, so check it against the API
                // level the shim stands in for.
                var fiddlerRequiredVersion = assembly.GetCustomAttribute<Violens.CompatShim.RequiredVersionAttribute>();
                if (fiddlerRequiredVersion is not null)
                {
                    if (!Version.TryParse(fiddlerRequiredVersion.RequiredVersion, out var fiddlerMinimum))
                    {
                        _loadErrors.Add($"{fileName}: RequiredVersion \"{fiddlerRequiredVersion.RequiredVersion}\" isn't a parseable version -- skipped.");
                        context.Unload();
                        return;
                    }

                    if (Violens.CompatShim.CompatShimHost.FiddlerApiLevel < fiddlerMinimum)
                    {
                        _loadErrors.Add($"{fileName}: needs Fiddler API {fiddlerMinimum}, newer than the {Violens.CompatShim.CompatShimHost.FiddlerApiLevel.ToString(2)}.x this build's compatibility layer covers -- skipped.");
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

            if (ViolensVersion.Current < minimumVersion)
            {
                _loadErrors.Add($"{fileName}: requires VioLens {minimumVersion} or later, this build is {ViolensVersion.Current} -- skipped.");
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

    /// <summary>Creates and registers the extension types in an assembly that passed its version check.</summary>
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
        // One instance may implement several interfaces; each list gets the
        // same instance.
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
        if (instance is Violens.CompatShim.ISessionImporter fiddlerShapedImporter)
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
        if (instance is Violens.CompatShim.IFiddlerExtension shimExtension)
        {
            _shimExtensions.Add(shimExtension);
        }

        if (instance is Violens.CompatShim.IAutoTamper shimAutoTamper)
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
        typeof(Violens.CompatShim.ISessionImporter).IsAssignableFrom(type) ||
        typeof(Violens.CompatShim.IFiddlerExtension).IsAssignableFrom(type) ||
        typeof(ISessionExporter).IsAssignableFrom(type) ||
        typeof(IHandleExecAction).IsAssignableFrom(type);

    /// <summary>
    /// The assembly's public types, skipping any that can't be loaded (an
    /// extension's Avalonia view where Avalonia isn't available, say), so its
    /// other types still register.
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
    /// One collectible load context per extension, so extensions can't clash
    /// over dependencies. Each can ship its own dependencies beside it.
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
            // Assemblies the host already has, Violens.Compatibility above all, are
            // shared rather than loaded again from beside the extension: a second
            // copy would define different interface types, and the extension
            // wouldn't be recognised.
            if (Default.Assemblies.Any(a => AssemblyName.ReferenceMatchesDefinition(assemblyName, a.GetName())))
            {
                return null;
            }

            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is not null ? LoadFromAssemblyPath(path) : null;
        }
    }
}

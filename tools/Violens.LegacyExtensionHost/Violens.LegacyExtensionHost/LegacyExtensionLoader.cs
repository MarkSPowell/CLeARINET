using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Violens.CompatShim;

namespace Violens.LegacyExtensionHost;

/// <summary>
/// The net48 counterpart of <c>ExtensionHost</c>. .NET Framework has no
/// AssemblyLoadContext, so extensions load with <see cref="Assembly.LoadFrom"/>
/// into one AppDomain; the separate process is the isolation.
/// </summary>
public sealed class LegacyExtensionLoader
{
    /// <summary>
    /// The newest Fiddler version the shim has been checked against (Differ and
    /// ContentBlock reference 2.4.2.5). Extensions requiring newer are skipped.
    /// </summary>
    public static readonly Version SupportedFiddlerVersion = new(2, 4, 2, 5);

    private readonly IReadOnlyList<string> _extensionFolders;
    private readonly Action<string> _log;
    private readonly List<string> _loadErrors = new();
    private readonly List<IFiddlerExtension> _fiddlerExtensions = new();
    private readonly List<IAutoTamper> _autoTampers = new();
    private readonly List<IHandleExecAction> _execActionHandlers = new();
    private readonly List<AssemblyMismatch> _assemblyMismatches = new();

    public LegacyExtensionLoader(IEnumerable<string> extensionFolders, Action<string> log = null)
    {
        _extensionFolders = extensionFolders.ToList();
        _log = log ?? (_ => { });
    }

    public IReadOnlyList<string> LoadErrors => _loadErrors;
    public IReadOnlyList<IAutoTamper> AutoTampers => _autoTampers;
    public IReadOnlyList<IHandleExecAction> ExecActionHandlers => _execActionHandlers;

    /// <summary>Every registered extension.</summary>
    public IReadOnlyList<IFiddlerExtension> FiddlerExtensions => _fiddlerExtensions;

    /// <summary>Extensions built against the <c>Fiddler</c> assembly; these are never loaded.</summary>
    public IReadOnlyList<AssemblyMismatch> AssemblyMismatches => _assemblyMismatches;

    public void Load()
    {
        foreach (var folder in _extensionFolders)
        {
            if (!Directory.Exists(folder))
            {
                _log($"Legacy extensions folder not found, nothing to scan there: {folder}");
                continue;
            }

            var dllPaths = Directory.EnumerateFiles(folder, "*.dll", SearchOption.TopDirectoryOnly).ToList();
            _log($"Scanning {folder}: found {dllPaths.Count} .dll file(s).");
            foreach (var dllPath in dllPaths)
            {
                LoadOne(dllPath);
            }
        }

        foreach (var extension in _fiddlerExtensions)
        {
            RunSafely(extension, nameof(IFiddlerExtension.OnLoad), extension.OnLoad);
        }
    }

    public void Unload()
    {
        foreach (var extension in _fiddlerExtensions)
        {
            RunSafely(extension, nameof(IFiddlerExtension.OnBeforeUnload), extension.OnBeforeUnload);
        }
    }

    private void LoadOne(string dllPath)
    {
        var fileName = Path.GetFileName(dllPath);

        // Read the metadata first, so a Fiddler-built extension is reported
        // clearly instead of failing later with a generic bind error.
        if (FiddlerAssemblyReferenceInspector.TryFindFiddlerReference(dllPath, out var expectedName, out var expectedVersion))
        {
            var hostAssemblyName = typeof(FiddlerApplication).Assembly.GetName();
            if (!string.Equals(expectedName, hostAssemblyName.Name, StringComparison.OrdinalIgnoreCase))
            {
                var mismatch = new AssemblyMismatch(fileName, expectedName, expectedVersion, hostAssemblyName.Name, hostAssemblyName.Version);
                _assemblyMismatches.Add(mismatch);
                _loadErrors.Add(mismatch.ToDiagnosticMessage());
                return;
            }
        }

        try
        {
            var assembly = Assembly.LoadFrom(dllPath);

            var requiredVersion = assembly.GetCustomAttribute<RequiredVersionAttribute>();
            if (requiredVersion is null)
            {
                // Silently ignored -- matches real Fiddler's own documented
                // behavior, and the same convention ExtensionHost (the
                // source-compatible, net10.0 sibling of this class) already
                // follows for consistency.
                return;
            }

            if (!Version.TryParse(requiredVersion.MinimumVersion, out var minimumVersion))
            {
                _loadErrors.Add($"{fileName}: RequiredVersion \"{requiredVersion.MinimumVersion}\" isn't a parseable version -- skipped.");
                return;
            }

            if (minimumVersion > SupportedFiddlerVersion)
            {
                _loadErrors.Add($"{fileName}: requires Fiddler {minimumVersion} or later, this shim covers up to {SupportedFiddlerVersion} -- skipped.");
                return;
            }

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
                    instance = Activator.CreateInstance(type);
                }
                catch (Exception ex)
                {
                    _loadErrors.Add($"{fileName}: couldn't construct {type.FullName} -- {ex.Message}");
                    continue;
                }

                Register(instance);
                typesLoaded = true;
            }

            if (!typesLoaded)
            {
                _loadErrors.Add($"{fileName}: declared RequiredVersion but no recognized extension type was found inside it.");
            }
        }
        catch (Exception ex)
        {
            _loadErrors.Add($"{fileName}: failed to load -- {ex.Message}");
        }
    }

    private void Register(object instance)
    {
        if (instance is IFiddlerExtension fiddlerExtension)
        {
            _fiddlerExtensions.Add(fiddlerExtension);
        }

        if (instance is IAutoTamper autoTamper)
        {
            _autoTampers.Add(autoTamper);
        }

        if (instance is IHandleExecAction execActionHandler)
        {
            _execActionHandlers.Add(execActionHandler);
        }
    }

    private static bool ImplementsAnyExtensionInterface(Type type) =>
        typeof(IFiddlerExtension).IsAssignableFrom(type) ||
        typeof(IAutoTamper).IsAssignableFrom(type) ||
        typeof(IHandleExecAction).IsAssignableFrom(type);

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null);
        }
    }

    private void RunSafely(IFiddlerExtension extension, string hookName, Action run)
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
}

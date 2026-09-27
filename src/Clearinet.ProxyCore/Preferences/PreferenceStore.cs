using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace Clearinet.ProxyCore.Preferences;

/// <summary>
/// The JSON-file-backed <see cref="IPreferenceStore"/> -- see the
/// Preferences Design doc for the full reasoning; the short version of
/// each design choice is repeated next to the code that implements it.
///
/// <b>Same behavior on Windows and macOS.</b> Nothing in this class
/// branches on the operating system: the folder comes from
/// <see cref="ClearinetPaths"/>, the file bytes are fixed (UTF-8, no BOM,
/// <c>\n</c>, sorted keys), every number is culture-invariant, and the
/// atomic-replace and cross-process-lock primitives used here
/// (<see cref="File.Move(string, string, bool)"/>,
/// <see cref="FileShare.None"/>) are the ones .NET implements on both.
///
/// <b>Deadlock safety</b> (the Fiddler lesson the Project Plan records):
/// reads take no lock at all (an immutable snapshot, swapped atomically);
/// writes hold <see cref="_writeLock"/> only long enough to compute and
/// swap the new snapshot -- never across I/O, never across a watcher
/// callback, never while taking any other lock; disk I/O runs under its
/// own separate <see cref="_ioLock"/>, off the caller's thread. At most one
/// of this class's locks is ever held at a time, so no lock-ordering cycle
/// can exist.
/// </summary>
public sealed class PreferenceStore : IPreferenceStore, IDisposable
{
    public const int CurrentFormatVersion = 1;
    public const string DefaultFileName = "preferences.json";

    private const string LockFileName = "preferences.lock";
    private const int MaxNameLength = 256;
    private const int MoveRetryCount = 5;

    private static readonly TimeSpan DefaultSaveDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(2);
    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

    private readonly string? _filePath;
    private readonly string? _lockFilePath;
    private readonly TimeSpan _saveDelay;
    private readonly Action<string> _log;
    private readonly Timer? _saveTimer;

    private readonly object _writeLock = new();
    private readonly object _ioLock = new();

    private ImmutableDictionary<string, string> _values;

    /// <summary>
    /// Names this process changed since its last successful save, with
    /// <see langword="null"/> meaning "removed". Only ever touched under
    /// <see cref="_writeLock"/>. A save applies exactly these on top of a
    /// fresh read of the file -- see "More than one CLeARINET running" in
    /// the design doc -- so another process's changes to other names
    /// survive.
    /// </summary>
    private Dictionary<string, string?> _pendingChanges = new(NameComparer);

    private ImmutableList<PrefWatcher> _watchers = ImmutableList<PrefWatcher>.Empty;
    private volatile bool _isReadOnly;
    private volatile bool _disposed;

    /// <param name="filePath">Where the preferences live. Most callers want <see cref="CreateDefault"/> instead.</param>
    /// <param name="saveDelay">
    /// How long after a change the background save runs (changes arriving
    /// in that window share one write). Defaults to 250 ms. Tests pass
    /// something long and call <see cref="Flush"/> themselves, so every
    /// write is deterministic.
    /// </param>
    /// <param name="log">Where load/save problems are reported. They're never thrown: settings are a convenience, not a reason for the app not to start.</param>
    public PreferenceStore(string filePath, TimeSpan? saveDelay = null, Action<string>? log = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        _filePath = Path.GetFullPath(filePath);
        _lockFilePath = Path.Combine(Path.GetDirectoryName(_filePath)!, LockFileName);
        _saveDelay = saveDelay ?? DefaultSaveDelay;
        _log = log ?? (_ => { });

        var (values, readOnly) = LoadAtStartup();
        _values = values;
        _isReadOnly = readOnly;

        _saveTimer = new Timer(_ => OnSaveTimer(), null, Timeout.Infinite, Timeout.Infinite);

        // Belt and braces alongside the host's own Dispose() on shutdown:
        // ProcessExit runs on a normal exit on both Windows and macOS, so a
        // host that forgets (or never reaches) Dispose still gets its last
        // changes saved. Unhooked again in Dispose().
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    private PreferenceStore()
    {
        _saveDelay = Timeout.InfiniteTimeSpan;
        _log = _ => { };
        _values = ImmutableDictionary.Create<string, string>(NameComparer);
    }

    /// <summary><c>preferences.json</c> under <see cref="ClearinetPaths.AppDataFolder"/>.</summary>
    public static PreferenceStore CreateDefault(Action<string>? log = null) =>
        new(Path.Combine(ClearinetPaths.AppDataFolder, DefaultFileName), log: log);

    /// <summary>A store that never touches disk -- for tests and for hosts that don't want persistence.</summary>
    public static PreferenceStore CreateInMemory() => new();

    /// <summary>The backing file, or <see langword="null"/> for an in-memory store.</summary>
    public string? FilePath => _filePath;

    /// <summary>
    /// True when the file on disk was written by a newer build
    /// (<c>formatVersion</c> above <see cref="CurrentFormatVersion"/>) or
    /// couldn't be read at all. Values still load and can still be changed
    /// in memory for this run, but the file is never overwritten, since
    /// this build can't know what it would lose.
    /// </summary>
    public bool IsReadOnly => _isReadOnly;

    public string? this[string prefName] =>
        IsValidName(prefName) && Volatile.Read(ref _values).TryGetValue(prefName, out var value) ? value : null;

    public string GetStringPref(string prefName, string defaultValue) => this[prefName] ?? defaultValue;

    public bool GetBoolPref(string prefName, bool defaultValue) =>
        bool.TryParse(this[prefName], out var value) ? value : defaultValue;

    public int GetInt32Pref(string prefName, int defaultValue) =>
        int.TryParse(this[prefName], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : defaultValue;

    public IReadOnlyList<string> GetListPref(string prefName, IReadOnlyList<string> defaultValue) =>
        this[prefName] is { } raw ? ParseList(raw) : defaultValue;

    public void SetStringPref(string prefName, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ApplyChanges([new(prefName, value)]);
    }

    /// <summary>Written as <c>True</c>/<c>False</c> (what <see cref="bool.ToString()"/> produces), the same way Fiddler stores them.</summary>
    public void SetBoolPref(string prefName, bool value) =>
        ApplyChanges([new(prefName, value ? bool.TrueString : bool.FalseString)]);

    public void SetInt32Pref(string prefName, int value) =>
        ApplyChanges([new(prefName, value.ToString(CultureInfo.InvariantCulture))]);

    public void SetListPref(string prefName, IEnumerable<string> values) =>
        ApplyChanges([new(prefName, FormatList(values))]);

    public void SetPrefs(IEnumerable<KeyValuePair<string, string>> prefs)
    {
        ArgumentNullException.ThrowIfNull(prefs);

        var changes = new List<KeyValuePair<string, string?>>();
        foreach (var pref in prefs)
        {
            ArgumentNullException.ThrowIfNull(pref.Value, nameof(prefs));
            changes.Add(new(pref.Key, pref.Value));
        }

        ApplyChanges(changes);
    }

    public void RemovePref(string prefName) => ApplyChanges([new(prefName, null)]);

    public PrefWatcher AddWatcher(string prefixFilter, EventHandler<PrefChangeEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(prefixFilter);
        ArgumentNullException.ThrowIfNull(handler);

        var watcher = new PrefWatcher(prefixFilter, handler);
        ImmutableInterlocked.Update(ref _watchers, list => list.Add(watcher));
        return watcher;
    }

    public void RemoveWatcher(PrefWatcher watcher)
    {
        ArgumentNullException.ThrowIfNull(watcher);
        ImmutableInterlocked.Update(ref _watchers, list => list.Remove(watcher));
    }

    /// <summary>
    /// Writes any unsaved changes now, synchronously. Called on shutdown
    /// (and by <see cref="Dispose"/>); otherwise the background save does
    /// this on its own shortly after each change. Never throws for I/O
    /// problems -- they're logged, and the unsaved changes are kept so the
    /// next save retries them.
    /// </summary>
    public void Flush()
    {
        if (_filePath is null)
        {
            return;
        }

        lock (_ioLock)
        {
            Dictionary<string, string?> toSave;
            lock (_writeLock)
            {
                if (_pendingChanges.Count == 0 || _isReadOnly)
                {
                    return;
                }

                toSave = _pendingChanges;
                _pendingChanges = new Dictionary<string, string?>(NameComparer);
            }

            bool saved;
            try
            {
                saved = SaveMerged(toSave);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log($"Couldn't save preferences to {_filePath}: {ex.Message}");
                saved = false;
            }

            if (!saved)
            {
                lock (_writeLock)
                {
                    // A newer change to the same name, made while this save
                    // was running, wins over the one that just failed.
                    foreach (var change in toSave)
                    {
                        _pendingChanges.TryAdd(change.Key, change.Value);
                    }
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_filePath is not null)
        {
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        }

        _saveTimer?.Dispose();
        Flush();
    }

    /// <summary>
    /// Non-empty, at most 256 characters, no whitespace or control
    /// characters, no leading or trailing dot.
    /// </summary>
    public static bool IsValidName(string? prefName) =>
        !string.IsNullOrEmpty(prefName)
        && prefName.Length <= MaxNameLength
        && prefName[0] != '.'
        && prefName[^1] != '.'
        && !prefName.Any(c => char.IsWhiteSpace(c) || char.IsControl(c));

    /// <summary>
    /// Fiddler's own <c>fiddlerscript.ephemeral.*</c> convention, applied to
    /// every name: kept in memory for this process, never written to disk.
    /// </summary>
    public static bool IsEphemeral(string prefName) =>
        prefName.Contains("ephemeral", StringComparison.OrdinalIgnoreCase);

    /// <summary>Split on <c>;</c>, trim, drop empties, drop case-insensitive duplicates (first kept).</summary>
    public static IReadOnlyList<string> ParseList(string raw)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var part in raw.Split(';'))
        {
            var entry = part.Trim();
            if (entry.Length > 0 && seen.Add(entry))
            {
                result.Add(entry);
            }
        }

        return result;
    }

    private static string FormatList(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var entries = new List<string>();
        foreach (var value in values)
        {
            ArgumentNullException.ThrowIfNull(value, nameof(values));
            if (value.Contains(';'))
            {
                throw new ArgumentException($"List entries can't contain ';' (got \"{value}\").", nameof(values));
            }

            entries.Add(value);
        }

        return string.Join(';', ParseList(string.Join(';', entries)));
    }

    private void ApplyChanges(IReadOnlyList<KeyValuePair<string, string?>> changes)
    {
        foreach (var change in changes)
        {
            if (!IsValidName(change.Key))
            {
                throw new ArgumentException($"\"{change.Key}\" isn't a valid preference name.", nameof(changes));
            }
        }

        var raised = new List<PrefChangeEventArgs>();

        lock (_writeLock)
        {
            var builder = _values.ToBuilder();
            var anyPersisted = false;

            foreach (var (requestedName, newValue) in changes)
            {
                // Keep whichever casing the name was first stored with.
                var name = builder.TryGetKey(requestedName, out var existingName) ? existingName : requestedName;
                var hadValue = builder.TryGetValue(name, out var oldValue);

                if (newValue is null)
                {
                    if (!hadValue)
                    {
                        continue;
                    }

                    builder.Remove(name);
                }
                else
                {
                    if (hadValue && string.Equals(oldValue, newValue, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    builder.Remove(name);
                    builder.Add(name, newValue);
                }

                if (!IsEphemeral(name))
                {
                    _pendingChanges[name] = newValue;
                    anyPersisted = true;
                }

                raised.Add(new PrefChangeEventArgs(name, hadValue ? oldValue : null, newValue));
            }

            if (raised.Count == 0)
            {
                return;
            }

            Volatile.Write(ref _values, builder.ToImmutable());

            if (anyPersisted && !_isReadOnly && !_disposed)
            {
                _saveTimer?.Change(_saveDelay, Timeout.InfiniteTimeSpan);
            }
        }

        // Outside the lock, on purpose -- see this class's own remarks.
        NotifyWatchers(raised);
    }

    private void NotifyWatchers(List<PrefChangeEventArgs> changes)
    {
        var watchers = Volatile.Read(ref _watchers);
        if (watchers.IsEmpty)
        {
            return;
        }

        foreach (var change in changes)
        {
            foreach (var watcher in watchers)
            {
                if (!watcher.Matches(change.PrefName))
                {
                    continue;
                }

                try
                {
                    watcher.Handler(this, change);
                }
                catch (Exception ex)
                {
                    _log($"A preference watcher for \"{watcher.PrefixFilter}\" threw while handling \"{change.PrefName}\": {ex.Message}");
                }
            }
        }
    }

    private void OnProcessExit(object? sender, EventArgs e) => OnSaveTimer();

    private void OnSaveTimer()
    {
        // A timer callback's unhandled exception would take the whole
        // process down; Flush() already catches the I/O failures it
        // expects, so anything reaching here is a bug worth logging, not a
        // reason to crash a debugging tool.
        try
        {
            Flush();
        }
        catch (Exception ex)
        {
            _log($"Unexpected error saving preferences: {ex}");
        }
    }

    private (ImmutableDictionary<string, string> Values, bool ReadOnly) LoadAtStartup()
    {
        var empty = ImmutableDictionary.Create<string, string>(NameComparer);

        if (!File.Exists(_filePath))
        {
            return (empty, false);
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(_filePath!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Exists but can't be read -- don't risk overwriting something
            // we couldn't even look at.
            _log($"Couldn't read preferences from {_filePath} ({ex.Message}); using defaults, and not saving changes this run.");
            return (empty, true);
        }

        switch (TryParse(bytes, out var formatVersion, out var values))
        {
            case ParseResult.Ok when formatVersion > CurrentFormatVersion:
                _log($"{_filePath} was written by a newer CLeARINET (format {formatVersion}); loading it read-only.");
                return (values!, true);

            case ParseResult.Ok:
                return (values!, false);

            default:
                MoveCorruptFileAside();
                return (empty, false);
        }
    }

    private void MoveCorruptFileAside()
    {
        var asidePath = $"{_filePath}.corrupt-{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}";
        try
        {
            File.Move(_filePath!, asidePath, overwrite: true);
            _log($"{_filePath} couldn't be parsed; moved it to {asidePath} and started from defaults.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log($"{_filePath} couldn't be parsed, and couldn't be moved aside either ({ex.Message}); starting from defaults.");
        }
    }

    private enum ParseResult
    {
        Ok,
        Corrupt,
    }

    /// <summary>
    /// Lenient about values -- a hand-edited <c>8888</c> or <c>true</c>
    /// without quotes is accepted as its text -- but strict about shape:
    /// anything that isn't <c>{ "formatVersion": n, "preferences": { ... } }</c>
    /// is corrupt.
    /// </summary>
    private static ParseResult TryParse(byte[] bytes, out int formatVersion, out ImmutableDictionary<string, string>? values)
    {
        formatVersion = 0;
        values = null;

        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });

            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("formatVersion", out var versionElement)
                || versionElement.ValueKind != JsonValueKind.Number
                || !versionElement.TryGetInt32(out formatVersion)
                || !root.TryGetProperty("preferences", out var prefsElement)
                || prefsElement.ValueKind != JsonValueKind.Object)
            {
                return ParseResult.Corrupt;
            }

            var builder = ImmutableDictionary.CreateBuilder<string, string>(NameComparer);
            foreach (var property in prefsElement.EnumerateObject())
            {
                if (!IsValidName(property.Name))
                {
                    continue;
                }

                var text = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => property.Value.GetRawText(),
                    _ => null,
                };

                if (text is not null)
                {
                    builder[property.Name] = text;
                }
            }

            values = builder.ToImmutable();
            return ParseResult.Ok;
        }
        catch (JsonException)
        {
            return ParseResult.Corrupt;
        }
    }

    /// <summary>
    /// Under the cross-process lock: re-read the file, apply only this
    /// process's own changes on top, write it back atomically. Returns
    /// false (nothing written) if the lock couldn't be taken in time or the
    /// final replace kept failing -- the caller keeps the changes for the
    /// next attempt.
    /// </summary>
    private bool SaveMerged(Dictionary<string, string?> changes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

        using var crossProcessLock = TryAcquireCrossProcessLock();
        if (crossProcessLock is null)
        {
            _log($"Another CLeARINET kept {_lockFilePath} locked for over {LockTimeout.TotalSeconds:0} s; will retry the save later.");
            return false;
        }

        var merged = ReadCurrentFileForMerge();
        if (merged is null)
        {
            // The file changed under us into something written by a newer
            // build: stop writing for the rest of this run.
            _isReadOnly = true;
            return true;
        }

        foreach (var (requestedName, value) in changes)
        {
            var name = merged.Keys.FirstOrDefault(k => NameComparer.Equals(k, requestedName)) ?? requestedName;
            merged.Remove(name);
            if (value is not null)
            {
                merged[name] = value;
            }
        }

        return WriteAtomically(merged);
    }

    /// <summary>
    /// What's on disk right now, as a mutable copy to merge into. Missing
    /// file: empty. Corrupt file: moved aside, then this process's own
    /// current persisted values are the best base left. Newer format:
    /// <see langword="null"/> (don't write).
    /// </summary>
    private Dictionary<string, string>? ReadCurrentFileForMerge()
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, string>(NameComparer);
        }

        var bytes = File.ReadAllBytes(_filePath!);
        switch (TryParse(bytes, out var formatVersion, out var values))
        {
            case ParseResult.Ok when formatVersion > CurrentFormatVersion:
                _log($"{_filePath} was replaced by a newer CLeARINET (format {formatVersion}); no longer saving changes this run.");
                return null;

            case ParseResult.Ok:
                return new Dictionary<string, string>(values!, NameComparer);

            default:
                MoveCorruptFileAside();
                return Volatile.Read(ref _values)
                    .Where(pair => !IsEphemeral(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, NameComparer);
        }
    }

    private FileStream? TryAcquireCrossProcessLock()
    {
        // FileShare.None is a real sharing-mode lock on Windows; on macOS
        // .NET enforces it between .NET processes with an advisory flock --
        // either way, two CLeARINET processes can't both hold it.
        var deadline = DateTime.UtcNow + LockTimeout;
        while (true)
        {
            try
            {
                return new FileStream(_lockFilePath!, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(25);
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    private bool WriteAtomically(Dictionary<string, string> values)
    {
        var directory = Path.GetDirectoryName(_filePath)!;
        var tempPath = Path.Combine(directory, $"{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                WriteJson(stream, values);
                stream.Flush(flushToDisk: true);
            }

            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    // MoveFileEx(MOVEFILE_REPLACE_EXISTING) on Windows,
                    // rename(2) on macOS: a reader sees the old file or the
                    // new one, never half of each.
                    File.Move(tempPath, _filePath!, overwrite: true);
                    return true;
                }
                catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < MoveRetryCount)
                {
                    // Most often an antivirus scanner or indexer briefly
                    // holding the target open on Windows.
                    Thread.Sleep(20 * (attempt + 1));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _log($"Couldn't replace {_filePath} after {MoveRetryCount + 1} attempts ({ex.Message}); will retry the save later.");
                    return false;
                }
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Leaving a stray .tmp behind is harmless.
            }
        }
    }

    /// <summary>
    /// The exact same bytes on every platform: UTF-8 without a BOM
    /// (<see cref="Utf8JsonWriter"/> never writes one), <c>\n</c> line
    /// endings regardless of <see cref="Environment.NewLine"/>, keys sorted
    /// so two machines' files diff cleanly.
    /// </summary>
    private static void WriteJson(Stream stream, Dictionary<string, string> values)
    {
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n" });

        writer.WriteStartObject();
        writer.WriteNumber("formatVersion", CurrentFormatVersion);
        writer.WriteStartObject("preferences");
        foreach (var pair in values
                     .OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(p => p.Key, StringComparer.Ordinal))
        {
            writer.WriteString(pair.Key, pair.Value);
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.Flush();

        stream.WriteByte((byte)'\n');
    }
}

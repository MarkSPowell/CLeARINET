using System.Text.Json;
using System.Text.Json.Serialization;

namespace Violens.ProxyCore.AutoResponder;

/// <summary>
/// Saves AutoResponder rules between runs in <c>autoresponder-rules.json</c>.
/// The on/off switch isn't saved; the AutoResponder always starts off.
/// </summary>
internal static class AutoResponderRuleFile
{
    public const string FileName = "autoresponder-rules.json";

    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary><c>autoresponder-rules.json</c> in the host's data folder.</summary>
    public static string DefaultPath => Path.Combine(ProxyEngine.Identity.ResolveDataFolder(), FileName);

    /// <summary>
    /// Reads saved rules (none if there's no file). An unreadable file is
    /// renamed to <c>.unreadable</c> so the next save doesn't overwrite it, and
    /// <paramref name="error"/> says why.
    /// </summary>
    public static IReadOnlyList<AutoResponderRule> Load(string path, out string? error)
    {
        error = null;
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            var saved = JsonSerializer.Deserialize<SavedRules>(File.ReadAllText(path), JsonOptions)
                ?? throw new JsonException("the file is empty");
            if (saved.Version > CurrentVersion)
            {
                throw new JsonException($"it was saved by a newer version of {ProxyEngine.Identity.ProductName} (format {saved.Version})");
            }

            return (saved.Rules ?? [])
                .Select(rule => new AutoResponderRule
                {
                    IsEnabled = rule.Enabled,
                    MatchPattern = rule.Match ?? string.Empty,
                    Action = rule.Action ?? string.Empty,
                })
                .ToList();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            string? setAside = path + ".unreadable";
            try
            {
                File.Copy(path, setAside, overwrite: true);
            }
            catch (Exception copyError) when (copyError is IOException or UnauthorizedAccessException)
            {
                setAside = null;
            }

            error = $"Couldn't read the saved AutoResponder rules ({ex.Message})" +
                    (setAside is null ? "." : $"; the file was kept as {setAside}.");
            return [];
        }
    }

    /// <summary>Writes the rules, replacing the file in one step.</summary>
    public static void Save(string path, IEnumerable<AutoResponderRule> rules)
    {
        var saved = new SavedRules
        {
            Version = CurrentVersion,
            Rules = rules.Select(rule => new SavedRule
            {
                Enabled = rule.IsEnabled,
                Match = rule.MatchPattern,
                Action = rule.Action,
            }).ToList(),
        };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(saved, JsonOptions));
        File.Move(temporary, path, overwrite: true);
    }

    internal sealed class SavedRules
    {
        public int Version { get; set; }

        public List<SavedRule>? Rules { get; set; }
    }

    internal sealed class SavedRule
    {
        public bool Enabled { get; set; }

        public string? Match { get; set; }

        public string? Action { get; set; }
    }
}

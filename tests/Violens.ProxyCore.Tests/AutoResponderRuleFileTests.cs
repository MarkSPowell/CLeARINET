using Violens.ProxyCore.AutoResponder;
using Xunit;

namespace Violens.ProxyCore.Tests;

/// <summary>Saving AutoResponder rules between runs (<see cref="AutoResponderRuleFile"/>).</summary>
public sealed class AutoResponderRuleFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "violens-rules-tests", Guid.NewGuid().ToString("N"));

    private string RulesPath => Path.Combine(_folder, AutoResponderRuleFile.FileName);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    [Fact]
    public void KeepsEachRuleInOrderWithItsOwnCheckbox()
    {
        AutoResponderRuleFile.Save(RulesPath,
        [
            new AutoResponderRule { IsEnabled = true, MatchPattern = "regex:^https://api\\.example\\.com/.*\"quoted\"", Action = "*delay:500" },
            new AutoResponderRule { IsEnabled = false, MatchPattern = "EXACT:https://example.com/", Action = "C:\\mocks\\home.html" },
        ]);

        var loaded = AutoResponderRuleFile.Load(RulesPath, out var error);

        Assert.Null(error);
        Assert.Equal(2, loaded.Count);
        Assert.True(loaded[0].IsEnabled);
        Assert.Equal("regex:^https://api\\.example\\.com/.*\"quoted\"", loaded[0].MatchPattern);
        Assert.Equal("*delay:500", loaded[0].Action);
        Assert.False(loaded[1].IsEnabled);
        Assert.Equal("C:\\mocks\\home.html", loaded[1].Action);
        Assert.False(File.Exists(RulesPath + ".tmp"));
    }

    [Fact]
    public void NoFileMeansNoRules()
    {
        Assert.Empty(AutoResponderRuleFile.Load(RulesPath, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void AnUnreadableFileIsSetAsideNotLost()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(RulesPath, "{ not json");

        var loaded = AutoResponderRuleFile.Load(RulesPath, out var error);

        Assert.Empty(loaded);
        Assert.Contains("Couldn't read the saved AutoResponder rules", error);
        Assert.Equal("{ not json", File.ReadAllText(RulesPath + ".unreadable"));
    }

    [Fact]
    public void AFileFromANewerVersionIsntMisread()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(RulesPath, "{ \"version\": 99, \"rules\": [] }");

        Assert.Empty(AutoResponderRuleFile.Load(RulesPath, out var error));
        Assert.Contains("newer version", error);
    }

    [Fact]
    public void SavingAnEmptyListClearsTheRules()
    {
        AutoResponderRuleFile.Save(RulesPath, [new AutoResponderRule { MatchPattern = "a", Action = "*drop" }]);
        AutoResponderRuleFile.Save(RulesPath, []);

        Assert.Empty(AutoResponderRuleFile.Load(RulesPath, out _));
    }
}

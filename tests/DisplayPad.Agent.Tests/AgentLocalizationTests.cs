using System.Text.RegularExpressions;
using DisplayPad.Agent;
using Xunit;

namespace DisplayPad.Agent.Tests;

public sealed class AgentLocalizationTests
{
    [Fact]
    public void EveryUsedAgentTextKeyHasGermanAndEnglishTextWithMatchingPlaceholders()
    {
        var agentDirectory = Path.Combine(GetRepositoryRoot(), "src", "DisplayPad.Agent");
        var sourceTexts = Directory.EnumerateFiles(agentDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path) != "AgentText.cs")
            .Select(File.ReadAllText)
            .ToArray();
        var usedKeys = sourceTexts
            .SelectMany(source =>
                Regex.Matches(source, "AgentText\\.Get\\([^,]+,\\s*\\\"([^\\\"]+)\\\"\\)")
                    .Concat(Regex.Matches(source, "new AgentConfigException\\(\\\"([^\\\"]+)\\\""))
                    .Select(match => match.Groups[1].Value))
            .Distinct()
            .ToArray();

        foreach (var key in AgentText.Keys)
        {
            var german = AgentText.Get("de", key);
            var english = AgentText.Get("en", key);
            Assert.False(string.IsNullOrWhiteSpace(german));
            Assert.False(string.IsNullOrWhiteSpace(english));
            Assert.Equal(Placeholders(german), Placeholders(english));
        }
        Assert.All(usedKeys, key => Assert.Contains(key, AgentText.Keys));
    }

    [Fact]
    public void AgentVisibleSinksUseAgentTextInsteadOfGermanLiterals()
    {
        var agentDirectory = Path.Combine(GetRepositoryRoot(), "src", "DisplayPad.Agent");
        var program = File.ReadAllText(Path.Combine(agentDirectory, "Program.cs"));
        var config = File.ReadAllText(Path.Combine(agentDirectory, "AgentConfig.cs"));
        var certificate = File.ReadAllText(Path.Combine(agentDirectory, "AgentCertificate.cs"));

        Assert.DoesNotContain("MessageBox.Show(ex.ToString()", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Adresse:", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Agent-Konfiguration ist", config, StringComparison.Ordinal);
        Assert.DoesNotContain("Zertifikat ist vorhanden", certificate, StringComparison.Ordinal);
    }

    private static string[] Placeholders(string value) =>
        Regex.Matches(value, @"\{\d+\}").Select(match => match.Value).Distinct().Order().ToArray();

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var index = 0; index < 5; index++) directory = directory.Parent!;
        return directory.FullName;
    }
}

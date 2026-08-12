using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace DisplayPad.Host.Tests;

public sealed class LocalizationTests
{
    [Fact]
    public void GermanAndEnglishHaveSameKeysAndPlaceholders()
    {
        var root = GetRepositoryRoot();
        var german = Read(Path.Combine(root, "src", "DisplayPad.Host", "Localization", "Strings.de.xaml"));
        var english = Read(Path.Combine(root, "src", "DisplayPad.Host", "Localization", "Strings.en.xaml"));

        Assert.Equal(german.Keys.Order(), english.Keys.Order());
        foreach (var key in german.Keys)
            Assert.Equal(Placeholders(german[key]), Placeholders(english[key]));
    }

    [Fact]
    public void WpfViewsContainNoHardcodedAlphabeticVisibleText()
    {
        var host = Path.Combine(GetRepositoryRoot(), "src", "DisplayPad.Host");
        var pattern = new Regex("\\b(?:Content|Text|ToolTip|Header|Title)=\"(?!\\{)([^\"]*[A-Za-zÄÖÜäöü][^\"]*)\"");
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "Deutsch", "English", "ItzMayzTV"
        };
        var violations = Directory.EnumerateFiles(host, "*.xaml", SearchOption.TopDirectoryOnly)
            .SelectMany(file => pattern.Matches(File.ReadAllText(file)).Select(match => $"{Path.GetFileName(file)}: {match.Groups[1].Value}"))
            .Where(value => !allowed.Any(item => value.EndsWith(item, StringComparison.Ordinal)))
            .ToArray();
        Assert.Empty(violations);
    }

    private static Dictionary<string, string> Read(string path)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return XDocument.Load(path).Root!.Elements().ToDictionary(element => element.Attribute(x + "Key")!.Value, element => element.Value);
    }

    private static string[] Placeholders(string value) =>
        Regex.Matches(value, "\\{\\d+\\}").Select(match => match.Value).Distinct().Order().ToArray();

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var index = 0; index < 5; index++) directory = directory.Parent!;
        return directory.FullName;
    }
}

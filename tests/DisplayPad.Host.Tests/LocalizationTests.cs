using System.Text.RegularExpressions;
using System.Xml.Linq;
using DisplayPad.Host.Services;
using DisplayPad.Shared.Models;
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
    public void DarkAndLightThemesHaveSameKeys()
    {
        var themes = Path.Combine(GetRepositoryRoot(), "src", "DisplayPad.Host", "Themes");
        var dark = Read(Path.Combine(themes, "Dark.xaml"));
        var light = Read(Path.Combine(themes, "Light.xaml"));

        Assert.Equal(dark.Keys.Order(), light.Keys.Order());
    }

    [Fact]
    public void WpfViewsContainNoHardcodedAlphabeticVisibleText()
    {
        var host = Path.Combine(GetRepositoryRoot(), "src", "DisplayPad.Host");
        var pattern = new Regex("\\b(?:Content|Text|ToolTip|Header|Title)=\"(?!\\{)([^\"]*[A-Za-zÄÖÜäöü][^\"]*)\"");
        var violations = Directory.EnumerateFiles(host, "*.xaml", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .SelectMany(file => pattern.Matches(File.ReadAllText(file)).Select(match => $"{Path.GetFileName(file)}: {match.Groups[1].Value}"))
            .ToArray();
        Assert.Empty(violations);
    }

    [Fact]
    public void EveryOperationErrorCodeHasGermanAndEnglishResource()
    {
        var root = GetRepositoryRoot();
        var german = Read(Path.Combine(root, "src", "DisplayPad.Host", "Localization", "Strings.de.xaml"));
        var english = Read(Path.Combine(root, "src", "DisplayPad.Host", "Localization", "Strings.en.xaml"));

        foreach (var code in Enum.GetValues<OperationErrorCode>())
        {
            var key = $"Error{code}";
            Assert.True(german.ContainsKey(key), $"German resource is missing {key}.");
            Assert.True(english.ContainsKey(key), $"English resource is missing {key}.");
        }
    }

    [Fact]
    public void EveryLiteralHostResourceReferenceExists()
    {
        var root = GetRepositoryRoot();
        var host = Path.Combine(root, "src", "DisplayPad.Host");
        var resources = Read(Path.Combine(host, "Localization", "Strings.de.xaml"));
        foreach (var resource in Read(Path.Combine(host, "Themes", "Dark.xaml")))
            resources.Add(resource.Key, resource.Value);
        var csharpKeys = Directory.EnumerateFiles(host, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), "Loc\\.Get\\(\\\"([^\\\"]+)\\\"\\)")
                .Select(match => match.Groups[1].Value));
        var xamlKeys = Directory.EnumerateFiles(host, "*.xaml", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), @"\{(?:Dynamic|Static)Resource\s+([^{}\s]+)")
                .Select(match => match.Groups[1].Value));

        Assert.All(csharpKeys.Concat(xamlKeys).Distinct(), key =>
            Assert.True(resources.ContainsKey(key), $"Host resource is missing {key}."));
    }

    [Fact]
    public void UserVisibleCSharpSinksDoNotUseDirectTextLiterals()
    {
        var host = Path.Combine(GetRepositoryRoot(), "src", "DisplayPad.Host");
        var all = string.Join(Environment.NewLine,
            Directory.EnumerateFiles(host, "*.cs", SearchOption.AllDirectories)
                .Where(file => !IsBuildOutput(file))
                .Select(File.ReadAllText));

        Assert.DoesNotMatch(new Regex("StatusMessage\\s*=\\s*\\$?\\\"", RegexOptions.Multiline), all);
        Assert.DoesNotMatch(new Regex("Filter\\s*=\\s*\\$?\\\"", RegexOptions.Multiline), all);
        Assert.DoesNotContain("MessageBox.Show(ex.ToString()", all, StringComparison.Ordinal);
        Assert.DoesNotContain("BaseCampStatus.Description", all, StringComparison.Ordinal);
        Assert.DoesNotContain("new PageConfig { Name = \"Ordner\"", all, StringComparison.Ordinal);
        Assert.DoesNotContain("Name = $\"Profil ", all, StringComparison.Ordinal);
        Assert.DoesNotContain("Name = $\"Seite ", all, StringComparison.Ordinal);
    }

    [Fact]
    public void StructuredRemoteErrorIsFormattedInHostLanguage()
    {
        var root = GetRepositoryRoot();
        var german = Read(Path.Combine(root, "src", "DisplayPad.Host", "Localization", "Strings.de.xaml"));
        var english = Read(Path.Combine(root, "src", "DisplayPad.Host", "Localization", "Strings.en.xaml"));
        var result = OperationResult.Fail(OperationErrorCode.RemoteHttpError,
            technicalDetail: "response-body", parameters: ["503"]);

        var germanText = OperationText.Format(result, key => german[key]);
        var englishText = OperationText.Format(result, key => english[key]);

        Assert.Contains("503", germanText);
        Assert.Contains("503", englishText);
        Assert.Contains("Technisches Detail", germanText);
        Assert.Contains("Technical detail", englishText);
        Assert.NotEqual(germanText, englishText);
    }

    [Fact]
    public void ActionServicesExposeStructuredResults()
    {
        Assert.Equal(typeof(OperationResult), typeof(LocalActionExecutor).GetMethod("Execute")!.ReturnType);
        Assert.Equal(typeof(OperationResult), typeof(NvidiaOverlayService).GetMethod("Execute")!.ReturnType);
        Assert.Equal(typeof(OperationResult), typeof(IObsService).GetMethod("Execute")!.ReturnType);
        Assert.Equal(typeof(OperationResult), typeof(BaseCampManager).GetMethod("Disable")!.ReturnType);
        Assert.Equal(typeof(OperationResult), typeof(BaseCampManager).GetMethod("Enable")!.ReturnType);
    }

    [Fact]
    public void DynamicNameTemplatesProduceLanguageSpecificNames()
    {
        var root = GetRepositoryRoot();
        var german = Read(Path.Combine(root, "src", "DisplayPad.Host", "Localization", "Strings.de.xaml"));
        var english = Read(Path.Combine(root, "src", "DisplayPad.Host", "Localization", "Strings.en.xaml"));

        Assert.Equal("Profil 2", LocalizedNames.Profile(2, key => german[key]));
        Assert.Equal("Profile 2", LocalizedNames.Profile(2, key => english[key]));
        Assert.Equal("Seite 3", LocalizedNames.Page(3, key => german[key]));
        Assert.Equal("Page 3", LocalizedNames.Page(3, key => english[key]));
        Assert.Equal("Ordner 4", LocalizedNames.Folder(4, key => german[key]));
        Assert.Equal("Folder 4", LocalizedNames.Folder(4, key => english[key]));
        Assert.Equal("Benutzerdefiniert (Kopie)", LocalizedNames.Copy("Benutzerdefiniert", key => german[key]));
        Assert.Equal("Custom (copy)", LocalizedNames.Copy("Custom", key => english[key]));
    }

    private static Dictionary<string, string> Read(string path)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return XDocument.Load(path).Root!.Elements().ToDictionary(element => element.Attribute(x + "Key")!.Value, element => element.Value);
    }

    private static string[] Placeholders(string value) =>
        Regex.Matches(value, "\\{\\d+\\}").Select(match => match.Value).Distinct().Order().ToArray();

    private static bool IsBuildOutput(string path) =>
        path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" or "publish");

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var index = 0; index < 5; index++) directory = directory.Parent!;
        return directory.FullName;
    }
}

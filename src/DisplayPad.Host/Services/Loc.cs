using System.Windows;

namespace DisplayPad.Host.Services;

public static class Loc
{
    private static string _language = "de";
    public static event Action? LanguageChanged;

    public static string Language => _language;

    public static string Get(string key) =>
        Application.Current.TryFindResource(key) is string s ? s : $"[{key}]";

    public static void Switch(string lang)
    {
        var dicts = Application.Current.Resources.MergedDictionaries;
        var old = dicts.FirstOrDefault(d =>
            d.Source?.OriginalString.Contains("/Localization/Strings.") == true);
        if (old != null) dicts.Remove(old);
        dicts.Add(new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Localization/Strings.{lang}.xaml")
        });
        _language = lang;
        LanguageChanged?.Invoke();
    }

    public static string GetForLanguage(string lang, string key)
    {
        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Localization/Strings.{lang}.xaml")
        };
        return dictionary[key] as string ?? $"[{key}]";
    }
}

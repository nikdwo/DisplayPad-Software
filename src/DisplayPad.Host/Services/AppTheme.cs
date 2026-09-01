using System.Windows;

namespace DisplayPad.Host.Services;

public static class AppTheme
{
    private static string _current = "dark";

    public static event Action? Changed;
    public static string Current => _current;
    public static bool IsDark => _current == "dark";

    public static void Switch(string theme)
    {
        theme = theme is "dark" or "light" ? theme : "dark";
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var old = dictionaries.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.Contains("/Themes/") == true);
        if (old is not null) dictionaries.Remove(old);
        dictionaries.Insert(0, new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{theme[..1].ToUpperInvariant()}{theme[1..]}.xaml")
        });
        _current = theme;
        Changed?.Invoke();
    }
}

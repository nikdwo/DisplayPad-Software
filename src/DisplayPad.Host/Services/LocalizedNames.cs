namespace DisplayPad.Host.Services;

public static class LocalizedNames
{
    public static string Profile(int number) => Profile(number, Loc.Get);
    public static string Page(int number) => Page(number, Loc.Get);
    public static string Folder(int number) => Folder(number, Loc.Get);
    public static string Copy(string existingName) => Copy(existingName, Loc.Get);

    public static string Profile(int number, Func<string, string> getResource) =>
        Format(getResource, "DefaultProfileName", number);

    public static string Page(int number, Func<string, string> getResource) =>
        Format(getResource, "DefaultPageName", number);

    public static string Folder(int number, Func<string, string> getResource) =>
        Format(getResource, "DefaultFolderName", number);

    public static string Copy(string existingName, Func<string, string> getResource) =>
        Format(getResource, "DefaultCopyName", existingName);

    private static string Format(Func<string, string> getResource, string key, object parameter) =>
        string.Format(getResource(key), parameter);
}

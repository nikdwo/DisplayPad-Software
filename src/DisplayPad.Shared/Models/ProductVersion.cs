using System.Reflection;

namespace DisplayPad.Shared.Models;

public static class ProductVersion
{
    public static string FromAssembly(Assembly assembly)
    {
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        return string.IsNullOrWhiteSpace(informationalVersion)
            ? assembly.GetName().Version?.ToString(3) ?? "unknown"
            : informationalVersion.Split('+', 2)[0];
    }
}

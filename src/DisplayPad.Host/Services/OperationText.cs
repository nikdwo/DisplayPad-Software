using DisplayPad.Shared.Models;

namespace DisplayPad.Host.Services;

public static class OperationText
{
    public static string Format(OperationResult result) =>
        Format(result, Loc.Get);

    public static string Format(OperationResult result, Func<string, string> getResource) =>
        Format(result.ErrorCode, result.Parameters, result.TechnicalDetail, getResource);

    public static string Format(OperationErrorCode errorCode, IEnumerable<string>? parameters = null,
        string? technicalDetail = null, Func<string, string>? getResource = null)
    {
        getResource ??= Loc.Get;
        var template = getResource($"Error{errorCode}");
        var parameterValues = parameters?.ToArray() ?? Array.Empty<string>();
        if (errorCode == OperationErrorCode.ConfigFieldTooLong && parameterValues.Length > 0)
            parameterValues[0] = getResource($"Field{parameterValues[0]}");
        var values = parameterValues.Cast<object>().ToArray();
        string text;
        try { text = string.Format(template, values); }
        catch (FormatException) { text = template; }

        return string.IsNullOrWhiteSpace(technicalDetail)
            ? text
            : string.Format(getResource("ErrorWithTechnicalDetail"), text, technicalDetail);
    }

    public static OperationResult FromException(Exception exception, OperationErrorCode fallback = OperationErrorCode.Unknown) =>
        exception switch
        {
            ConfigValidationException validation => validation.Result,
            ConfigLoadException load => load.Result with { TechnicalDetail = load.InnerException?.Message },
            _ => OperationResult.Fail(fallback, exception.Message)
        };
}

public sealed class HostConfigNameProvider : IConfigNameProvider
{
    public string ProfileName(string language, int number) =>
        LocalizedNames.Profile(number, key => Loc.GetForLanguage(language, key));

    public string PageName(string language, int number) =>
        LocalizedNames.Page(number, key => Loc.GetForLanguage(language, key));
}

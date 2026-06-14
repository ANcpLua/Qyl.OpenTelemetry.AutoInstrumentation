namespace Qyl.AutoInstrumentation;

internal static class QylHttpMethod
{
    public static string Normalize(string? method)
    {
        if (string.IsNullOrEmpty(method))
            return QylSemanticAttributes.HttpRequestMethodOther;

        if (string.Equals(method, QylSemanticAttributes.HttpRequestMethodConnect, StringComparison.OrdinalIgnoreCase))
            return QylSemanticAttributes.HttpRequestMethodConnect;

        if (string.Equals(method, QylSemanticAttributes.HttpRequestMethodDelete, StringComparison.OrdinalIgnoreCase))
            return QylSemanticAttributes.HttpRequestMethodDelete;

        if (string.Equals(method, QylSemanticAttributes.HttpRequestMethodGet, StringComparison.OrdinalIgnoreCase))
            return QylSemanticAttributes.HttpRequestMethodGet;

        if (string.Equals(method, QylSemanticAttributes.HttpRequestMethodHead, StringComparison.OrdinalIgnoreCase))
            return QylSemanticAttributes.HttpRequestMethodHead;

        if (string.Equals(method, QylSemanticAttributes.HttpRequestMethodOptions, StringComparison.OrdinalIgnoreCase))
            return QylSemanticAttributes.HttpRequestMethodOptions;

        if (string.Equals(method, QylSemanticAttributes.HttpRequestMethodPatch, StringComparison.OrdinalIgnoreCase))
            return QylSemanticAttributes.HttpRequestMethodPatch;

        if (string.Equals(method, QylSemanticAttributes.HttpRequestMethodPost, StringComparison.OrdinalIgnoreCase))
            return QylSemanticAttributes.HttpRequestMethodPost;

        if (string.Equals(method, QylSemanticAttributes.HttpRequestMethodPut, StringComparison.OrdinalIgnoreCase))
            return QylSemanticAttributes.HttpRequestMethodPut;

        if (string.Equals(method, QylSemanticAttributes.HttpRequestMethodTrace, StringComparison.OrdinalIgnoreCase))
            return QylSemanticAttributes.HttpRequestMethodTrace;

        return QylSemanticAttributes.HttpRequestMethodOther;
    }
}

using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Haven.Core;

namespace Haven.Application;

public sealed record NotesHtmlSandboxDocument(string DocumentHtml, string Error);

public static partial class NotesHtmlSandbox
{
    private const int MaximumCombinedSourceLength = 5_000_000;

    public static NotesHtmlSandboxDocument Build(NotesHtmlData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var combinedLength = checked(data.HtmlSource.Length + data.CssSource.Length + data.JavaScriptSource.Length);
        if (combinedLength > MaximumCombinedSourceLength)
            return Blocked("The HTML widget source exceeds the five-million-character safety limit.");
        if (!data.AllowScripts && !string.IsNullOrWhiteSpace(data.JavaScriptSource))
            return Blocked("JavaScript source requires explicit script permission.");
        if (!data.AllowNetwork && NetworkReferencePattern().IsMatch(data.HtmlSource + data.CssSource + data.JavaScriptSource))
            return Blocked("Network references require explicit network permission.");
        if (!data.AllowForms && FormPattern().IsMatch(data.HtmlSource))
            return Blocked("Forms require explicit form permission.");
        if (data.AllowPopups)
            return Blocked("Popups are not supported by the Notes sandbox.");

        var networkSources = data.AllowNetwork ? " http: https:" : string.Empty;
        var scriptSources = data.AllowScripts ? "'unsafe-inline'" + networkSources : "'none'";
        var connectSources = data.AllowNetwork ? "http: https:" : "'none'";
        var formAction = data.AllowForms && data.AllowNetwork ? "http: https:" : "'none'";
        var policy = string.Join(' ',
            "default-src 'none';",
            "base-uri 'none';",
            "object-src 'none';",
            "frame-src 'none';",
            "frame-ancestors 'none';",
            $"img-src data: blob:{networkSources};",
            $"media-src data: blob:{networkSources};",
            $"font-src data:{networkSources};",
            $"style-src 'unsafe-inline'{networkSources};",
            $"script-src {scriptSources};",
            $"connect-src {connectSources};",
            $"form-action {formAction};");

        var document = new StringBuilder(combinedLength + 1_024)
            .Append("<!doctype html><html><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            .Append("<meta http-equiv=\"Content-Security-Policy\" content=\"")
            .Append(WebUtility.HtmlEncode(policy))
            .Append("\"><style>")
            .Append("html,body{margin:0;min-height:100%;}body{box-sizing:border-box;padding:12px;font-family:system-ui,sans-serif;}")
            .Append(EscapeStyleEndTag(data.CssSource))
            .Append("</style></head><body>")
            .Append(data.HtmlSource);

        if (data.AllowScripts && !string.IsNullOrWhiteSpace(data.JavaScriptSource))
        {
            document.Append("<script>")
                .Append(EscapeScriptEndTag(data.JavaScriptSource))
                .Append("</script>");
        }

        document.Append("</body></html>");
        return new NotesHtmlSandboxDocument(document.ToString(), string.Empty);
    }

    private static NotesHtmlSandboxDocument Blocked(string error) => new(string.Empty, error);

    private static string EscapeScriptEndTag(string value) =>
        ScriptEndTagPattern().Replace(value, "<\\/script");

    private static string EscapeStyleEndTag(string value) =>
        StyleEndTagPattern().Replace(value, "<\\/style");

    [GeneratedRegex("(?:https?:)?//|url\\s*\\(|@import", RegexOptions.IgnoreCase)]
    private static partial Regex NetworkReferencePattern();

    [GeneratedRegex("<\\s*form\\b", RegexOptions.IgnoreCase)]
    private static partial Regex FormPattern();

    [GeneratedRegex("</script", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptEndTagPattern();

    [GeneratedRegex("</style", RegexOptions.IgnoreCase)]
    private static partial Regex StyleEndTagPattern();
}

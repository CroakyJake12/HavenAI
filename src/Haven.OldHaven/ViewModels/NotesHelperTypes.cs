using System.Text;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Represents notes equation render result and keeps its related state and behavior together.
/// </summary>
public sealed record NotesEquationRenderResult(string RenderedText, string Error);

/// <summary>
/// Represents notes equation renderer and keeps its related state and behavior together.
/// </summary>
public static class NotesEquationRenderer
{
    private static readonly IReadOnlyDictionary<string, string> Symbols = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["\\alpha"] = "α", ["\\beta"] = "β", ["\\gamma"] = "γ", ["\\delta"] = "δ", ["\\theta"] = "θ",
        ["\\lambda"] = "λ", ["\\mu"] = "μ", ["\\pi"] = "π", ["\\sigma"] = "σ", ["\\phi"] = "φ",
        ["\\omega"] = "ω", ["\\times"] = "×", ["\\div"] = "÷", ["\\pm"] = "±", ["\\leq"] = "≤",
        ["\\geq"] = "≥", ["\\neq"] = "≠", ["\\infty"] = "∞", ["\\sum"] = "∑", ["\\prod"] = "∏",
        ["\\int"] = "∫", ["\\sqrt"] = "√", ["\\rightarrow"] = "→", ["\\leftarrow"] = "←"
    };

    public static NotesEquationRenderResult Render(string source)
    {
        var value = source?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return new NotesEquationRenderResult(string.Empty, "Equation source is empty.");
        var braces = 0;
        foreach (var character in value)
        {
            if (character == '{') braces++;
            else if (character == '}') braces--;
            if (braces < 0) return new NotesEquationRenderResult(string.Empty, "Equation has an unmatched closing brace.");
        }
        if (braces != 0) return new NotesEquationRenderResult(string.Empty, "Equation has unmatched braces.");
        foreach (var symbol in Symbols) value = value.Replace(symbol.Key, symbol.Value, StringComparison.Ordinal);
        value = ReplaceSuperscripts(value);
        value = value.Replace("\\frac", "fraction", StringComparison.Ordinal).Replace("\\text", string.Empty, StringComparison.Ordinal);
        return new NotesEquationRenderResult(value, string.Empty);
    }

    private static string ReplaceSuperscripts(string source)
    {
        var superscripts = new Dictionary<char, char> { ['0'] = '⁰', ['1'] = '¹', ['2'] = '²', ['3'] = '³', ['4'] = '⁴', ['5'] = '⁵', ['6'] = '⁶', ['7'] = '⁷', ['8'] = '⁸', ['9'] = '⁹', ['+'] = '⁺', ['-'] = '⁻', ['='] = '⁼', ['n'] = 'ⁿ' };
        var builder = new StringBuilder();
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] == '^' && index + 1 < source.Length)
            {
                var next = source[index + 1];
                if (superscripts.TryGetValue(next, out var replacement)) { builder.Append(replacement); index++; continue; }
                if (next == '{')
                {
                    var end = source.IndexOf('}', index + 2);
                    if (end > index)
                    {
                        var segment = source[(index + 2)..end];
                        if (segment.All(superscripts.ContainsKey)) { foreach (var character in segment) builder.Append(superscripts[character]); index = end; continue; }
                    }
                }
            }
            builder.Append(source[index]);
        }
        return builder.ToString();
    }
}

/// <summary>
/// Represents notes html sandbox result and keeps its related state and behavior together.
/// </summary>
public sealed record NotesHtmlSandboxResult(string DocumentHtml, string FallbackText, string Error);

/// <summary>
/// Represents notes html sandbox and keeps its related state and behavior together.
/// </summary>
public static class NotesHtmlSandbox
{
    public static NotesHtmlSandboxResult Build(NotesHtmlData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!data.AllowScripts && !string.IsNullOrWhiteSpace(data.JavaScriptSource))
            return new NotesHtmlSandboxResult(string.Empty, Fallback(data.HtmlSource), "JavaScript is present but script permission is disabled.");
        if (!data.AllowNetwork && ContainsNetworkReference(data.HtmlSource + data.CssSource + data.JavaScriptSource))
            return new NotesHtmlSandboxResult(string.Empty, Fallback(data.HtmlSource), "Network references are present but network permission is disabled.");
        if (!data.AllowForms && data.HtmlSource.Contains("<form", StringComparison.OrdinalIgnoreCase))
            return new NotesHtmlSandboxResult(string.Empty, Fallback(data.HtmlSource), "Forms are present but form permission is disabled.");
        if (data.HtmlSource.Contains("window.open", StringComparison.OrdinalIgnoreCase) || data.HtmlSource.Contains("target=\"_blank\"", StringComparison.OrdinalIgnoreCase) || data.HtmlSource.Contains("target='_blank'", StringComparison.OrdinalIgnoreCase))
            return new NotesHtmlSandboxResult(string.Empty, Fallback(data.HtmlSource), "Popups are not permitted in Notes widgets.");

        var contentSecurity = new StringBuilder("default-src 'none'; img-src data: blob:");
        if (data.AllowNetwork) contentSecurity.Append(" https:");
        contentSecurity.Append("; style-src 'unsafe-inline'");
        if (data.AllowNetwork) contentSecurity.Append(" https:");
        contentSecurity.Append("; font-src data:");
        if (data.AllowNetwork) contentSecurity.Append(" https:");
        contentSecurity.Append("; script-src ");
        contentSecurity.Append(data.AllowScripts ? "'unsafe-inline'" : "'none'");
        if (data.AllowNetwork && data.AllowScripts) contentSecurity.Append(" https:");
        contentSecurity.Append("; connect-src ").Append(data.AllowNetwork ? "https:" : "'none'").Append("; form-action ").Append(data.AllowForms ? "'self'" : "'none'").Append("; frame-src 'none'; object-src 'none'; base-uri 'none'");
        var script = data.AllowScripts ? "<script>" + data.JavaScriptSource + "</script>" : string.Empty;
        var document = "<!doctype html><html><head><meta charset=\"utf-8\"><meta http-equiv=\"Content-Security-Policy\" content=\"" + System.Net.WebUtility.HtmlEncode(contentSecurity.ToString()) + "\"><style>html,body{margin:0;padding:8px;font-family:system-ui}" + data.CssSource + "</style></head><body>" + data.HtmlSource + script + "</body></html>";
        return new NotesHtmlSandboxResult(document, Fallback(data.HtmlSource), string.Empty);
    }

    private static bool ContainsNetworkReference(string value) =>
        value.Contains("http://", StringComparison.OrdinalIgnoreCase)
        || value.Contains("https://", StringComparison.OrdinalIgnoreCase)
        || value.Contains("//", StringComparison.Ordinal)
        || value.Contains("@import", StringComparison.OrdinalIgnoreCase)
        || value.Contains("url(", StringComparison.OrdinalIgnoreCase)
        || value.Contains("fetch(", StringComparison.OrdinalIgnoreCase)
        || value.Contains("XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
        || value.Contains("WebSocket", StringComparison.OrdinalIgnoreCase);

    private static string Fallback(string html)
    {
        var builder = new StringBuilder();
        var inside = false;
        foreach (var character in html)
        {
            if (character == '<') { inside = true; builder.Append(' '); continue; }
            if (character == '>') { inside = false; continue; }
            if (!inside) builder.Append(character);
        }
        return System.Net.WebUtility.HtmlDecode(builder.ToString()).ReplaceLineEndings(" ").Trim();
    }
}

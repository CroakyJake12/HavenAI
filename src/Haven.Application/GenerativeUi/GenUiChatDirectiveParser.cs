using System.Text.Json;

namespace Haven.Application;

/// <summary>
/// A bounded model request for a canonical template. Model text can select and
/// seed a registered implementation; it cannot define controls or executable UI.
/// </summary>
public sealed record GenUiTemplateRequest(string TemplateKey, string? Expression);

public sealed record GenUiChatDirectiveParseResult(
    string DisplayContent,
    GenUiTemplateRequest? Request,
    string? Error,
    bool HasDirective);

public static class GenUiChatDirectiveParser
{
    private const string OpeningFence = "```haven-ui";
    private const int MaximumPayloadLength = 8_192;
    private const int MaximumExpressionLength = 256;

    public const string ModelInstruction = """
        Interactive Haven UI is enabled through registered, trusted templates. Do not claim that Haven cannot generate UI. Production chat currently supports the interactive Calculator template; other template records are foundations and must not be presented as complete. If the user asks whether Generative UI is available, explain this exact boundary. When the user asks to open, create or generate a calculator, or when an interactive calculator is materially more useful than prose, append exactly one fenced haven-ui request in this shape:
        ```haven-ui
        {"version":1,"template":"calculator","inputs":{"expression":"2 + 2"}}
        ```
        The expression may be empty. Never place executable code, visual markup, styles, permissions, capability calls, or an invented template in the request. The request selects a trusted local template; it does not grant permission or prove that an action occurred.
        """;

    /// <summary>
    /// Handles product-capability questions from Haven's own registered state
    /// instead of asking a local model to guess whether the host can render UI.
    /// The response includes the one production Chat template as live proof and
    /// remains explicit that the other registry records are foundations.
    /// </summary>
    public static bool TryCreateAvailabilityResponse(string? prompt, out string response)
    {
        var normalised = string.Join(" ", (prompt ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var mentionsGeneratedUi = normalised.Contains("generative ui", StringComparison.Ordinal)
                                  || normalised.Contains("generate ui", StringComparison.Ordinal)
                                  || normalised.Contains("generated ui", StringComparison.Ordinal)
                                  || normalised.Contains("interactive ui", StringComparison.Ordinal);
        var asksAvailability = normalised.Contains("yet", StringComparison.Ordinal)
                               || normalised.Contains("available", StringComparison.Ordinal)
                               || normalised.Contains("support", StringComparison.Ordinal)
                               || normalised.Contains("progress", StringComparison.Ordinal)
                               || normalised.StartsWith("how is ", StringComparison.Ordinal);
        if (!mentionsGeneratedUi || !asksAvailability)
        {
            response = string.Empty;
            return false;
        }

        response = """
            Yes—Haven Chat can render trusted interactive Generative UI now. The production Chat path currently includes the Calculator below, with live input, validation, actions, state patches and HavenUI styling. The other registered template records are foundations and are not being presented as finished features yet.

            ```haven-ui
            {"version":1,"template":"calculator","inputs":{"expression":""}}
            ```
            """;
        return true;
    }

    public static GenUiChatDirectiveParseResult Parse(string? content)
    {
        content ??= string.Empty;
        var opening = content.IndexOf(OpeningFence, StringComparison.OrdinalIgnoreCase);
        if (opening < 0) return new GenUiChatDirectiveParseResult(content, null, null, false);

        var payloadStart = content.IndexOf('\n', opening + OpeningFence.Length);
        if (payloadStart < 0)
            return Invalid(content[..opening].Trim(), "The generated UI request has no JSON payload.");
        payloadStart++;
        var closing = content.IndexOf("```", payloadStart, StringComparison.Ordinal);
        if (closing < 0)
            return Invalid(content[..opening].Trim(), "The generated UI request is missing its closing fence.");
        if (content.IndexOf(OpeningFence, closing + 3, StringComparison.OrdinalIgnoreCase) >= 0)
            return Invalid(RemoveBlock(content, opening, closing), "Only one generated UI request is allowed per message.");

        var visible = RemoveBlock(content, opening, closing);
        var payload = content[payloadStart..closing].Trim();
        if (payload.Length == 0 || payload.Length > MaximumPayloadLength)
            return Invalid(visible, "The generated UI request is empty or exceeds the safe size limit.");

        try
        {
            using var json = JsonDocument.Parse(payload, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8
            });
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Invalid(visible, "The generated UI request must be a JSON object.");
            var allowedTopLevel = new HashSet<string>(["version", "template", "inputs"], StringComparer.Ordinal);
            var topLevelProperties = root.EnumerateObject().ToArray();
            if (topLevelProperties.Any(property => !allowedTopLevel.Contains(property.Name))
                || topLevelProperties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != topLevelProperties.Length)
                return Invalid(visible, "The generated UI request contains an unsupported field.");
            if (!root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var versionNumber) || versionNumber != 1)
                return Invalid(visible, "The generated UI request uses an unsupported contract version.");
            if (!root.TryGetProperty("template", out var templateElement) || templateElement.ValueKind != JsonValueKind.String)
                return Invalid(visible, "The generated UI request must name a registered template.");
            var template = templateElement.GetString()?.Trim() ?? string.Empty;
            if (!template.Equals("calculator", StringComparison.OrdinalIgnoreCase))
                return Invalid(visible, $"Template '{template}' is not available on the production chat path yet.");

            string? expression = null;
            if (root.TryGetProperty("inputs", out var inputs))
            {
                if (inputs.ValueKind != JsonValueKind.Object)
                    return Invalid(visible, "Template inputs must be a JSON object.");
                var allowedInputs = new HashSet<string>(["expression"], StringComparer.Ordinal);
                var inputProperties = inputs.EnumerateObject().ToArray();
                if (inputProperties.Any(property => !allowedInputs.Contains(property.Name))
                    || inputProperties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != inputProperties.Length)
                    return Invalid(visible, "The calculator request contains an unsupported input.");
                if (inputs.TryGetProperty("expression", out var expressionElement))
                {
                    if (expressionElement.ValueKind != JsonValueKind.String)
                        return Invalid(visible, "The calculator expression must be text.");
                    expression = expressionElement.GetString()?.Trim();
                    if (expression?.Length > MaximumExpressionLength)
                        return Invalid(visible, "The calculator expression exceeds the safe length limit.");
                }
            }

            return new GenUiChatDirectiveParseResult(
                visible,
                new GenUiTemplateRequest("calculator", expression),
                null,
                true);
        }
        catch (JsonException)
        {
            return Invalid(visible, "The generated UI request contains invalid JSON.");
        }
        catch (InvalidOperationException)
        {
            return Invalid(visible, "The generated UI request contains an invalid field value.");
        }
    }

    private static string RemoveBlock(string content, int opening, int closing) =>
        (content[..opening] + content[(closing + 3)..]).Trim();

    private static GenUiChatDirectiveParseResult Invalid(string visible, string error) =>
        new(visible, null, error, true);
}

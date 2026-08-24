using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// A bounded model request for a canonical template. Model text can select and
/// seed a registered implementation; it cannot define executable UI or code.
/// </summary>
public sealed record GenUiTemplateRequest(
    string TemplateKey,
    IReadOnlyDictionary<string, JsonElement> Inputs,
    string? AccentKey = null)
{
    public string? Expression =>
        Inputs.TryGetValue("expression", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public string Signature => TemplateKey + "|" + JsonSerializer.Serialize(
        Inputs.OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
}

public sealed record GenUiChatDirectiveParseResult(
    string DisplayContent,
    IReadOnlyList<GenUiTemplateRequest> Requests,
    string? Error,
    bool HasDirective)
{
    public GenUiTemplateRequest? Request => Requests.FirstOrDefault();
}

public static class GenUiChatDirectiveParser
{
    private const string OpeningFence = "```haven-ui";
    private const string QuestionOpening = "<haven-question>";
    private const string QuestionClosing = "</haven-question>";
    private const int MaximumPayloadLength = 8_192;
    private const int MaximumExpressionLength = 256;
    private const int MaximumQuestionLength = 500;
    private const int MaximumOptionLength = 160;
    private const int MaximumFormFields = 12;

    public static string ModelInstruction
    {
        get
        {
            var live = LiveTemplates();
            var liveSummary = string.Join(", ", live.Select(template => $"{template.Name} (`{template.Key}`)"));
            return $$$$"""
                Haven can render interactive UI inline in the chat. Available templates: {{{{liveSummary}}}}.

                To render a template, output a fenced code block with the language tag `haven-ui` containing a JSON object with `version` (always 1), `template` (the template key), and `inputs` (template-specific parameters). Example:
                ```haven-ui
                {"version":1,"template":"calculator","inputs":{"expression":"2 + 2"}}
                ```

                Stream for immediate feedback: when the response will contain Generative UI, begin the first `haven-ui` fence immediately (or after one short orienting sentence), put `version` and `template` before the larger inputs/components payload, and do not narrate a long plan before the declaration. Haven mounts a template-shaped loading skeleton from those first streamed fields, then progressively reveals the trusted controls as the declaration completes.

                Templates: `calculator` (expression), `structured-form` (title, schema with id/label/type), `checklist` (items array), `data-grid` (columns, rows), `dashboard` (panels), `card-deck` (cards/items with front and back content), `graph` (expressions), `task-list` (tasks), `workflow` (steps), `assessment` (questions). Use `choice-prompt` for quick 2-3 option questions.

                Selection rules are strict: use `calculator` only when the user asks for arithmetic or calculation. Never use a calculator as a generic Generative UI demo or as the answer to "can you generate UI?" For flashcards, use `card-deck` and provide multiple real cards/items with front and back content. For a unique request such as a crafting table, use `custom` and build the purpose-specific interface with nested HavenUI components. If the user asks whether GenUI is available without requesting a UI, answer briefly and do not emit a calculator block.

                Generative UI is a complete task surface, not a decorative widget or proof-of-concept. Before emitting UI, determine the user's actual purpose, choose the simplest suitable App/template/foundation, and implement the normal core workflow implied by the requested name. A flashcard deck must support reveal, response/confidence, progress and navigation. A whiteboard must support the relevant tools, selection, editing, undo/redo and persistence. A crafting table must support item placement, removal, recipe/output state and a useful inventory interaction. A board, calendar, editor, quiz, dashboard or form must likewise expose its expected core interactions rather than only a static shell.

                After generating the declaration, perform a self-check before presenting it: verify the JSON/contract, every component type, nested container content, stable IDs, action bindings, state ownership, result/update paths, empty/loading/error states, accessibility names, responsive layout and whether the surface actually fulfills the user's requested purpose. Repair or extend the declaration when the check finds a missing core interaction. Do not present a static placeholder as a completed interactive experience. Keep conversation available alongside the surface.

                When no template fits the request, generate a custom UI using HavenUI components. Use `template` set to `"custom"` and provide a `components` array instead of `inputs`. Each component has `id`, `type`, `props`, and optional `children` and `actions`. Available types include: `HavenStack` (vertical layout), `HavenGrid` (responsive grid; use `columns`, `spacing`, `responsive`, and `itemMinWidth`), `HavenSplitView` (two-region composition), `HavenCard` (rounded surface), `HavenText` (supports `text`, `fontSize`, `textAlignment`, `emphasis`, and scoped accent-safe `tone`), `HavenButton` (button with `label` prop and an action), `HavenTextInput`, `HavenSelect`, `HavenToggle`, `HavenSlider`, `HavenProgress`, `HavenStatus`, `HavenToolbar`, `HavenList`, `HavenTabs`, `HavenGraph`, `HavenChart`, `HavenCanvas`, and `HavenImage`. Layout components may use bounded `minWidth`, `minHeight`, `width`, `height`, and `horizontalAlignment` props. Use these primitives to fill the available chat width when the task benefits from a wide or spatial workspace instead of defaulting to a single vertical stack.

                `HavenCanvas` is available on migrated Haven-native Chat as an interactive whiteboard foundation with pen, highlighter, eraser, text, shape and pan tools, selection/editing, undo/redo, zoom and persisted state. `HavenGraph` and `HavenChart` render through native Haven drawing commands; the graph template supports live expression updates. Use these foundations only when they fit the requested workflow, and do not claim controls or editing modes that the emitted surface does not actually provide.

                To make custom components interactive, use bounded declarative actions. Each action requires an `id` and may include a short `message` plus `patches`. A patch is `{"target":"component-id-or-state","path":"property-or-state-key","value":<JSON literal>}`. `target` may be an existing component ID or the literal `state`; `path` names a trusted component property or state key. Example: `{"id":"slot.place","message":"Placed oak plank.","patches":[{"target":"slot-label","path":"text","value":"Oak Plank"},{"target":"state","path":"slot1","value":"oak_plank"}]}`. Use patches for the normal state transitions of games, practice tools, boards, workflows, and other custom experiences. Actions are declarative only: never emit code, scripts, commands, URLs, or executable expressions. Button clicks, toggle changes, text submits, and slider drags all emit events.

                Container types like HavenGrid, HavenStack, HavenCard MUST have a `children` array. HavenGrid arranges children into columns. Example — an interactive crafting table with clickable slots:
                ```haven-ui
                {"version":1,"template":"custom","title":"Crafting Table","accent":"orange","components":[{"id":"header","type":"HavenText","props":{"text":"Crafting","emphasis":true}},{"id":"grid","type":"HavenGrid","props":{"columns":3},"children":[{"id":"s1","type":"HavenCard","props":{},"children":[{"id":"t1","type":"HavenText","props":{"text":"Empty"}},{"id":"b1","type":"HavenButton","props":{"label":"Place"},"actions":[{"id":"slot.place.1"}]}]},{"id":"s2","type":"HavenCard","props":{},"children":[{"id":"t2","type":"HavenText","props":{"text":"Empty"}},{"id":"b2","type":"HavenButton","props":{"label":"Place"},"actions":[{"id":"slot.place.2"}]}]},{"id":"s3","type":"HavenCard","props":{},"children":[{"id":"t3","type":"HavenText","props":{"text":"Empty"}},{"id":"b3","type":"HavenButton","props":{"label":"Place"},"actions":[{"id":"slot.place.3"}]}]},{"id":"s4","type":"HavenCard","props":{},"children":[{"id":"t4","type":"HavenText","props":{"text":"Empty"}},{"id":"b4","type":"HavenButton","props":{"label":"Place"},"actions":[{"id":"slot.place.4"}]}]},{"id":"s5","type":"HavenCard","props":{},"children":[{"id":"t5","type":"HavenText","props":{"text":"Diamond"}},{"id":"b5","type":"HavenButton","props":{"label":"Clear"},"actions":[{"id":"slot.clear.5"}]}]},{"id":"s6","type":"HavenCard","props":{},"children":[{"id":"t6","type":"HavenText","props":{"text":"Empty"}},{"id":"b6","type":"HavenButton","props":{"label":"Place"},"actions":[{"id":"slot.place.6"}]}]},{"id":"s7","type":"HavenCard","props":{},"children":[{"id":"t7","type":"HavenText","props":{"text":"Empty"}},{"id":"b7","type":"HavenButton","props":{"label":"Place"},"actions":[{"id":"slot.place.7"}]}]},{"id":"s8","type":"HavenCard","props":{},"children":[{"id":"t8","type":"HavenText","props":{"text":"Empty"}},{"id":"b8","type":"HavenButton","props":{"label":"Place"},"actions":[{"id":"slot.place.8"}]}]},{"id":"s9","type":"HavenCard","props":{},"children":[{"id":"t9","type":"HavenText","props":{"text":"Empty"}},{"id":"b9","type":"HavenButton","props":{"label":"Place"},"actions":[{"id":"slot.place.9"}]}]}]},{"id":"arrow","type":"HavenText","props":{"text":"→ Output"}},{"id":"output","type":"HavenCard","props":{},"children":[{"id":"out-text","type":"HavenText","props":{"text":"Diamond Pickaxe","emphasis":true}},{"id":"craft-btn","type":"HavenButton","props":{"label":"Craft","kind":"primary"},"actions":[{"id":"craft.execute"}]}]},{"id":"status","type":"HavenStatus","props":{"text":"Place items to craft","automationName":"Crafting status"}}]}
                ```

                Prefer templates when they fit. Use custom components for unique interactive experiences. Outside `haven-ui` fences, include only concise user-facing prose that adds useful context. The fenced JSON is transport data: never echo the declaration, component props, input records, arrays, IDs, or other structured payloads again as visible prose, and never dump raw JSON unless the user explicitly asks to see raw JSON. You can include multiple `haven-ui` blocks in one response.

                You can set a scoped accent color for any UI block by adding an `accent` field. This changes the accent colors for just that surface, not the whole app. Values: `blue`, `green`, `orange`, `purple`, `teal`, `pink`, `yellow`, `red`, `indigo`, `sky`. Example:
                ```haven-ui
                {"version":1,"template":"calculator","inputs":{"expression":"2+2"},"accent":"blue"}
                ```
                """;
        }
    }

    /// <summary>
    /// Applies the user's autonomous Generative UI preference without disabling
    /// explicit requests for interactive or visual responses.
    /// </summary>
    public static string ModelInstructionFor(GenerativeUiResponseMode mode)
    {
        var preference = mode switch
        {
            GenerativeUiResponseMode.AlwaysVisual => "Response preference: Always Visual. When the request can be represented usefully with Haven Generative UI, render it; prefer interactive or visual UI over a text-only presentation.",
            GenerativeUiResponseMode.PreferVisual => "Response preference: Prefer Visual. Prefer Haven Generative UI whenever it is a natural fit, falling back to text when UI would add little value.",
            GenerativeUiResponseMode.PreferText => "Response preference: Prefer Text. Default to a text response and use Haven Generative UI autonomously only when it provides a clear usability advantage.",
            GenerativeUiResponseMode.AlwaysText => "Response preference: Always Text. Do not invoke Haven Generative UI on your own. You may still render Haven Generative UI when the user explicitly asks for a visual, interactive, generated, or UI response.",
            _ => "Response preference: Auto. Choose between text and Haven Generative UI based on which format best serves the request."
        };
        return preference + "\n\n" + ModelInstruction;
    }

    /// <summary>
    /// Answers capability-status questions from Haven's actual live registry
    /// rather than asking a local model to guess what the host can render.
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
        var availabilityProbe = normalised.TrimEnd('?', '!', '.');
        var asksAvailability = normalised.Contains("yet", StringComparison.Ordinal)
                               || normalised.Contains("available", StringComparison.Ordinal)
                               || normalised.Contains("support", StringComparison.Ordinal)
                               || normalised.Contains("progress", StringComparison.Ordinal)
                               || normalised.StartsWith("how is ", StringComparison.Ordinal)
                               || availabilityProbe is "can you generate ui" or "can you generate generative ui";
        if (!mentionsGeneratedUi || !asksAvailability)
        {
            response = string.Empty;
            return false;
        }

        var liveNames = string.Join(", ", LiveTemplates().Select(template => template.Name));
        response = $$$$"""
            Yes—Haven can render interactive UI in the chat canvas. Available templates include {{{{liveNames}}}}. Templates are reusable foundations, not the limit of Haven's UI capability. For a specific request, describe the purpose and Haven will choose the appropriate foundation or compose a custom HavenUI surface.
            """;
        return true;
    }

    public static GenUiChatDirectiveParseResult Parse(string? content)
    {
        var remaining = content ?? string.Empty;
        var requests = new List<GenUiTemplateRequest>();
        var errors = new List<string>();
        var hasDirective = false;

        while (true)
        {
            var fenceOpening = remaining.IndexOf(OpeningFence, StringComparison.OrdinalIgnoreCase);
            var questionOpening = remaining.IndexOf(QuestionOpening, StringComparison.OrdinalIgnoreCase);
            if (fenceOpening < 0 && questionOpening < 0) break;

            hasDirective = true;
            var parseQuestion = questionOpening >= 0 && (fenceOpening < 0 || questionOpening < fenceOpening);
            var parsed = parseQuestion
                ? ParseQuestion(remaining, questionOpening)
                : ParseTemplateBlock(remaining, fenceOpening);
            remaining = parsed.DisplayContent;
            requests.AddRange(parsed.Requests);
            if (!string.IsNullOrWhiteSpace(parsed.Error)) errors.Add(parsed.Error);
        }

        return new GenUiChatDirectiveParseResult(
            remaining.Trim(),
            requests,
            errors.Count == 0 ? null : string.Join(" ", errors),
            hasDirective);
    }

    private static GenUiChatDirectiveParseResult ParseTemplateBlock(string content, int opening)
    {
        var payloadStart = content.IndexOf('\n', opening + OpeningFence.Length);
        if (payloadStart < 0)
            return Invalid(content[..opening].Trim(), "The generated UI request has no JSON payload.");
        payloadStart++;
        var closing = content.IndexOf("```", payloadStart, StringComparison.Ordinal);
        if (closing < 0)
            return Invalid(content[..opening].Trim(), "The generated UI request is missing its closing fence.");
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
                MaxDepth = GenerativeUiContractValidator.MaximumDepth * 4
            });
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Invalid(visible, "The generated UI request must be a JSON object.");
            var allowedTopLevel = new HashSet<string>(["version", "template", "inputs", "title", "components", "accent"], StringComparer.Ordinal);
            var topLevelProperties = root.EnumerateObject().ToArray();
            if (topLevelProperties.Any(property => !allowedTopLevel.Contains(property.Name))
                || topLevelProperties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != topLevelProperties.Length)
                return Invalid(visible, "The generated UI request contains an unsupported field.");
            if (!root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var versionNumber) || versionNumber != 1)
                return Invalid(visible, "The generated UI request uses an unsupported contract version.");
            if (!root.TryGetProperty("template", out var templateElement) || templateElement.ValueKind != JsonValueKind.String)
                return Invalid(visible, "The generated UI request must name a registered template.");

            var templateKey = templateElement.GetString()?.Trim() ?? string.Empty;
            var accentKey = root.TryGetProperty("accent", out var accentEl) && accentEl.ValueKind == JsonValueKind.String
                ? accentEl.GetString()?.Trim() : null;

            // Custom templates generate UI from inline HavenUI component trees
            if (templateKey.Equals("custom", StringComparison.OrdinalIgnoreCase))
            {
                var title = root.TryGetProperty("title", out var titleEl) && titleEl.ValueKind == JsonValueKind.String
                    ? titleEl.GetString() ?? "Custom UI" : "Custom UI";
                if (!root.TryGetProperty("components", out var componentsEl) || componentsEl.ValueKind != JsonValueKind.Array)
                    return Invalid(visible, "Custom UI requires a components array.");
                var customInputs = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["title"] = JsonSerializer.SerializeToElement(title),
                    ["components"] = componentsEl.Clone()
                };
                return new GenUiChatDirectiveParseResult(visible,
                    [new GenUiTemplateRequest("custom", customInputs, accentKey)], null, true);
            }

            var template = LiveTemplates().FirstOrDefault(item => item.Key.Equals(templateKey, StringComparison.OrdinalIgnoreCase));
            if (template is null)
                return Invalid(visible, $"Template '{templateKey}' is not available on the live Chat path yet.");

            var parsedInputs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (root.TryGetProperty("inputs", out var inputs))
            {
                if (inputs.ValueKind != JsonValueKind.Object)
                    return Invalid(visible, "Template inputs must be a JSON object.");
                var inputProperties = inputs.EnumerateObject().ToArray();
                if (inputProperties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != inputProperties.Length)
                    return Invalid(visible, "The generated UI request contains a duplicate input.");
                // Pass all inputs through to the runtime — don't reject unknown keys.
                // The model may use alternative input names that the runtime can handle.
                foreach (var property in inputProperties) parsedInputs[property.Name] = property.Value.Clone();
                if (template.Key.Equals("card-deck", StringComparison.OrdinalIgnoreCase)
                    && !parsedInputs.ContainsKey("cards"))
                {
                    foreach (var alias in new[] { "items", "flashcards", "questions" })
                    {
                        if (!parsedInputs.TryGetValue(alias, out var aliasValue) || aliasValue.ValueKind != JsonValueKind.Array) continue;
                        parsedInputs["cards"] = aliasValue.Clone();
                        break;
                    }
                }
            }

            var inputError = ValidateInputs(template.Key, parsedInputs);
            if (inputError is not null) return Invalid(visible, inputError);
            return new GenUiChatDirectiveParseResult(
                visible,
                [new GenUiTemplateRequest(template.Key, parsedInputs, accentKey)],
                null,
                true);
        }
        catch (JsonException)
        {
            // Try to repair common model JSON mistakes (trailing commas, etc.)
            var repaired = TryRepairJson(payload);
            if (repaired is not null)
            {
                try
                {
                    using var json2 = JsonDocument.Parse(repaired, new JsonDocumentOptions
                    {
                        AllowTrailingCommas = true,
                        CommentHandling = JsonCommentHandling.Disallow,
                        MaxDepth = GenerativeUiContractValidator.MaximumDepth * 4
                    });
                    // Re-parse with the repaired JSON by recursing
                    var repairedContent = content[..opening] + OpeningFence + "\n" + repaired + "\n```" + content[(closing + 3)..];
                    return ParseTemplateBlock(repairedContent, opening);
                }
                catch { }
            }
            return Invalid(visible, "The generated UI request contains invalid JSON.");
        }
        catch (InvalidOperationException)
        {
            return Invalid(visible, "The generated UI request contains an invalid field value.");
        }
    }

    private static GenUiChatDirectiveParseResult ParseQuestion(string content, int opening)
    {
        var payloadStart = opening + QuestionOpening.Length;
        var closing = content.IndexOf(QuestionClosing, payloadStart, StringComparison.OrdinalIgnoreCase);
        if (closing < 0)
            return Invalid(content[..opening].Trim(), "The clarification UI request is incomplete.");
        var visible = RemoveQuestionBlock(content, opening, closing);
        var payload = content[payloadStart..closing].Trim();
        if (payload.Length == 0 || payload.Length > MaximumPayloadLength)
            return Invalid(visible, "The clarification UI request is empty or exceeds the safe size limit.");
        try
        {
            using var json = JsonDocument.Parse(payload, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 4
            });
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Invalid(visible, "The clarification UI request must be a JSON object.");
            var properties = root.EnumerateObject().ToArray();
            var allowed = new HashSet<string>(["question", "options"], StringComparer.Ordinal);
            if (properties.Any(property => !allowed.Contains(property.Name))
                || properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
                return Invalid(visible, "The clarification UI request contains an unsupported field.");
            if (!root.TryGetProperty("question", out var questionElement) || questionElement.ValueKind != JsonValueKind.String)
                return Invalid(visible, "The clarification UI request requires question text.");
            var question = questionElement.GetString()?.Trim() ?? string.Empty;
            if (question.Length == 0 || question.Length > MaximumQuestionLength)
                return Invalid(visible, "The clarification question is empty or too long.");
            if (!root.TryGetProperty("options", out var optionsElement) || optionsElement.ValueKind != JsonValueKind.Array)
                return Invalid(visible, "The clarification UI request requires options.");
            var options = optionsElement.EnumerateArray().ToArray();
            if (options.Length is < 2 or > 3 || options.Any(option => option.ValueKind != JsonValueKind.String))
                return Invalid(visible, "The clarification UI request requires two or three text options.");
            var optionTexts = options.Select(option => option.GetString()?.Trim() ?? string.Empty).ToArray();
            if (optionTexts.Any(option => option.Length == 0 || option.Length > MaximumOptionLength))
                return Invalid(visible, "A clarification option is empty or too long.");

            var inputs = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["question"] = JsonSerializer.SerializeToElement(question),
                ["options"] = JsonSerializer.SerializeToElement(optionTexts)
            };
            return new GenUiChatDirectiveParseResult(
                visible,
                [new GenUiTemplateRequest("choice-prompt", inputs)],
                null,
                true);
        }
        catch (JsonException)
        {
            return Invalid(visible, "The clarification UI request contains invalid JSON.");
        }
        catch (InvalidOperationException)
        {
            return Invalid(visible, "The clarification UI request contains an invalid field value.");
        }
    }

    private static string? ValidateInputs(string templateKey, IReadOnlyDictionary<string, JsonElement> inputs) =>
        templateKey switch
        {
            "calculator" => ValidateCalculatorInputs(inputs),
            "structured-form" => ValidateStructuredFormInputs(inputs),
            "choice-prompt" => ValidateChoicePromptInputs(inputs),
            "checklist" => ValidateChecklistInputs(inputs),
            "data-grid" => ValidateDataGridInputs(inputs),
            "dashboard" => ValidateDashboardInputs(inputs),
            "assessment" => ValidateAssessmentInputs(inputs),
            "card-deck" => ValidateCardDeckInputs(inputs),
            "graph" => ValidateGraphInputs(inputs),
            "task-list" => ValidateTaskListInputs(inputs),
            "workflow" => ValidateWorkflowInputs(inputs),
            _ => null
        };

    private static string? ValidateCalculatorInputs(IReadOnlyDictionary<string, JsonElement> inputs)
    {
        if (inputs.Keys.Any(key => !key.Equals("expression", StringComparison.Ordinal)))
            return "The calculator request contains an unsupported input.";
        if (!inputs.TryGetValue("expression", out var expression)) return null;
        if (expression.ValueKind != JsonValueKind.String) return "The calculator expression must be text.";
        return expression.GetString()?.Length > MaximumExpressionLength
            ? "The calculator expression exceeds the safe length limit."
            : null;
    }

    private static string? ValidateChoicePromptInputs(IReadOnlyDictionary<string, JsonElement> inputs)
    {
        if (!inputs.TryGetValue("question", out var question) || question.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(question.GetString()) || question.GetString()!.Length > MaximumQuestionLength)
            return "The choice prompt requires bounded question text.";
        if (!inputs.TryGetValue("options", out var options) || options.ValueKind != JsonValueKind.Array)
            return "The choice prompt requires options.";
        var values = options.EnumerateArray().ToArray();
        if (values.Length is < 2 or > 3 || values.Any(value => value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()) || value.GetString()!.Length > MaximumOptionLength))
            return "The choice prompt requires two or three bounded text options.";
        return null;
    }

    private static string? ValidateStructuredFormInputs(IReadOnlyDictionary<string, JsonElement> inputs)
    {
        if (inputs.TryGetValue("title", out var title)
            && (title.ValueKind != JsonValueKind.String || title.GetString()!.Length > 120))
            return "The structured form title must be bounded text.";
        if (!inputs.TryGetValue("schema", out var schema) || schema.ValueKind != JsonValueKind.Array)
            return "The structured form requires a schema array.";
        var fields = schema.EnumerateArray().ToArray();
        if (fields.Length is < 1 || fields.Length > MaximumFormFields)
            return $"The structured form requires between 1 and {MaximumFormFields} fields.";
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            if (field.ValueKind != JsonValueKind.Object) return "Every structured form field must be an object.";
            var properties = field.EnumerateObject().ToArray();
            var allowed = new HashSet<string>(["id", "label", "type", "placeholder", "options"], StringComparer.Ordinal);
            if (properties.Any(property => !allowed.Contains(property.Name))
                || properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
                return "A structured form field contains an unsupported property.";
            if (!field.TryGetProperty("id", out var idValue) || idValue.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(idValue.GetString()) || idValue.GetString()!.Length > 64
                || !ids.Add(idValue.GetString()!.Trim()))
                return "Structured form field IDs must be unique bounded text.";
            if (!field.TryGetProperty("label", out var label) || label.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(label.GetString()) || label.GetString()!.Length > 160)
                return "Every structured form field requires a bounded label.";
            if (!field.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                || type.GetString() is not ("text" or "select" or "toggle"))
                return "Structured form field type must be text, select, or toggle.";
            if (field.TryGetProperty("placeholder", out var placeholder)
                && (placeholder.ValueKind != JsonValueKind.String || placeholder.GetString()!.Length > 200))
                return "Structured form placeholders must be bounded text.";
            if (type.GetString() == "select")
            {
                if (!field.TryGetProperty("options", out var options) || options.ValueKind != JsonValueKind.Array)
                    return "Select fields require an options array.";
                var optionValues = options.EnumerateArray().ToArray();
                if (optionValues.Length is < 1 or > 12 || optionValues.Any(option => option.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(option.GetString()) || option.GetString()!.Length > MaximumOptionLength))
                    return "Select fields require between 1 and 12 bounded text options.";
            }
        }
        if (inputs.TryGetValue("initialValues", out var initialValues) && initialValues.ValueKind != JsonValueKind.Object)
            return "Structured form initialValues must be an object.";
        return null;
    }

    private static string? ValidateChecklistInputs(IReadOnlyDictionary<string, JsonElement> inputs)
    {
        if (!inputs.TryGetValue("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return "Checklist requires an items array.";
        var entries = items.EnumerateArray().ToArray();
        if (entries.Length is < 1 or > 50)
            return "Checklist requires between 1 and 50 items.";
        if (entries.Any(entry => entry.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(entry.GetString()) || entry.GetString()!.Length > 200))
            return "Checklist items must be bounded text.";
        return null;
    }

    private static string? ValidateDataGridInputs(IReadOnlyDictionary<string, JsonElement> inputs)
    {
        if (!inputs.TryGetValue("columns", out var columns) || columns.ValueKind != JsonValueKind.Array)
            return "Data grid requires a columns array.";
        var cols = columns.EnumerateArray().ToArray();
        if (cols.Length is < 1 or > 20)
            return "Data grid requires between 1 and 20 columns.";
        if (cols.Any(col => col.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(col.GetString()) || col.GetString()!.Length > 80))
            return "Data grid column names must be bounded text.";
        if (inputs.TryGetValue("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array)
                    return "Data grid rows must be arrays.";
            }
        }
        return null;
    }

    private static string? ValidateDashboardInputs(IReadOnlyDictionary<string, JsonElement> inputs)
    {
        if (inputs.TryGetValue("panels", out var panels) && panels.ValueKind == JsonValueKind.Array)
        {
            var entries = panels.EnumerateArray().ToArray();
            if (entries.Length > 20) return "Dashboard supports at most 20 panels.";
            foreach (var panel in entries)
            {
                if (panel.ValueKind != JsonValueKind.Object) return "Dashboard panels must be objects.";
            }
        }
        return null;
    }

    private static string? ValidateAssessmentInputs(IReadOnlyDictionary<string, JsonElement> inputs)
    {
        if (inputs.TryGetValue("title", out var title)
            && (title.ValueKind != JsonValueKind.String || title.GetString()!.Length > 200))
            return "Assessment title must be bounded text.";
        if (inputs.TryGetValue("questions", out var questions) && questions.ValueKind == JsonValueKind.Array)
        {
            var entries = questions.EnumerateArray().ToArray();
            if (entries.Length > 50) return "Assessment supports at most 50 questions.";
            foreach (var q in entries)
            {
                if (q.ValueKind != JsonValueKind.Object) return "Assessment questions must be objects.";
                if (q.TryGetProperty("text", out var text) && (text.ValueKind != JsonValueKind.String
                    || text.GetString()!.Length > 500))
                    return "Assessment question text must be bounded.";
            }
        }
        return null;
    }

    private static string? ValidateCardDeckInputs(IReadOnlyDictionary<string, JsonElement> inputs)
    {
        JsonElement cards = default;
        var found = false;
        foreach (var key in new[] { "cards", "items", "flashcards", "questions" })
        {
            if (!inputs.TryGetValue(key, out var candidate) || candidate.ValueKind != JsonValueKind.Array) continue;
            cards = candidate;
            found = true;
            break;
        }
        if (!found)
            return "Card deck requires a cards array.";
        var entries = cards.EnumerateArray().ToArray();
        if (entries.Length is < 1 or > 100)
            return "Card deck requires between 1 and 100 cards.";
        foreach (var card in entries)
        {
            if (card.ValueKind != JsonValueKind.Object) return "Cards must be objects.";
        }
        return null;
    }

    private static string? ValidateGraphInputs(IReadOnlyDictionary<string, JsonElement> inputs)
    {
        if (inputs.TryGetValue("expressions", out var expressions) && expressions.ValueKind == JsonValueKind.Array)
        {
            var entries = expressions.EnumerateArray().ToArray();
            if (entries.Length > 10) return "Graph supports at most 10 expressions.";
            if (entries.Any(entry => entry.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(entry.GetString()) || entry.GetString()!.Length > 256))
                return "Graph expressions must be bounded text.";
        }
        return null;
    }

    private static string? ValidateTaskListInputs(IReadOnlyDictionary<string, JsonElement> inputs)
    {
        if (inputs.TryGetValue("tasks", out var tasks) && tasks.ValueKind == JsonValueKind.Array)
        {
            var entries = tasks.EnumerateArray().ToArray();
            if (entries.Length > 50) return "Task list supports at most 50 tasks.";
            foreach (var task in entries)
            {
                if (task.ValueKind != JsonValueKind.Object) return "Tasks must be objects.";
            }
        }
        return null;
    }

    private static string? ValidateWorkflowInputs(IReadOnlyDictionary<string, JsonElement> inputs)
    {
        if (inputs.TryGetValue("steps", out var steps) && steps.ValueKind == JsonValueKind.Array)
        {
            var entries = steps.EnumerateArray().ToArray();
            if (entries.Length > 30) return "Workflow supports at most 30 steps.";
            foreach (var step in entries)
            {
                if (step.ValueKind != JsonValueKind.Object) return "Workflow steps must be objects.";
            }
        }
        return null;
    }

    /// <summary>
    /// Attempts to repair common JSON mistakes made by small local models.
    /// Returns null if repair is not possible.
    /// </summary>
    private static string? TryRepairJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        var repaired = json.Trim();

        // Remove trailing commas before } or ]
        repaired = System.Text.RegularExpressions.Regex.Replace(repaired, @",\s*([}\]])", "$1");

        // Remove any text after the last } or ] (model might add explanation)
        var lastBrace = repaired.LastIndexOf('}');
        var lastBracket = repaired.LastIndexOf(']');
        var lastStructural = Math.Max(lastBrace, lastBracket);
        if (lastStructural > 0 && lastStructural < repaired.Length - 1)
            repaired = repaired[..(lastStructural + 1)];

        // Try to close unclosed braces/brackets
        var openBraces = repaired.Count(c => c == '{') - repaired.Count(c => c == '}');
        var openBrackets = repaired.Count(c => c == '[') - repaired.Count(c => c == ']');
        while (openBrackets > 0) { repaired += "]"; openBrackets--; }
        while (openBraces > 0) { repaired += "}"; openBraces--; }

        // Try parsing with lenient options first
        try
        {
            System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(repaired);
            return repaired;
        }
        catch { }

        // More aggressive: try to extract just the JSON object
        var start = repaired.IndexOf('{');
        if (start > 0)
        {
            var candidate = repaired[start..];
            openBraces = candidate.Count(c => c == '{') - candidate.Count(c => c == '}');
            openBrackets = candidate.Count(c => c == '[') - candidate.Count(c => c == ']');
            while (openBrackets > 0) { candidate += "]"; openBrackets--; }
            while (openBraces > 0) { candidate += "}"; openBraces--; }
            try
            {
                System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(candidate);
                return candidate;
            }
            catch { }
        }

        return null;
    }

    private static IReadOnlyList<GenUiTemplateDefinition> LiveTemplates() =>
        TemplateRegistryCatalog.BuiltIns
            .Where(template => template.IsEnabled && template.Maturity is GenUiTemplateMaturity.Preview or GenUiTemplateMaturity.Production)
            .ToArray();

    private static string StripInternalDirectives(string content)
    {
        var result = content;
        var questionOpening = result.IndexOf(QuestionOpening, StringComparison.OrdinalIgnoreCase);
        if (questionOpening >= 0)
        {
            var questionClosing = result.IndexOf(QuestionClosing, questionOpening + QuestionOpening.Length, StringComparison.OrdinalIgnoreCase);
            result = questionClosing >= 0
                ? RemoveQuestionBlock(result, questionOpening, questionClosing)
                : result[..questionOpening].Trim();
        }
        var fenceOpening = result.IndexOf(OpeningFence, StringComparison.OrdinalIgnoreCase);
        if (fenceOpening < 0) return result.Trim();
        var payloadStart = result.IndexOf('\n', fenceOpening + OpeningFence.Length);
        var fenceClosing = payloadStart >= 0 ? result.IndexOf("```", payloadStart + 1, StringComparison.Ordinal) : -1;
        return fenceClosing >= 0 ? RemoveBlock(result, fenceOpening, fenceClosing) : result[..fenceOpening].Trim();
    }

    private static string RemoveQuestionBlock(string content, int opening, int closing) =>
        (content[..opening] + content[(closing + QuestionClosing.Length)..]).Trim();

    private static string RemoveBlock(string content, int opening, int closing) =>
        (content[..opening] + content[(closing + 3)..]).Trim();

    private static GenUiChatDirectiveParseResult Invalid(string visible, string error) =>
        new(visible, [], error, true);
}

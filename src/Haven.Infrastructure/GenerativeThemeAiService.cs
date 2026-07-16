using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class GenerativeThemeAiService(
    IOllamaClient models,
    IGenerativeThemeValidator validator,
    IProductionDiagnostics diagnostics) : IGenerativeThemeAiService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<GenerativeThemeProposal> CreateAsync(
        string prompt,
        string modelName,
        GenerativeThemePack? startingTheme,
        CancellationToken cancellationToken)
    {
        if (RuntimeSafetyState.IsSafeMode)
            throw new InvalidOperationException("Generative UI model requests are disabled in crash-loop recovery safe mode. " + RuntimeSafetyState.Reason);
        var request = NormalizePrompt(prompt);
        if (string.IsNullOrWhiteSpace(modelName)) throw new ArgumentException("Choose a model for Theme Studio.", nameof(modelName));

        var system = BuildSystemPrompt(startingTheme);
        var effort = Enum.GetValues<EffortLevel>().First();
        var response = await models.CompleteAsync(
            new OllamaChatRequest(
                modelName.Trim(),
                [new OllamaMessage("user", request)],
                effort,
                system),
            cancellationToken).ConfigureAwait(false);

        var proposalEnvelope = ParseEnvelope(response);
        var candidate = proposalEnvelope.Theme with
        {
            SchemaVersion = 1,
            Id = startingTheme is { IsBuiltIn: false } ? startingTheme.Id : Guid.NewGuid(),
            IsBuiltIn = false,
            Origin = GenerativeThemeOrigin.AiGenerated,
            Author = string.IsNullOrWhiteSpace(proposalEnvelope.Theme.Author) ? "Created with Haven Studio" : proposalEnvelope.Theme.Author,
            CreatedAt = startingTheme?.CreatedAt ?? DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var validation = validator.Validate(candidate);
        if (!validation.IsValid || validation.NormalizedTheme is null)
        {
            await diagnostics.WriteAsync(
                ReliabilitySeverity.Warning,
                "generative-ui",
                "ai-proposal-rejected",
                "A model-generated theme proposal failed the deterministic safety validator.",
                new Dictionary<string, string>
                {
                    ["model"] = modelName,
                    ["issues"] = string.Join(" | ", validation.Issues.Where(issue => issue.IsError).Take(12).Select(issue => issue.Path + ": " + issue.Message))
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            throw new InvalidDataException("The model returned an unsafe or invalid theme: " +
                                           string.Join("; ", validation.Issues.Where(issue => issue.IsError).Take(8).Select(issue => issue.Message)));
        }

        var summary = NormalizeText(proposalEnvelope.Summary, 500);
        var changes = (proposalEnvelope.Changes ?? [])
            .Select(item => NormalizeText(item, 240))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToArray();
        var notes = validation.Issues.Where(issue => !issue.IsError)
            .Select(issue => issue.Path + ": " + issue.Message)
            .Concat((proposalEnvelope.SafetyNotes ?? []).Select(item => NormalizeText(item, 240)))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToArray();
        await diagnostics.WriteAsync(
            ReliabilitySeverity.Information,
            "generative-ui",
            "ai-proposal-created",
            "A model-generated theme proposal passed deterministic validation and is ready for preview.",
            new Dictionary<string, string>
            {
                ["model"] = modelName,
                ["themeId"] = validation.NormalizedTheme.Id.ToString("D"),
                ["pageCount"] = validation.NormalizedTheme.Pages.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["placementCount"] = validation.NormalizedTheme.Layout.Placements.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return new GenerativeThemeProposal(validation.NormalizedTheme, summary, changes, notes);
    }

    private static ProposalEnvelope ParseEnvelope(string response)
    {
        if (string.IsNullOrWhiteSpace(response)) throw new InvalidDataException("The model returned an empty Theme Studio response.");
        var start = response.IndexOf('{');
        var end = response.LastIndexOf('}');
        if (start < 0 || end <= start) throw new InvalidDataException("The model did not return the required JSON theme proposal.");
        var json = response[start..(end + 1)];
        try
        {
            var envelope = JsonSerializer.Deserialize<ProposalEnvelope>(json, JsonOptions);
            return envelope?.Theme is null
                ? throw new InvalidDataException("The model response did not contain a theme object.")
                : envelope;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The model returned malformed Theme Studio JSON.", ex);
        }
    }

    private static string BuildSystemPrompt(GenerativeThemePack? startingTheme)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are Haven Theme Studio. Produce a complete declarative Generative UI proposal as one JSON object and no markdown.");
        builder.AppendLine("You cannot emit XAML, C#, JavaScript, binding paths, reflection, arbitrary commands, filesystem paths, network URLs, plugins, or executable code.");
        builder.AppendLine("Every theme must contain complete light and dark palettes. Never omit either palette.");
        builder.AppendLine("Layout may only use the exact item IDs and regions listed below. Do not rename item IDs. Required controls cannot be hidden.");
        builder.AppendLine("Additional pages may only use Text, ShortcutGrid, Timer, CommandButton or Divider widgets and the approved command IDs below.");
        builder.AppendLine("The response object must be {\"theme\":<GenerativeThemePack>,\"summary\":\"...\",\"changes\":[\"...\"],\"safetyNotes\":[\"...\"]}.");
        builder.AppendLine("Use schemaVersion 1, a non-empty GUID placeholder, isBuiltIn false, origin 2, ISO timestamps, and conservative readable contrast.");
        builder.AppendLine("Movable item catalogue:");
        builder.AppendLine(JsonSerializer.Serialize(GenerativeUiCatalog.Items, JsonOptions));
        builder.AppendLine("Approved generated-page commands:");
        builder.AppendLine(JsonSerializer.Serialize(GenerativeUiCatalog.PageCommands, JsonOptions));
        builder.AppendLine("GeneratedWidgetKind numeric values: Text=0, ShortcutGrid=1, Timer=2, CommandButton=3, Divider=4.");
        builder.AppendLine("GenerativeThemeOrigin numeric values: BuiltIn=0, Manual=1, AiGenerated=2, Imported=3.");
        if (startingTheme is not null)
        {
            builder.AppendLine("Current theme to adapt. Return a complete replacement while preserving anything the user did not request to change:");
            builder.AppendLine(JsonSerializer.Serialize(startingTheme, JsonOptions));
        }
        else
        {
            builder.AppendLine("Default layout to use unless the user requests a safe change:");
            builder.AppendLine(JsonSerializer.Serialize(GenerativeUiCatalog.DefaultLayout, JsonOptions));
        }
        return builder.ToString();
    }

    private static string NormalizePrompt(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        normalized = new string(normalized.Where(character => !char.IsControl(character) || character is '\n' or '\t').ToArray());
        if (normalized.Length > 8_000) throw new ArgumentException("Theme Studio prompts are limited to 8,000 characters.", nameof(value));
        return string.IsNullOrWhiteSpace(normalized) ? throw new ArgumentException("Describe the theme or page you want Haven to create.", nameof(value)) : normalized;
    }

    private static string NormalizeText(string? value, int maximumLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        normalized = new string(normalized.Where(character => !char.IsControl(character) || character is '\n' or '\t').ToArray());
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private sealed record ProposalEnvelope(
        GenerativeThemePack Theme,
        string Summary,
        IReadOnlyList<string> Changes,
        IReadOnlyList<string> SafetyNotes);
}

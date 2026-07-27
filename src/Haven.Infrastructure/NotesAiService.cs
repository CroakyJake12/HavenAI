/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/NotesAiService.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns NotesAiService. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents notes ai service and keeps its related state and behavior together.
/// </summary>
public sealed class NotesAiService(
    IOllamaClient models,
    IProductionDiagnostics diagnostics) : INotesAiService
{
    /// <summary>
    /// Performs propose asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<NotesAiProposalResult> ProposeAsync(NotesAiProposalRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (RuntimeSafetyState.IsSafeMode)
            throw new InvalidOperationException("Notes AI is disabled in crash-loop recovery Safe Mode. Local editing and recovery remain available.");
        if (string.IsNullOrWhiteSpace(request.Instruction)) throw new ArgumentException("Describe the change you want Notes AI to propose.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.ModelName)) throw new ArgumentException("Choose a model before requesting a Notes AI proposal.", nameof(request));
        if (!request.AllowDocumentContext && string.IsNullOrWhiteSpace(request.SelectedText))
            throw new InvalidOperationException("Select text or explicitly allow document context before using Notes AI.");

        var context = request.AllowDocumentContext
            ? Limit(request.DocumentContext, 30_000)
            : Limit(request.SelectedText, 12_000);
        var evidence = request.Citations.Take(30).Select(citation => new
        {
            id = citation.Id,
            citation.Key,
            citation.Title,
            citation.Authors,
            citation.Year,
            citation.Url,
            citation.EvidenceExcerpt,
            citation.SourceLocation
        }).ToArray();
        var payload = JsonSerializer.Serialize(new
        {
            instruction = request.Instruction.Trim(),
            selectedText = Limit(request.SelectedText, 12_000),
            context,
            evidence
        });
        var system = """
            You are Haven Notes' reviewed writing assistant. Return exactly one JSON object and no markdown.
            Schema:
            {
              "proposedContent": "replacement or new content",
              "explanation": "brief reason and uncertainty statement",
              "citationIds": ["GUID"]
            }
            Use only the supplied context and evidence. Never invent a source, quotation, date, statistic or citation.
            Preserve the user's meaning unless explicitly asked to transform it. If the evidence is insufficient, say so
            in explanation and avoid unsupported factual additions. The result is a proposal and will be reviewed by the user.
            """;
        var correlationId = Guid.NewGuid().ToString("N");
        try
        {
            var response = await models.CompleteAsync(new OllamaChatRequest(
                request.ModelName,
                [new OllamaMessage("user", payload)],
                EffortLevel.High,
                system,
                Options: new GenerationOptions(0.2, 16_384, 0)), cancellationToken).ConfigureAwait(false);
            var result = Parse(response, request.Citations);
            await diagnostics.WriteAsync(
                ReliabilitySeverity.Information,
                "notes-ai",
                "proposal-created",
                "Notes AI produced a review-required proposal.",
                new Dictionary<string, string>
                {
                    ["documentId"] = request.DocumentId.ToString("D"),
                    ["blockId"] = request.BlockId?.ToString("D") ?? "document",
                    ["model"] = request.ModelName,
                    ["documentContextAllowed"] = request.AllowDocumentContext.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["citationCount"] = result.CitationIds.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                },
                correlationId,
                cancellationToken).ConfigureAwait(false);
            return result with { ProviderId = ProviderId(request.ModelName), ModelName = request.ModelName };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await diagnostics.WriteAsync(
                ReliabilitySeverity.Information,
                "notes-ai",
                "proposal-cancelled",
                "The user cancelled a Notes AI proposal.",
                new Dictionary<string, string> { ["documentId"] = request.DocumentId.ToString("D") },
                correlationId,
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (ex is not ArgumentException and not InvalidOperationException)
        {
            await diagnostics.WriteAsync(
                ReliabilitySeverity.Warning,
                "notes-ai",
                "proposal-failed",
                "Notes AI could not produce a valid review proposal.",
                new Dictionary<string, string>
                {
                    ["documentId"] = request.DocumentId.ToString("D"),
                    ["exceptionType"] = ex.GetType().FullName ?? ex.GetType().Name
                },
                correlationId,
                cancellationToken: CancellationToken.None).ConfigureAwait(false);
            throw new InvalidDataException("The selected model did not return a valid Notes proposal. No document content was changed.", ex);
        }
    }

    /// <summary>
    /// Performs the parse step owned by this component.
    /// </summary>
    private static NotesAiProposalResult Parse(string response, IReadOnlyList<NotesCitation> citations)
    {
        var json = ExtractJson(response);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 32 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Notes AI response was not a JSON object.");
        var proposed = root.TryGetProperty("proposedContent", out var proposedValue) && proposedValue.ValueKind == JsonValueKind.String
            ? proposedValue.GetString()?.Trim() ?? string.Empty
            : string.Empty;
        var explanation = root.TryGetProperty("explanation", out var explanationValue) && explanationValue.ValueKind == JsonValueKind.String
            ? explanationValue.GetString()?.Trim() ?? string.Empty
            : string.Empty;
        if (string.IsNullOrWhiteSpace(proposed)) throw new InvalidDataException("Notes AI returned an empty proposal.");
        if (proposed.Length > 2_000_000) throw new InvalidDataException("Notes AI proposal exceeded the two-million-character safety limit.");
        if (explanation.Length > 20_000) throw new InvalidDataException("Notes AI explanation exceeded the safety limit.");

        var available = citations.Select(citation => citation.Id).ToHashSet();
        var cited = new List<Guid>();
        if (root.TryGetProperty("citationIds", out var citationIds) && citationIds.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in citationIds.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.String || !Guid.TryParse(value.GetString(), out var id))
                    throw new InvalidDataException("Notes AI returned an invalid citation ID.");
                if (!available.Contains(id)) throw new InvalidDataException("Notes AI cited evidence that was not supplied to it.");
                if (!cited.Contains(id)) cited.Add(id);
            }
        }
        return new NotesAiProposalResult(proposed, explanation, cited, string.Empty, string.Empty);
    }

    /// <summary>
    /// Performs the extract json step owned by this component.
    /// </summary>
    private static string ExtractJson(string response)
    {
        var value = response.Trim();
        if (value.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLine = value.IndexOf('\n');
            var closing = value.LastIndexOf("```", StringComparison.Ordinal);
            if (firstLine >= 0 && closing > firstLine) value = value[(firstLine + 1)..closing].Trim();
        }
        var start = value.IndexOf('{');
        var end = value.LastIndexOf('}');
        if (start < 0 || end <= start) throw new InvalidDataException("Notes AI response did not contain JSON.");
        return value[start..(end + 1)];
    }

    /// <summary>
    /// Performs the limit step owned by this component.
    /// </summary>
    private static string Limit(string? value, int maximum)
    {
        var normalized = value ?? string.Empty;
        return normalized.Length <= maximum ? normalized : normalized[..maximum];
    }

    /// <summary>
    /// Performs the provider id step owned by this component.
    /// </summary>
    private static string ProviderId(string modelName)
    {
        var separator = modelName.IndexOf(':');
        return separator > 0 ? modelName[..separator].Trim().ToLowerInvariant() : "ollama";
    }
}

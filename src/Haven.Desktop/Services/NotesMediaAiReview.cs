/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Services/NotesMediaAiReview.cs, in the Desktop services layer, adapting application behavior to Windows and Avalonia concerns.
 * What: This file owns NotesMediaAiTarget, NotesMediaAiReview. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Services;

/// <summary>
/// Lists the supported notes media ai target values used to make state explicit and type-safe.
/// </summary>
public enum NotesMediaAiTarget
{
    AltText = 0,
    Caption = 1,
    Transcript = 2
}

/// <summary>
/// Represents notes media ai review and keeps its related state and behavior together.
/// </summary>
public static class NotesMediaAiReview
{
    /// <summary>
    /// Stores prefix locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const string Prefix = "[haven-media-ai:";
    /// <summary>
    /// Stores pending key prefix locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const string PendingKeyPrefix = "haven.notes.media-ai.pending.";
    /// <summary>
    /// Stores json options locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Performs propose asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public static async Task<NotesAiChange> ProposeAsync(
        INotesAiService ai,
        NotesWorkspaceViewModel workspace,
        NotesBlock block,
        NotesMediaAiTarget target,
        string instruction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ai);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(block);
        cancellationToken.ThrowIfCancellationRequested();
        var document = workspace.Document ?? throw new InvalidOperationException("Open a Notes document before requesting a media proposal.");
        _ = block.Media ?? throw new InvalidOperationException("Select an image, audio or video block first.");
        if (string.IsNullOrWhiteSpace(workspace.SelectedModelName))
            throw new InvalidOperationException("Choose a model in the Notes AI inspector first.");
        var userInstruction = string.IsNullOrWhiteSpace(instruction)
            ? DefaultInstruction(target, block.Kind)
            : instruction.Trim();
        var original = ReadTarget(block, target);
        var selectedEvidence = BuildSelectedEvidence(document, block, target, original);
        var documentContext = workspace.AllowDocumentContext
            ? string.Join("\n", NotesTextStatistics.EnumerateText(document))
            : string.Empty;
        var result = await ai.ProposeAsync(new NotesAiProposalRequest(
            document.Id,
            block.Id,
            BuildModelInstruction(target, userInstruction),
            selectedEvidence,
            documentContext,
            workspace.SelectedModelName,
            workspace.AllowDocumentContext,
            document.Citations), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        workspace.BeginBlockEdit(block);
        var previous = ReadPending(block, target);
        if (previous is not null)
        {
            previous.Status = NotesAiChangeStatus.Cancelled;
            previous.ReviewedAt = DateTimeOffset.UtcNow;
            previous.ReviewedBy = Environment.UserName;
            document.AiChanges.Add(previous);
        }
        var change = new NotesAiChange
        {
            BlockId = block.Id,
            Instruction = EncodeInstruction(target, userInstruction),
            OriginalContent = original,
            ProposedContent = result.ProposedContent.Trim(),
            Explanation = result.Explanation.Trim(),
            CitationIds = result.CitationIds.ToList(),
            ProviderId = result.ProviderId,
            ModelName = result.ModelName,
            Status = NotesAiChangeStatus.Proposed,
            UserConsentRecorded = true,
            SentDocumentContext = workspace.AllowDocumentContext,
            CreatedAt = DateTimeOffset.UtcNow
        };
        WritePending(block, target, change);
        workspace.CommitBlockEdit(block, "Created reviewed AI proposal for media " + DisplayName(target).ToLowerInvariant());
        return change;
    }

    /// <summary>
    /// Performs apply asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public static async Task ApplyAsync(
        NotesWorkspaceViewModel workspace,
        NotesBlock block,
        NotesAiChange change,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(change);
        cancellationToken.ThrowIfCancellationRequested();
        var document = workspace.Document ?? throw new InvalidOperationException("The Notes document is no longer open.");
        if (change.Status != NotesAiChangeStatus.Proposed)
            throw new InvalidOperationException("Only an unreviewed media proposal can be applied.");
        if (change.BlockId != block.Id || !TryGetTarget(change, out var target))
            throw new InvalidDataException("The media proposal target does not match the selected block.");
        if (string.IsNullOrWhiteSpace(change.ProposedContent))
            throw new InvalidDataException("The media proposal is empty.");
        var persisted = ReadPending(block, target);
        if (persisted?.Id != change.Id)
            throw new InvalidDataException("The media proposal is stale or has already been replaced.");

        workspace.BeginBlockEdit(block);
        WriteTarget(block, target, change.ProposedContent.Trim());
        RemovePending(block, target);
        change.Status = NotesAiChangeStatus.Applied;
        change.ReviewedAt = DateTimeOffset.UtcNow;
        change.ReviewedBy = Environment.UserName;
        document.AiChanges.Add(change);
        document.Revisions.Add(new NotesRevision
        {
            Kind = NotesRevisionKind.AiApplied,
            BlockId = block.Id,
            Author = Environment.UserName,
            Summary = "Applied reviewed AI media " + DisplayName(target).ToLowerInvariant(),
            CreatedAt = DateTimeOffset.UtcNow
        });
        workspace.CommitBlockEdit(block, "Applied reviewed AI media " + DisplayName(target).ToLowerInvariant());
        if (workspace.SaveCommand.CanExecute(null))
            await workspace.SaveCommand.ExecuteAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Performs the reject step owned by this component.
    /// </summary>
    public static void Reject(
        NotesWorkspaceViewModel workspace,
        NotesBlock block,
        NotesAiChange change)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(change);
        if (change.Status != NotesAiChangeStatus.Proposed) return;
        if (change.BlockId != block.Id || !TryGetTarget(change, out var target))
            throw new InvalidDataException("The media proposal target does not match the selected block.");
        var persisted = ReadPending(block, target);
        if (persisted?.Id != change.Id)
            throw new InvalidDataException("The media proposal is stale or has already been replaced.");
        workspace.BeginBlockEdit(block);
        RemovePending(block, target);
        change.Status = NotesAiChangeStatus.Rejected;
        change.ReviewedAt = DateTimeOffset.UtcNow;
        change.ReviewedBy = Environment.UserName;
        workspace.Document!.AiChanges.Add(change);
        workspace.CommitBlockEdit(block, "Rejected AI media accessibility proposal");
    }

    /// <summary>
    /// Performs the find pending step owned by this component.
    /// </summary>
    public static NotesAiChange? FindPending(
        NotesDocument document,
        Guid blockId,
        NotesMediaAiTarget target)
    {
        ArgumentNullException.ThrowIfNull(document);
        var block = document.Sections
            .SelectMany(section => section.Pages)
            .SelectMany(page => page.Blocks)
            .FirstOrDefault(value => value.Id == blockId);
        return block is null ? null : ReadPending(block, target);
    }

    /// <summary>
    /// Performs the display name step owned by this component.
    /// </summary>
    public static string DisplayName(NotesMediaAiTarget target) => target switch
    {
        NotesMediaAiTarget.AltText => "Alt text",
        NotesMediaAiTarget.Caption => "Caption",
        NotesMediaAiTarget.Transcript => "Transcript",
        _ => throw new ArgumentOutOfRangeException(nameof(target))
    };

    /// <summary>
    /// Attempts to get target and reports the result without using failure for normal control flow.
    /// </summary>
    public static bool TryGetTarget(NotesAiChange change, out NotesMediaAiTarget target)
    {
        ArgumentNullException.ThrowIfNull(change);
        target = default;
        var instruction = change.Instruction ?? string.Empty;
        if (!instruction.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var closing = instruction.IndexOf(']');
        if (closing <= Prefix.Length) return false;
        return Enum.TryParse(instruction[Prefix.Length..closing], ignoreCase: true, out target)
               && Enum.IsDefined(target);
    }

    /// <summary>
    /// Performs the pending key step owned by this component.
    /// </summary>
    private static string PendingKey(NotesMediaAiTarget target) =>
        PendingKeyPrefix + target.ToString().ToLowerInvariant();

    /// <summary>
    /// Performs the read pending step owned by this component.
    /// </summary>
    private static NotesAiChange? ReadPending(NotesBlock block, NotesMediaAiTarget target)
    {
        if (!block.Metadata.TryGetValue(PendingKey(target), out var json) || string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var change = JsonSerializer.Deserialize<NotesAiChange>(json, JsonOptions);
            return change is not null
                   && change.Status == NotesAiChangeStatus.Proposed
                   && change.BlockId == block.Id
                   && TryGetTarget(change, out var parsed)
                   && parsed == target
                ? change
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Performs the write pending step owned by this component.
    /// </summary>
    private static void WritePending(NotesBlock block, NotesMediaAiTarget target, NotesAiChange change) =>
        block.Metadata[PendingKey(target)] = JsonSerializer.Serialize(change, JsonOptions);

    /// <summary>
    /// Performs the remove pending step owned by this component.
    /// </summary>
    private static void RemovePending(NotesBlock block, NotesMediaAiTarget target) =>
        block.Metadata.Remove(PendingKey(target));

    /// <summary>
    /// Performs the encode instruction step owned by this component.
    /// </summary>
    private static string EncodeInstruction(NotesMediaAiTarget target, string instruction) =>
        Prefix + target + "] " + instruction.Trim();

    /// <summary>
    /// Builds model instruction from the currently available inputs.
    /// </summary>
    private static string BuildModelInstruction(NotesMediaAiTarget target, string instruction) =>
        $"Create only the proposed {DisplayName(target).ToLowerInvariant()} for this media block. "
        + "Do not claim to have seen or heard content that is not explicitly present in the supplied evidence. "
        + "Do not include labels, markdown, quotation marks or an explanation in proposedContent. "
        + instruction;

    /// <summary>
    /// Performs the default instruction step owned by this component.
    /// </summary>
    private static string DefaultInstruction(NotesMediaAiTarget target, NotesBlockKind kind) => target switch
    {
        NotesMediaAiTarget.AltText => kind == NotesBlockKind.Image
            ? "Write concise, useful accessibility text based only on the supplied evidence. State when visual details are unknown."
            : "Write concise accessibility text describing the media purpose from the supplied evidence.",
        NotesMediaAiTarget.Caption => "Write a concise caption grounded only in the supplied evidence.",
        NotesMediaAiTarget.Transcript => "Clean and structure the supplied transcript evidence without adding unsupplied speech or facts.",
        _ => throw new ArgumentOutOfRangeException(nameof(target))
    };

    /// <summary>
    /// Builds selected evidence from the currently available inputs.
    /// </summary>
    private static string BuildSelectedEvidence(
        NotesDocument document,
        NotesBlock block,
        NotesMediaAiTarget target,
        string original)
    {
        var media = block.Media!;
        var transform = NotesMediaTransformStore.Load(block);
        var nearby = document.Sections
            .SelectMany(section => section.Pages)
            .Where(page => page.Blocks.Any(value => value.Id == block.Id))
            .SelectMany(page => page.Blocks.OrderBy(value => value.Order))
            .Where(value => value.Id != block.Id)
            .Select(value => value.PlainText)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Take(8);
        return string.Join("\n", new[]
        {
            "Requested target: " + DisplayName(target),
            "Media kind: " + block.Kind,
            "File name: " + media.OriginalName,
            "Media type: " + media.MediaType,
            "Current alt text: " + media.AltText,
            "Current caption: " + media.Caption,
            "Current transcript: " + transform.Transcript,
            "Current target value: " + original,
            "Nearby note text: " + string.Join(" | ", nearby)
        });
    }

    /// <summary>
    /// Performs the read target step owned by this component.
    /// </summary>
    private static string ReadTarget(NotesBlock block, NotesMediaAiTarget target) => target switch
    {
        NotesMediaAiTarget.AltText => block.Media?.AltText ?? string.Empty,
        NotesMediaAiTarget.Caption => block.Media?.Caption ?? string.Empty,
        NotesMediaAiTarget.Transcript => NotesMediaTransformStore.Load(block).Transcript,
        _ => throw new ArgumentOutOfRangeException(nameof(target))
    };

    /// <summary>
    /// Performs the write target step owned by this component.
    /// </summary>
    private static void WriteTarget(NotesBlock block, NotesMediaAiTarget target, string value)
    {
        var media = block.Media ?? throw new InvalidOperationException("The media block no longer contains media metadata.");
        switch (target)
        {
            case NotesMediaAiTarget.AltText:
                media.AltText = value;
                break;
            case NotesMediaAiTarget.Caption:
                media.Caption = value;
                break;
            case NotesMediaAiTarget.Transcript:
                var transform = NotesMediaTransformStore.Load(block);
                transform.Transcript = value;
                NotesMediaTransformStore.Save(block, transform);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target));
        }
    }
}

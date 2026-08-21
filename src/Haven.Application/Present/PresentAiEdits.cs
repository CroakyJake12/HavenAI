using Haven.Core;

namespace Haven.Application;

public enum PresentEditOperationKind
{
    SetDocumentTitle = 0,
    SetSlideTitle = 1,
    SetSpeakerNotes = 2,
    SetElementText = 3,
    AddSlide = 4,
    AddText = 5,
    AddShape = 6,
    AddImage = 7,
    AddMedia = 8,
    RemoveElement = 9,
    AddCustomShape = 10
}

public sealed record PresentSelectionElement(
    Guid Id,
    PresentElementKind Kind,
    string Text,
    string AlternativeText,
    string ShapeType);

public sealed record PresentSelectionContext(
    Guid DocumentId,
    int DocumentVersion,
    DateTimeOffset DocumentUpdatedAt,
    Guid SlideId,
    string SlideTitle,
    string SpeakerNotes,
    IReadOnlyList<PresentSelectionElement> Elements);

public sealed record PresentEditOperation(
    PresentEditOperationKind Kind,
    Guid? SlideId = null,
    Guid? ElementId = null,
    string? Text = null,
    string? AssetId = null,
    string? ContentType = null,
    string? AlternativeText = null,
    DocumentVectorShape? VectorShape = null);

public sealed record PresentEditProposal(
    Guid Id,
    Guid DocumentId,
    int BaseVersion,
    DateTimeOffset BaseUpdatedAt,
    string Reason,
    PresentSelectionContext Selection,
    IReadOnlyList<PresentEditOperation> Operations);

public sealed record PresentEditApplyResult(
    Guid ProposalId,
    int AppliedOperations,
    PresentSelection Selection);

public static class PresentAiEdits
{
    public static PresentSelectionContext CaptureSelection(PresentEditor editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        var slide = editor.SelectedSlide;
        return new PresentSelectionContext(
            editor.Document.Id,
            editor.Document.Version,
            editor.Document.UpdatedAt,
            slide.Id,
            slide.Title,
            slide.SpeakerNotes,
            editor.SelectedElements.Select(element => new PresentSelectionElement(
                element.Id,
                element.Kind,
                element.Text,
                element.AlternativeText,
                element.ShapeType)).ToArray());
    }

    public static PresentEditProposal CreateProposal(
        PresentEditor editor, string reason, IEnumerable<PresentEditOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(operations);
        var context = CaptureSelection(editor);
        return new PresentEditProposal(
            Guid.NewGuid(),
            editor.Document.Id,
            editor.Document.Version,
            editor.Document.UpdatedAt,
            reason?.Trim() ?? string.Empty,
            context,
            operations.ToArray());
    }

    public static PresentEditApplyResult Apply(PresentEditor editor, PresentEditProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(proposal);
        if (proposal.DocumentId != editor.Document.Id)
            throw new InvalidOperationException("This edit proposal targets a different presentation.");
        if (proposal.BaseVersion != editor.Document.Version || proposal.BaseUpdatedAt != editor.Document.UpdatedAt)
            throw new InvalidOperationException("The presentation changed after this AI edit was proposed. Generate a fresh proposal before applying it.");

        var applied = 0;
        foreach (var operation in proposal.Operations)
        {
            if (ApplyOperation(editor, operation)) applied++;
        }
        return new PresentEditApplyResult(proposal.Id, applied, editor.Selection);
    }

    private static bool ApplyOperation(PresentEditor editor, PresentEditOperation operation)
    {
        var slideId = operation.SlideId ?? editor.Selection.SlideId;
        switch (operation.Kind)
        {
            case PresentEditOperationKind.SetDocumentTitle:
                return editor.SetDocumentTitle(operation.Text);
            case PresentEditOperationKind.SetSlideTitle:
                return editor.SetSlideTitle(slideId, operation.Text);
            case PresentEditOperationKind.SetSpeakerNotes:
                return editor.SetSpeakerNotes(slideId, operation.Text);
            case PresentEditOperationKind.SetElementText:
                if (operation.ElementId is not { } textElementId) throw Missing(nameof(operation.ElementId), operation.Kind);
                return editor.SetElementText(slideId, textElementId, operation.Text);
            case PresentEditOperationKind.AddSlide:
                editor.AddSlide(slideId);
                return true;
            case PresentEditOperationKind.AddText:
                editor.AddText(slideId, operation.Text);
                return true;
            case PresentEditOperationKind.AddShape:
                editor.AddShape(slideId, operation.ContentType ?? "rect");
                return true;
            case PresentEditOperationKind.AddImage:
                editor.AddImage(slideId, RequireAsset(operation), operation.AlternativeText);
                return true;
            case PresentEditOperationKind.AddMedia:
                editor.AddMedia(slideId, RequireAsset(operation), operation.ContentType ?? string.Empty, operation.AlternativeText);
                return true;
            case PresentEditOperationKind.RemoveElement:
                if (operation.ElementId is not { } removeId) throw Missing(nameof(operation.ElementId), operation.Kind);
                editor.SelectSlide(slideId);
                editor.SelectElements([removeId]);
                return editor.RemoveSelectedElements();
            case PresentEditOperationKind.AddCustomShape:
                if (operation.VectorShape is null) throw Missing(nameof(operation.VectorShape), operation.Kind);
                editor.AddCustomShape(slideId, DocumentVectorShapes.PrepareAiShape(operation.VectorShape));
                return true;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation.Kind), operation.Kind, "Unsupported Present edit operation.");
        }
    }

    private static string RequireAsset(PresentEditOperation operation) =>
        !string.IsNullOrWhiteSpace(operation.AssetId)
            ? operation.AssetId
            : throw Missing(nameof(operation.AssetId), operation.Kind);

    private static InvalidOperationException Missing(string field, PresentEditOperationKind kind) =>
        new($"Present edit operation '{kind}' requires {field}.");
}

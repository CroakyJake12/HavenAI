using Haven.Application;
using Haven.Desktop.Events;
using Haven.Desktop.Views.Pages.Notes;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Backward-compatible name for the production Notes workspace surface.
///
/// The Notes implementation lives in <see cref="NotesPage"/>. This subclass keeps the
/// existing desktop-test and integration contract without maintaining a second document,
/// autosave, undo/redo, AI-review, media, or accessibility state model.
/// </summary>
public sealed class NotesWorkspaceViewModel : NotesPage
{
    private readonly HavenEventBus _ownedEventBus;

    public NotesWorkspaceViewModel(
        INotesRepository repository,
        INotesImportExportService formats,
        INotesAiService ai,
        INotesAttachmentStore attachments,
        IOllamaClient models,
        IProductionDiagnostics diagnostics)
        : this(
            new HavenEventBus(),
            repository,
            formats,
            ai,
            attachments,
            models,
            diagnostics)
    {
    }

    private NotesWorkspaceViewModel(
        HavenEventBus eventBus,
        INotesRepository repository,
        INotesImportExportService formats,
        INotesAiService ai,
        INotesAttachmentStore attachments,
        IOllamaClient models,
        IProductionDiagnostics diagnostics)
        : base(eventBus, repository, formats, ai, attachments, models, diagnostics)
    {
        _ownedEventBus = eventBus;
    }

    public new void Dispose()
    {
        base.Dispose();
        _ownedEventBus.Dispose();
    }
}

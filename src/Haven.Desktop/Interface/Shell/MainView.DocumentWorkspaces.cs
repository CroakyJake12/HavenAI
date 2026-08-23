using Avalonia.Controls;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Canvas;
using Haven.Desktop.Views.Pages.Data;
using Haven.Desktop.Views.Pages.Present;
using Haven.Desktop.Views.Pages.Write;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private readonly Dictionary<string, Control> _documentWorkspaces = new(StringComparer.OrdinalIgnoreCase);

    private static bool IsDocumentWorkspace(string key) =>
        key.Equals("write", StringComparison.OrdinalIgnoreCase)
        || key.Equals("canvas", StringComparison.OrdinalIgnoreCase)
        || key.Equals("present", StringComparison.OrdinalIgnoreCase)
        || key.Equals("data", StringComparison.OrdinalIgnoreCase);

    private Control CreateDocumentWorkspace(string key)
    {
        var services = global::Haven.Desktop.App.Services
            ?? throw new InvalidOperationException("Haven services are unavailable.");
        return key.ToLowerInvariant() switch
        {
            "write" => new WritePage(_bus, services.GetRequiredService<INotesRepository>(),
                services.GetRequiredService<INotesImportExportService>(), services.GetService<INotesAttachmentStore>(),
                ai: services.GetService<INotesAiService>(), aiModels: services.GetService<IOllamaClient>()),
            "canvas" => new CanvasPage(_bus, services.GetRequiredService<INotesRepository>(),
                services.GetRequiredService<INotesImportExportService>(), services.GetRequiredService<UserPreferencesService>()),
            "present" => new PresentPage(_bus, services.GetRequiredService<IPresentRepository>(),
                services.GetRequiredService<IPresentExportService>(), services.GetRequiredService<IPresentImportService>()),
            "data" => new DataPage(_bus, services.GetRequiredService<IDataWorkbookRepository>(),
                services.GetRequiredService<IDataWorkbookFormatService>(), services.GetRequiredService<IDataWorkbookQueryService>()),
            _ => throw new InvalidOperationException($"{key} is not a direct document workspace.")
        };
    }

    private void OpenDocumentWorkspace(ModeDefinition mode, HavenSurface surface, bool forceNewTab)
    {
        Control page;
        var key = $"app-{mode.Key}";
        if (forceNewTab)
        {
            page = CreateDocumentWorkspace(mode.Key);
            key = $"app-{mode.Key}-{Guid.NewGuid():N}";
        }
        else if (!_documentWorkspaces.TryGetValue(mode.Key, out page!))
        {
            page = CreateDocumentWorkspace(mode.Key);
            _documentWorkspaces[mode.Key] = page;
        }

        AddOrSelectTab(key, mode.Name, page, forceNewTab, surface, forceNewTab);
        ApplyShellVisualState();
    }
}

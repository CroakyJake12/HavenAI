using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views;
using Haven.Infrastructure;

namespace Haven.Desktop.Tests;

public sealed class NotesAccessibilityTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [AvaloniaFact]
    public async Task NotesWorkspaceAndCriticalActionsExposeAutomationNames()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var viewModel = CreateViewModel(diagnostics);
        await viewModel.InitializeAsync(CancellationToken.None);
        var table = new NotesBlock
        {
            Kind = NotesBlockKind.Table,
            Table = NotesTableData.Create(2, 2)
        };
        var page = Assert.IsType<NotesPage>(viewModel.CurrentPage);
        page.Blocks.Clear();
        page.Blocks.Add(table);
        viewModel.SelectedBlock = table;
        var view = new NotesWorkspaceView(viewModel);
        var window = new Window { Width = 1500, Height = 900, Content = view };
        try
        {
            window.Show();
            await Task.Delay(25);

            Assert.Equal("Haven Notes document workspace", AutomationProperties.GetName(view));
            var criticalLabels = new HashSet<string>(StringComparer.Ordinal)
            {
                "Save", "Import", "Export", "Print", "Delete",
                "Sort ↑", "Sort ↓", "Sum", "Apply table data"
            };
            var criticalButtons = view.GetVisualDescendants()
                .OfType<Button>()
                .Where(button => button.Content is string label && criticalLabels.Contains(label))
                .ToArray();

            Assert.Equal(criticalLabels.Count, criticalButtons.Length);
            Assert.All(criticalButtons, button =>
            {
                Assert.True(button.Focusable);
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));
            });
            Assert.Contains(view.GetVisualDescendants().OfType<TabControl>(), control => control.Focusable);
        }
        finally
        {
            window.Close();
            view.Dispose();
        }
    }

    [AvaloniaFact]
    public void BlankSiblingRoutesHaveStableAccessibleIdentityAndNoFakeEditorControls()
    {
        foreach (var kind in Enum.GetValues<NotesExperienceKind>().Where(value => value != NotesExperienceKind.Notes))
        {
            var view = new BlankNotesExperienceView(kind);
            var window = new Window { Content = view };
            try
            {
                window.Show();
                Assert.True(view.Focusable);
                Assert.Equal(NotesExperienceNavigation.DisplayName(kind), AutomationProperties.GetName(view));
                Assert.Empty(view.GetVisualDescendants().OfType<TextBox>());
                Assert.Empty(view.GetVisualDescendants().OfType<Button>());
            }
            finally
            {
                window.Close();
            }
        }
    }

    public void Dispose() => _paths.Dispose();

    private NotesWorkspaceViewModel CreateViewModel(IProductionDiagnostics diagnostics)
    {
        var validator = new NotesDocumentValidator();
        var repository = new VerifiedNotesRepository(
            new NotesRepository(_paths, validator, diagnostics),
            _paths,
            diagnostics);
        var formats = new NotesImportExportService(validator, diagnostics);
        var attachments = new SecureNotesAttachmentStore(
            new NotesAttachmentStore(_paths, diagnostics),
            _paths);
        var model = new FakeModelClient();
        return new NotesWorkspaceViewModel(
            repository,
            formats,
            new NotesAiService(model, diagnostics),
            attachments,
            model,
            diagnostics);
    }

    private sealed class FakeModelClient : IOllamaClient
    {
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ModelDescriptor>>([]);
        public async IAsyncEnumerable<string> StreamChatAsync(
            OllamaChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken) =>
            Task.FromResult("{\"proposedContent\":\"unchanged\",\"explanation\":\"test\",\"citationIds\":[]}");
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaToolResponse(string.Empty, []));
        public Task PullModelAsync(string model, IProgress<double>? progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteModelAsync(string model, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-notes-accessibility-tests-" + Guid.NewGuid().ToString("N"));
            DatabasePath = Path.Combine(DataDirectory, "haven.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "missing.json");
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(LogsDirectory);
        }

        public string DataDirectory { get; }
        public string DatabasePath { get; }
        public string BrowserProfileDirectory { get; }
        public string AttachmentsDirectory { get; }
        public string LogsDirectory { get; }
        public string LegacyStatePath { get; }

        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

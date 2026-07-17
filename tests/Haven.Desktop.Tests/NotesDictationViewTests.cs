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

public sealed class NotesDictationViewTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [AvaloniaFact]
    public async Task SelectedParagraphShowsLocalOnePassageDictationWithoutResolvingServices()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var viewModel = CreateViewModel(diagnostics);
        await viewModel.InitializeAsync(CancellationToken.None);
        var paragraph = NotesBlock.Paragraph("Existing sentence.");
        var page = Assert.IsType<NotesPage>(viewModel.CurrentPage);
        page.Blocks.Clear();
        page.Blocks.Add(paragraph);
        viewModel.SelectedBlock = paragraph;
        var view = new NotesWorkspaceView(viewModel);
        var window = new Window { Width = 1500, Height = 900, Content = view };
        try
        {
            window.Show();
            await Task.Delay(25);
            var labels = view.GetVisualDescendants()
                .OfType<TextBlock>()
                .Select(text => text.Text ?? string.Empty)
                .ToArray();
            var buttons = view.GetVisualDescendants()
                .OfType<Button>()
                .Select(button => Convert.ToString(button.Content, System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();

            Assert.Contains("LOCAL DICTATION", labels);
            Assert.Contains("Dictate one passage", buttons);
            Assert.Contains("Stop dictation", buttons);
            Assert.Contains(labels, value => value.Contains("Raw microphone audio is discarded", StringComparison.Ordinal));
        }
        finally
        {
            window.Close();
            view.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task CodeAndEquationBlocksDoNotOfferAmbiguousDictationInsertion()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var viewModel = CreateViewModel(diagnostics);
        await viewModel.InitializeAsync(CancellationToken.None);
        var page = Assert.IsType<NotesPage>(viewModel.CurrentPage);
        foreach (var block in new[]
                 {
                     new NotesBlock { Kind = NotesBlockKind.Code, StyleId = "code", PlainText = "return 42;" },
                     NotesBlock.EquationBlock()
                 })
        {
            page.Blocks.Clear();
            page.Blocks.Add(block);
            viewModel.SelectedBlock = block;
            var view = new NotesWorkspaceView(viewModel);
            var window = new Window { Width = 1500, Height = 900, Content = view };
            try
            {
                window.Show();
                await Task.Delay(20);
                Assert.DoesNotContain(
                    view.GetVisualDescendants().OfType<Button>(),
                    button => Equals(button.Content, "Dictate one passage"));
            }
            finally
            {
                window.Close();
                view.Dispose();
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
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-notes-dictation-view-tests-" + Guid.NewGuid().ToString("N"));
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

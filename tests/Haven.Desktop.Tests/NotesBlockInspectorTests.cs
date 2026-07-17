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

public sealed class NotesBlockInspectorTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [AvaloniaFact]
    public async Task SelectedTableExposesRealSortSumAndDelimitedDataTools()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var viewModel = CreateViewModel(diagnostics);
        await viewModel.InitializeAsync(CancellationToken.None);
        var tableBlock = new NotesBlock
        {
            Kind = NotesBlockKind.Table,
            Table = NotesTableData.Create(3, 2)
        };
        tableBlock.Table.Rows[0].Cells[0].Text = "Name";
        tableBlock.Table.Rows[0].Cells[1].Text = "Score";
        tableBlock.Table.Rows[1].Cells[0].Text = "B";
        tableBlock.Table.Rows[1].Cells[1].Text = "2";
        tableBlock.Table.Rows[2].Cells[0].Text = "A";
        tableBlock.Table.Rows[2].Cells[1].Text = "3";
        viewModel.CurrentPage!.Blocks.Clear();
        viewModel.CurrentPage.Blocks.Add(tableBlock);
        viewModel.SelectedBlock = tableBlock;

        var view = new NotesWorkspaceView(viewModel);
        var window = new Window { Width = 1500, Height = 900, Content = view };
        try
        {
            window.Show();
            await Task.Delay(25);
            var tabHeaders = view.GetVisualDescendants()
                .OfType<TabItem>()
                .Select(item => Convert.ToString(item.Header, System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();
            var buttons = view.GetVisualDescendants()
                .OfType<Button>()
                .Select(button => Convert.ToString(button.Content, System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();
            var labels = view.GetVisualDescendants()
                .OfType<TextBlock>()
                .Select(block => block.Text ?? string.Empty)
                .ToArray();

            Assert.Contains("Block", tabHeaders);
            Assert.Contains("SELECTED BLOCK", labels);
            Assert.Contains("TABLE TOOLS", labels);
            Assert.Contains("Sort ↑", buttons);
            Assert.Contains("Sort ↓", buttons);
            Assert.Contains("Sum", buttons);
            Assert.Contains("Apply table data", buttons);
        }
        finally
        {
            window.Close();
            view.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task SelectedMediaToolsConstructWithoutResolvingDesktopServices()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var viewModel = CreateViewModel(diagnostics);
        await viewModel.InitializeAsync(CancellationToken.None);
        var mediaBlock = new NotesBlock
        {
            Kind = NotesBlockKind.Image,
            Media = new NotesMediaData
            {
                AttachmentId = Guid.NewGuid(),
                OriginalName = "diagram.png",
                StoredPath = Guid.NewGuid().ToString("N") + ".png",
                MediaType = "image/png",
                Sha256 = new string('a', 64),
                SizeBytes = 128
            }
        };
        viewModel.CurrentPage!.Blocks.Clear();
        viewModel.CurrentPage.Blocks.Add(mediaBlock);
        viewModel.SelectedBlock = mediaBlock;

        var view = new NotesWorkspaceView(viewModel);
        var window = new Window { Width = 1500, Height = 900, Content = view };
        try
        {
            window.Show();
            await Task.Delay(25);
            var buttons = view.GetVisualDescendants()
                .OfType<Button>()
                .Select(button => Convert.ToString(button.Content, System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();
            var labels = view.GetVisualDescendants()
                .OfType<TextBlock>()
                .Select(block => block.Text ?? string.Empty)
                .ToArray();

            Assert.Contains("MANAGED MEDIA", labels);
            Assert.Contains("Verify", buttons);
            Assert.Contains("Open", buttons);
            Assert.Contains("Replace", buttons);
            Assert.Contains("Save copy", buttons);
        }
        finally
        {
            window.Close();
            view.Dispose();
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
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-notes-block-inspector-tests-" + Guid.NewGuid().ToString("N"));
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

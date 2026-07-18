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

public sealed class NotesMediaAiViewTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [AvaloniaFact]
    public async Task SelectedMediaShowsEvidenceBoundProposalControlsAndReviewCard()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var model = new FakeModelClient();
        var workspace = CreateWorkspace(diagnostics, model);
        await workspace.InitializeAsync(CancellationToken.None);
        workspace.SelectedModelName = "ollama:qwen-test";
        var page = Assert.IsType<NotesPage>(workspace.CurrentPage);
        page.Blocks.Clear();
        page.Blocks.Add(NotesBlock.CreateParagraph("The note identifies this as a labelled plant-energy diagram."));
        var media = new NotesBlock
        {
            Kind = NotesBlockKind.Image,
            Order = 1,
            Media = new NotesMediaData
            {
                AttachmentId = Guid.NewGuid(),
                OriginalName = "plant-energy.png",
                StoredPath = Guid.NewGuid().ToString("N") + ".png",
                MediaType = "image/png",
                SizeBytes = 256,
                Sha256 = new string('a', 64),
                Width = 800,
                Height = 600
            }
        };
        page.Blocks.Add(media);
        workspace.SelectedBlock = media;
        var proposalService = new FakeNotesAiService();
        var proposal = await NotesMediaAiReview.ProposeAsync(
            proposalService,
            workspace,
            media,
            NotesMediaAiTarget.AltText,
            "Use one sentence.",
            CancellationToken.None);

        var view = new NotesWorkspaceView(workspace);
        var window = new Window { Width = 1500, Height = 900, Content = view };
        try
        {
            window.Show();
            await Task.Delay(30);
            var inspectorTabs = view.GetVisualDescendants()
                .OfType<TabControl>()
                .First(control => control.ItemCount == 5);
            inspectorTabs.SelectedIndex = 1;
            await Task.Delay(20);
            var labels = view.GetVisualDescendants()
                .OfType<TextBlock>()
                .Select(value => value.Text ?? string.Empty)
                .ToArray();
            var buttons = view.GetVisualDescendants()
                .OfType<Button>()
                .Select(value => Convert.ToString(value.Content, System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();
            var readOnlyValues = view.GetVisualDescendants()
                .OfType<TextBox>()
                .Where(value => value.IsReadOnly)
                .Select(value => value.Text ?? string.Empty)
                .ToArray();

            Assert.Contains("MEDIA ACCESSIBILITY AI", labels);
            Assert.Contains(labels, value => value.Contains("not to claim it saw or heard", StringComparison.Ordinal));
            Assert.Contains("Create media proposal", buttons);
            Assert.Contains("Approve media proposal", buttons);
            Assert.Contains("Reject media proposal", buttons);
            Assert.Contains("PROPOSED ALT TEXT", labels);
            Assert.Contains("A concise plant-energy diagram description.", readOnlyValues);
            Assert.Contains(labels, value => value.Contains("full document context not sent", StringComparison.Ordinal));
            Assert.Equal(NotesAiChangeStatus.Proposed, proposal.Status);
            Assert.Equal(string.Empty, media.Media.AltText);
        }
        finally
        {
            window.Close();
            view.Dispose();
        }
    }

    public void Dispose() => _paths.Dispose();

    private NotesWorkspaceViewModel CreateWorkspace(
        IProductionDiagnostics diagnostics,
        IOllamaClient model)
    {
        var validator = new NotesDocumentValidator();
        return new NotesWorkspaceViewModel(
            new VerifiedNotesRepository(new NotesRepository(_paths, validator, diagnostics), _paths, diagnostics),
            new NotesImportExportService(validator, diagnostics),
            new FakeNotesAiService(),
            new SecureNotesAttachmentStore(new NotesAttachmentStore(_paths, diagnostics), _paths),
            model,
            diagnostics);
    }

    private sealed class FakeNotesAiService : INotesAiService
    {
        public Task<NotesAiProposalResult> ProposeAsync(
            NotesAiProposalRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new NotesAiProposalResult(
                "A concise plant-energy diagram description.",
                "Grounded only in the supplied filename and nearby note text.",
                [],
                "ollama",
                request.ModelName));
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
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken) => Task.FromResult(string.Empty);
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaToolResponse(string.Empty, []));
        public Task PullModelAsync(string model, IProgress<double>? progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteModelAsync(string model, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-notes-media-ai-view-tests-" + Guid.NewGuid().ToString("N"));
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

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views;
using Haven.Infrastructure;

namespace Haven.Desktop.Tests;

public sealed class NotesWorkspaceTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [AvaloniaFact]
    public async Task NotesWorkspaceCreatesNativeDocumentAndExposesProductionTools()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var viewModel = CreateViewModel(diagnostics, ProposalResponse());
        var view = new NotesWorkspaceView(viewModel);
        var window = new Window { Width = 1500, Height = 900, Content = view };
        try
        {
            window.Show();
            await viewModel.InitializeAsync(CancellationToken.None);
            await Task.Delay(25);

            Assert.NotNull(viewModel.Document);
            Assert.Single(viewModel.Sections);
            Assert.Single(viewModel.Pages);
            Assert.NotEmpty(viewModel.Blocks);
            var labels = view.GetVisualDescendants()
                .OfType<TextBlock>()
                .Select(block => block.Text ?? string.Empty)
                .ToArray();
            Assert.Contains("HAVEN NOTES", labels);
            Assert.Contains("LIBRARY", labels);
            Assert.Contains("REVIEWED AI", labels);
            Assert.Contains("VERSION HISTORY", labels);
            Assert.Contains("DOCUMENT INFORMATION", labels);
            var buttons = view.GetVisualDescendants().OfType<Button>().Select(button => button.Content as string).ToArray();
            Assert.Contains("Import", buttons);
            Assert.Contains("Export", buttons);
            Assert.Contains("Print", buttons);
            Assert.Contains("Save", buttons);
        }
        finally
        {
            window.Close();
            view.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task EveryMixedContentEditorConstructsAgainstOneDocumentModel()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var viewModel = CreateViewModel(diagnostics, ProposalResponse());
        await viewModel.InitializeAsync(CancellationToken.None);
        var page = viewModel.CurrentPage!;
        page.Blocks.Clear();
        page.Blocks.Add(NotesBlock.Paragraph("Text"));
        page.Blocks.Add(new NotesBlock
        {
            Kind = NotesBlockKind.List,
            Order = 1,
            List = new NotesListData { Items = [new NotesListItem { Text = "Item" }] }
        });
        page.Blocks.Add(new NotesBlock { Kind = NotesBlockKind.Table, Order = 2, Table = NotesTableData.Create(2, 2) });
        page.Blocks.Add(new NotesBlock
        {
            Kind = NotesBlockKind.Image,
            Order = 3,
            Media = new NotesMediaData
            {
                AttachmentId = Guid.NewGuid(),
                OriginalName = "image.png",
                StoredPath = Guid.NewGuid().ToString("N") + ".png",
                MediaType = "image/png",
                Sha256 = new string('a', 64),
                SizeBytes = 10
            }
        });
        page.Blocks.Add(NotesBlock.EquationBlock("x^2"));
        page.Blocks[^1].Order = 4;
        page.Blocks.Add(NotesBlock.CanvasBlock());
        page.Blocks[^1].Order = 5;
        page.Blocks.Add(NotesBlock.FlashcardBlock("Question", "Answer"));
        page.Blocks[^1].Order = 6;

        foreach (var block in page.Blocks)
        {
            var editor = NotesBlockEditorFactory.Build(
                viewModel,
                block,
                viewModel.BeginBlockEdit,
                viewModel.CommitBlockEdit,
                () => { },
                () => Task.CompletedTask);
            var window = new Window { Content = editor };
            try
            {
                window.Show();
                Assert.NotEmpty(editor.GetVisualDescendants());
            }
            finally
            {
                window.Close();
            }
        }
        viewModel.Dispose();
    }

    [AvaloniaFact]
    public async Task UndoRedoAndReviewedAiProposalDoNotBypassApproval()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var viewModel = CreateViewModel(diagnostics, ProposalResponse());
        await viewModel.InitializeAsync(CancellationToken.None);
        var block = viewModel.SelectedBlock!;
        viewModel.BeginBlockEdit(block);
        viewModel.UpdateBlockText(block, "Changed locally");
        viewModel.CommitBlockEdit(block, "Changed locally");
        Assert.True(viewModel.UndoCommand.CanExecute(null));

        viewModel.UndoCommand.Execute(null);
        Assert.NotEqual("Changed locally", viewModel.SelectedBlock!.PlainText);
        viewModel.RedoCommand.Execute(null);
        Assert.Equal("Changed locally", viewModel.SelectedBlock!.PlainText);

        viewModel.SelectedModelName = "ollama:test";
        viewModel.AiInstruction = "Clarify this sentence";
        viewModel.AllowDocumentContext = false;
        await viewModel.ProposeAiCommand.ExecuteAsync();

        Assert.NotNull(viewModel.PendingAiChange);
        Assert.Equal(NotesAiChangeStatus.Proposed, viewModel.PendingAiChange!.Status);
        Assert.Equal("Changed locally", viewModel.SelectedBlock!.PlainText);
        viewModel.RejectAiCommand.Execute(null);
        Assert.Null(viewModel.PendingAiChange);
        Assert.Equal("Changed locally", viewModel.SelectedBlock!.PlainText);
        viewModel.Dispose();
    }

    [AvaloniaFact]
    public void PresentDataTasksAndImagineAreRoutedAccessibleAndIntentionallyBlank()
    {
        foreach (var kind in Enum.GetValues<NotesExperienceKind>().Where(kind => kind != NotesExperienceKind.Notes))
        {
            var view = new BlankNotesExperienceView(kind);
            var window = new Window { Content = view };
            try
            {
                window.Show();
                var text = view.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Select(block => block.Text ?? string.Empty)
                    .ToArray();
                Assert.Contains(NotesExperienceNavigation.DisplayName(kind), text);
                Assert.Contains(NotesExperienceNavigation.Description(kind), text);
                Assert.Empty(view.GetVisualDescendants().OfType<TextBox>());
                Assert.Empty(view.GetVisualDescendants().OfType<Button>());
                Assert.True(view.Focusable);
            }
            finally
            {
                window.Close();
            }
        }
    }

    [AvaloniaFact]
    public void EquationHtmlSandboxAndInkControlsExposeRealRuntimeState()
    {
        var equation = NotesEquationRenderer.Render("E = mc^{2} + \\alpha");
        Assert.Empty(equation.Error);
        Assert.Contains("²", equation.RenderedText, StringComparison.Ordinal);
        Assert.Contains("α", equation.RenderedText, StringComparison.Ordinal);

        var blocked = NotesHtmlSandbox.Build(new NotesHtmlData
        {
            HtmlSource = "<img src=\"https://example.test/image.png\">",
            AllowNetwork = false
        });
        Assert.NotEmpty(blocked.Error);
        Assert.Empty(blocked.DocumentHtml);

        var allowed = NotesHtmlSandbox.Build(new NotesHtmlData
        {
            HtmlSource = "<button id=\"x\">Ready</button>",
            JavaScriptSource = "document.getElementById('x').textContent='Done';",
            AllowScripts = true
        });
        Assert.Empty(allowed.Error);
        Assert.Contains("Content-Security-Policy", allowed.DocumentHtml, StringComparison.Ordinal);
        Assert.Contains("default-src 'none'", allowed.DocumentHtml, StringComparison.Ordinal);

        var canvas = new NotesInkCanvasControl
        {
            CanvasData = new NotesCanvasData
            {
                Strokes =
                [
                    new NotesInkStroke
                    {
                        Points =
                        [
                            new NotesInkPoint { X = 1, Y = 2, Pressure = 0.4, TiltX = 3, TiltY = 4 },
                            new NotesInkPoint { X = 10, Y = 20, Pressure = 0.8, TiltX = 6, TiltY = 8 }
                        ]
                    }
                ]
            }
        };
        var window = new Window { Content = canvas };
        try
        {
            window.Show();
            Assert.Equal(2, canvas.CanvasData.Strokes[0].Points.Count);
            Assert.Equal(0.8, canvas.CanvasData.Strokes[0].Points[1].Pressure);
        }
        finally
        {
            window.Close();
        }
    }

    public void Dispose() => _paths.Dispose();

    private NotesWorkspaceViewModel CreateViewModel(
        IProductionDiagnostics diagnostics,
        string response)
    {
        var validator = new NotesDocumentValidator();
        var inner = new NotesRepository(_paths, validator, diagnostics);
        var repository = new VerifiedNotesRepository(inner, _paths, diagnostics);
        var formats = new NotesImportExportService(validator, diagnostics);
        var attachments = new SecureNotesAttachmentStore(
            new NotesAttachmentStore(_paths, diagnostics),
            _paths);
        var model = new FakeModelClient(response);
        return new NotesWorkspaceViewModel(
            repository,
            formats,
            new NotesAiService(model, diagnostics),
            attachments,
            model,
            diagnostics);
    }

    private static string ProposalResponse() =>
        """
        {
          "proposedContent": "A clearer reviewed sentence.",
          "explanation": "Rewrites only the supplied text.",
          "citationIds": []
        }
        """;

    private sealed class FakeModelClient(string response) : IOllamaClient
    {
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ModelDescriptor>>(
                [new ModelDescriptor("ollama:test", 1, DateTimeOffset.UtcNow, "test", ToolCapability.None)]);
        public async IAsyncEnumerable<string> StreamChatAsync(
            OllamaChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(response);
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaToolResponse(string.Empty, []));
        public Task PullModelAsync(string model, IProgress<double>? progress, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task DeleteModelAsync(string model, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-notes-desktop-tests-" + Guid.NewGuid().ToString("N"));
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
            try { Directory.Delete(DataDirectory, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

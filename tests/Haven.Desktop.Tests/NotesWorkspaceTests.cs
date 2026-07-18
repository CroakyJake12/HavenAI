/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Desktop.Tests/NotesWorkspaceTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns NotesWorkspaceTests, FakeModelClient, TestPaths. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

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

/// <summary>
/// Represents notes workspace tests and keeps its related state and behavior together.
/// </summary>
public sealed class NotesWorkspaceTests : IDisposable
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TestPaths _paths = new();

    /// <summary>
    /// Performs the notes workspace creates native document and exposes production tools step owned by this component.
    /// </summary>
    [AvaloniaFact]
    public async Task NotesWorkspaceCreatesNativeDocumentAndExposesProductionTools()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var viewModel = CreateViewModel(diagnostics, ProposalResponse());
        await viewModel.InitializeAsync(CancellationToken.None);
        var view = new NotesWorkspaceView(viewModel);
        var window = new Window { Width = 1500, Height = 900, Content = view };
        try
        {
            window.Show();
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
            var buttons = view.GetVisualDescendants()
                .OfType<Button>()
                .Select(button => button.Content as string)
                .ToArray();
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

    /// <summary>
    /// Performs the every mixed content editor constructs against one document model step owned by this component.
    /// </summary>
    [AvaloniaFact]
    public async Task EveryMixedContentEditorConstructsAgainstOneDocumentModel()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var viewModel = CreateViewModel(diagnostics, ProposalResponse());
        await viewModel.InitializeAsync(CancellationToken.None);
        var page = viewModel.CurrentPage!;
        page.Blocks.Clear();
        page.Blocks.Add(NotesBlock.CreateParagraph("Text"));
        page.Blocks.Add(new NotesBlock
        {
            Kind = NotesBlockKind.List,
            Order = 1,
            List = new NotesListData { Items = [new NotesListItem { Text = "Item" }] }
        });
        page.Blocks.Add(new NotesBlock
        {
            Kind = NotesBlockKind.Table,
            Order = 2,
            Table = NotesTableData.Create(2, 2)
        });
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
        var equation = NotesBlock.EquationBlock();
        equation.Order = 4;
        equation.Equation!.Source = "x^2";
        page.Blocks.Add(equation);
        var canvas = NotesBlock.CanvasBlock();
        canvas.Order = 5;
        page.Blocks.Add(canvas);
        var flashcard = NotesBlock.FlashcardBlock();
        flashcard.Order = 6;
        flashcard.Flashcard!.Front = "Question";
        flashcard.Flashcard.Back = "Answer";
        page.Blocks.Add(flashcard);

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

    /// <summary>
    /// Performs the undo redo and reviewed ai proposal do not bypass approval step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the present data tasks and imagine are routed accessible and intentionally blank step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the equation html sandbox and ink controls expose real runtime state step owned by this component.
    /// </summary>
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
            Assert.Equal(2, canvas.CanvasData!.Strokes[0].Points.Count);
            Assert.Equal(0.8, canvas.CanvasData.Strokes[0].Points[1].Pressure);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose() => _paths.Dispose();

    /// <summary>
    /// Creates view model with the invariants required by its callers.
    /// </summary>
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

    /// <summary>
    /// Performs the proposal response step owned by this component.
    /// </summary>
    private static string ProposalResponse() =>
        """
        {
          "proposedContent": "A clearer reviewed sentence.",
          "explanation": "Rewrites only the supplied text.",
          "citationIds": []
        }
        """;

    /// <summary>
    /// Represents fake model client and keeps its related state and behavior together.
    /// </summary>
    private sealed class FakeModelClient(string response) : IOllamaClient
    {
        /// <summary>
        /// Reports whether available async applies to the current state.
        /// </summary>
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        /// <summary>
        /// Retrieves models async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ModelDescriptor>>([]);
        /// <summary>
        /// Performs stream chat asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public async IAsyncEnumerable<string> StreamChatAsync(
            OllamaChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
        /// <summary>
        /// Performs complete asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(response);
        /// <summary>
        /// Performs chat with tools asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaToolResponse(string.Empty, []));
        /// <summary>
        /// Performs pull model asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task PullModelAsync(string model, IProgress<double>? progress, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        /// <summary>
        /// Performs delete model asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task DeleteModelAsync(string model, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>
    /// Represents test paths and keeps its related state and behavior together.
    /// </summary>
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
        /// <summary>
        /// Gets or updates data directory, the bindable or domain state represented by this property.
        /// </summary>
        public string DataDirectory { get; }
        /// <summary>
        /// Gets or updates database path, the bindable or domain state represented by this property.
        /// </summary>
        public string DatabasePath { get; }
        /// <summary>
        /// Gets or updates browser profile directory, the bindable or domain state represented by this property.
        /// </summary>
        public string BrowserProfileDirectory { get; }
        /// <summary>
        /// Gets or updates attachments directory, the bindable or domain state represented by this property.
        /// </summary>
        public string AttachmentsDirectory { get; }
        /// <summary>
        /// Gets or updates logs directory, the bindable or domain state represented by this property.
        /// </summary>
        public string LogsDirectory { get; }
        /// <summary>
        /// Gets or updates legacy state path, the bindable or domain state represented by this property.
        /// </summary>
        public string LegacyStatePath { get; }
        /// <summary>
        /// Performs the dispose step owned by this component.
        /// </summary>
        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

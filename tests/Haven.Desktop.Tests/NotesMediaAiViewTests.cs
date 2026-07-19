/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Desktop.Tests/NotesMediaAiViewTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns NotesMediaAiViewTests, FakeNotesAiService, FakeModelClient, TestPaths. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views;
using Haven.Infrastructure;

namespace Haven.Desktop.Tests;

/// <summary>
/// Represents notes media ai view tests and keeps its related state and behavior together.
/// </summary>
public sealed class NotesMediaAiViewTests : IDisposable
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TestPaths _paths = new();

    /// <summary>
    /// Performs the selected media shows evidence bound proposal controls and review card step owned by this component.
    /// </summary>
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
            view.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Advanced tools"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose() => _paths.Dispose();

    /// <summary>
    /// Creates workspace with the invariants required by its callers.
    /// </summary>
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

    /// <summary>
    /// Represents fake notes ai service and keeps its related state and behavior together.
    /// </summary>
    private sealed class FakeNotesAiService : INotesAiService
    {
        /// <summary>
        /// Performs propose asynchronously so I/O does not block the caller's thread.
        /// </summary>
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

    /// <summary>
    /// Represents fake model client and keeps its related state and behavior together.
    /// </summary>
    private sealed class FakeModelClient : IOllamaClient
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
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken) => Task.FromResult(string.Empty);
        /// <summary>
        /// Performs chat with tools asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaToolResponse(string.Empty, []));
        /// <summary>
        /// Performs pull model asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task PullModelAsync(string model, IProgress<double>? progress, CancellationToken cancellationToken) => Task.CompletedTask;
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
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-notes-media-ai-view-tests-" + Guid.NewGuid().ToString("N"));
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
            try { Directory.Delete(DataDirectory, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

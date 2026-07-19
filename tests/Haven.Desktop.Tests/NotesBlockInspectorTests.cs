/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Desktop.Tests/NotesBlockInspectorTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns NotesBlockInspectorTests, FakeModelClient, TestPaths. Read the type and member comments below as a map of each responsibility.
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
/// Represents notes block inspector tests and keeps its related state and behavior together.
/// </summary>
public sealed class NotesBlockInspectorTests : IDisposable
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TestPaths _paths = new();

    /// <summary>
    /// Performs the selected table exposes real sort sum and delimited data tools step owned by this component.
    /// </summary>
    [AvaloniaFact]
    public async Task SelectedTableExposesRealSortSumAndDelimitedDataTools()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var viewModel = CreateViewModel(diagnostics);
        await viewModel.InitializeAsync(CancellationToken.None);
        var table = NotesTableData.Create(3, 2);
        table.Rows[0].Cells[0].Text = "Name";
        table.Rows[0].Cells[1].Text = "Score";
        table.Rows[1].Cells[0].Text = "B";
        table.Rows[1].Cells[1].Text = "2";
        table.Rows[2].Cells[0].Text = "A";
        table.Rows[2].Cells[1].Text = "3";
        var tableBlock = new NotesBlock
        {
            Kind = NotesBlockKind.Table,
            Table = table
        };
        var page = Assert.IsType<NotesPage>(viewModel.CurrentPage);
        page.Blocks.Clear();
        page.Blocks.Add(tableBlock);
        viewModel.SelectedBlock = tableBlock;

        var view = new NotesWorkspaceView(viewModel);
        var window = new Window { Width = 1500, Height = 900, Content = view };
        try
        {
            window.Show();
            view.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Advanced tools"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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

    /// <summary>
    /// Performs the selected media tools construct without resolving desktop services step owned by this component.
    /// </summary>
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
        var page = Assert.IsType<NotesPage>(viewModel.CurrentPage);
        page.Blocks.Clear();
        page.Blocks.Add(mediaBlock);
        viewModel.SelectedBlock = mediaBlock;

        var view = new NotesWorkspaceView(viewModel);
        var window = new Window { Width = 1500, Height = 900, Content = view };
        try
        {
            window.Show();
            view.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Advanced tools"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose() => _paths.Dispose();

    /// <summary>
    /// Creates view model with the invariants required by its callers.
    /// </summary>
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
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken) =>
            Task.FromResult("{\"proposedContent\":\"unchanged\",\"explanation\":\"test\",\"citationIds\":[]}");
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
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-notes-block-inspector-tests-" + Guid.NewGuid().ToString("N"));
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

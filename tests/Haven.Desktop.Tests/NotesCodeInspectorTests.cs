/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Desktop.Tests/NotesCodeInspectorTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns NotesCodeInspectorTests, FakeModelClient, TestPaths. Read the type and member comments below as a map of each responsibility.
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
/// Represents notes code inspector tests and keeps its related state and behavior together.
/// </summary>
public sealed class NotesCodeInspectorTests : IDisposable
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TestPaths _paths = new();

    /// <summary>
    /// Performs the code inspector persists settings and normalizes tabs step owned by this component.
    /// </summary>
    [AvaloniaFact]
    public async Task CodeInspectorPersistsSettingsAndNormalizesTabs()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var viewModel = CreateViewModel(diagnostics);
        await viewModel.InitializeAsync(CancellationToken.None);
        var code = new NotesBlock
        {
            Kind = NotesBlockKind.Code,
            StyleId = "code",
            PlainText = "if (ready)\n{\n\tRun();\n}",
            Runs =
            [
                new NotesTextRun
                {
                    Text = "if (ready)\n{\n\tRun();\n}",
                    FontFamily = "Cascadia Mono"
                }
            ]
        };
        var page = Assert.IsType<NotesPage>(viewModel.CurrentPage);
        page.Blocks.Clear();
        page.Blocks.Add(code);
        viewModel.SelectedBlock = code;
        var view = new NotesWorkspaceView(viewModel);
        var window = new Window { Width = 1500, Height = 900, Content = view };
        try
        {
            window.Show();
            view.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Advanced tools"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(25);
            var labels = view.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text ?? string.Empty).ToArray();
            var buttons = view.GetVisualDescendants().OfType<Button>().ToArray();
            var language = view.GetVisualDescendants()
                .OfType<ComboBox>()
                .First(combo => combo.ItemsSource?.Cast<object?>().Any(item => Equals(item, "C#")) == true);
            var tabSize = view.GetVisualDescendants()
                .OfType<NumericUpDown>()
                .First(control => control.Minimum == 1 && control.Maximum == 16);

            Assert.Contains("CODE TOOLS", labels);
            Assert.Contains(buttons, button => Equals(button.Content, "Normalize indentation"));
            language.SelectedItem = "C#";
            tabSize.Value = 2;
            await Task.Delay(10);
            var normalize = buttons.Single(button => Equals(button.Content, "Normalize indentation"));
            normalize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(10);

            Assert.Equal("C#", code.Metadata["haven.notes.code.language"]);
            Assert.Equal("2", code.Metadata["haven.notes.code.tab-size"]);
            Assert.DoesNotContain('\t', code.PlainText);
            Assert.Contains("  Run();", code.PlainText, StringComparison.Ordinal);
            Assert.True(viewModel.IsDirty);
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
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-notes-code-inspector-tests-" + Guid.NewGuid().ToString("N"));
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

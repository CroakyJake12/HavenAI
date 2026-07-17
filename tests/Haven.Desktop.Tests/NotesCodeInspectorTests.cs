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

public sealed class NotesCodeInspectorTests : IDisposable
{
    private readonly TestPaths _paths = new();

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
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-notes-code-inspector-tests-" + Guid.NewGuid().ToString("N"));
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

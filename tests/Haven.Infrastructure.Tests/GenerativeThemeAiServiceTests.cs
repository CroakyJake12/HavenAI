/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/GenerativeThemeAiServiceTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns GenerativeThemeAiServiceTests, FakeOllamaClient, TestPaths. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Xunit;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents generative theme ai service tests and keeps its related state and behavior together.
/// </summary>
public sealed class GenerativeThemeAiServiceTests : IDisposable
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TestPaths _paths = new();

    /// <summary>
    /// Performs the valid model proposal becomes previewable but is not persisted step owned by this component.
    /// </summary>
    [Fact]
    public async Task ValidModelProposalBecomesPreviewableButIsNotPersisted()
    {
        var proposed = ValidTheme() with
        {
            Id = Guid.Empty,
            IsBuiltIn = true,
            Origin = GenerativeThemeOrigin.BuiltIn,
            Layout = new GenerativeLayoutManifest(
            [
                new("chat.temporary", GenerativeUiCatalog.ChatComposerCenter, 10),
                new("chat.model", GenerativeUiCatalog.ChatComposerRight, 20),
                new("chat.effort", GenerativeUiCatalog.ChatComposerRight, 30, false),
                new("chat.context", GenerativeUiCatalog.ShellHeaderRight, 5)
            ],
            []),
            Pages =
            [
                new GeneratedPageDefinition(
                    "focus",
                    "Focus",
                    "A focus page.",
                    "clock",
                    1,
                    [new GeneratedWidgetDefinition(
                        "timer",
                        GeneratedWidgetKind.Timer,
                        "Timer",
                        null,
                        null,
                        1500,
                        [])])
            ]
        };
        var response = JsonSerializer.Serialize(new
        {
            theme = proposed,
            summary = "Moved context to the header and added a timer.",
            changes = new[] { "Context moved to header", "Timer page added" },
            safetyNotes = Array.Empty<string>()
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var service = new GenerativeThemeAiService(
            new FakeOllamaClient(response),
            new GenerativeThemeValidator(),
            diagnostics);

        var proposal = await service.CreateAsync(
            "Move context to the header and add a focus timer.",
            "local-theme-model",
            null,
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, proposal.Theme.Id);
        Assert.False(proposal.Theme.IsBuiltIn);
        Assert.Equal(GenerativeThemeOrigin.AiGenerated, proposal.Theme.Origin);
        Assert.Equal(
            GenerativeUiCatalog.ShellHeaderRight,
            proposal.Theme.Layout.Placements.Single(item => item.ItemId == "chat.context").Region);
        Assert.Single(proposal.Theme.Pages);
        Assert.Contains("timer", proposal.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(_paths.DataDirectory, "GenerativeUi", "Themes")));
    }

    /// <summary>
    /// Performs the arbitrary model control is rejected and audited step owned by this component.
    /// </summary>
    [Fact]
    public async Task ArbitraryModelControlIsRejectedAndAudited()
    {
        var unsafeTheme = ValidTheme() with
        {
            Layout = new GenerativeLayoutManifest(
                [new("shell.execute-arbitrary-xaml", GenerativeUiCatalog.ShellHeaderRight, 1)],
                [])
        };
        var response = JsonSerializer.Serialize(new
        {
            theme = unsafeTheme,
            summary = "Unsafe",
            changes = Array.Empty<string>(),
            safetyNotes = Array.Empty<string>()
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var service = new GenerativeThemeAiService(
            new FakeOllamaClient(response),
            new GenerativeThemeValidator(),
            diagnostics);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.CreateAsync(
            "Add arbitrary XAML.",
            "local-theme-model",
            null,
            CancellationToken.None));

        var events = await diagnostics.ReadRecentAsync(20, CancellationToken.None);
        Assert.Contains(events, item => item.EventName == "ai-proposal-rejected");
    }

    /// <summary>
    /// Performs the malformed model json is rejected without persistence step owned by this component.
    /// </summary>
    [Fact]
    public async Task MalformedModelJsonIsRejectedWithoutPersistence()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var service = new GenerativeThemeAiService(
            new FakeOllamaClient("Here is your theme: definitely-not-json"),
            new GenerativeThemeValidator(),
            diagnostics);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.CreateAsync(
            "Create a theme.",
            "local-theme-model",
            null,
            CancellationToken.None));

        Assert.False(Directory.Exists(Path.Combine(_paths.DataDirectory, "GenerativeUi")));
    }

    /// <summary>
    /// Performs the safe mode blocks theme model before calling provider step owned by this component.
    /// </summary>
    [Fact]
    public async Task SafeModeBlocksThemeModelBeforeCallingProvider()
    {
        var fake = new FakeOllamaClient("unused");
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var service = new GenerativeThemeAiService(fake, new GenerativeThemeValidator(), diagnostics);
        RuntimeSafetyState.EnableSafeMode("test recovery");
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
                "Create a theme.",
                "local-theme-model",
                null,
                CancellationToken.None));
            Assert.Equal(0, fake.CompleteCalls);
        }
        finally
        {
            RuntimeSafetyState.DisableSafeMode();
        }
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose()
    {
        RuntimeSafetyState.DisableSafeMode();
        _paths.Dispose();
    }

    /// <summary>
    /// Performs the valid theme step owned by this component.
    /// </summary>
    private static GenerativeThemePack ValidTheme()
    {
        var now = DateTimeOffset.UtcNow;
        return new GenerativeThemePack(
            1,
            Guid.NewGuid(),
            "AI theme",
            "A complete model proposal.",
            "Haven Studio",
            GenerativeThemeOrigin.AiGenerated,
            false,
            now,
            now,
            Palette(light: true, "#FFF8FAFD", "#FF087F8C"),
            Palette(light: false, "#FF0D161B", "#FF087F8C"),
            new GenerativeThemeTypography("Segoe UI", 14, 1.35, 0),
            new GenerativeThemeShape(10, 14, 16, 1, true, true),
            GenerativeUiCatalog.DefaultLayout,
            []);
    }

    /// <summary>
    /// Performs the palette step owned by this component.
    /// </summary>
    private static GenerativeThemePalette Palette(bool light, string background, string accent) => new(
        background,
        light ? "#FFF7F9FC" : "#FF182129",
        light ? "#FFFFFFFF" : "#FF202A33",
        light ? "#FFF1F4F8" : "#FF26323C",
        light ? "#FFE8EDF3" : "#FF2C3A46",
        light ? "#FFE1E7EE" : "#FF344552",
        light ? "#FF16191E" : "#FFFFFFFF",
        light ? "#FF343A44" : "#FFD9E2EA",
        light ? "#FF5F6875" : "#FFA8B5C1",
        light ? "#FF7D8795" : "#FF748391",
        accent,
        "#FFFFFFFF",
        light ? "#FFDCEEFF" : "#FF203A45",
        light ? "#FF0067B8" : "#FF60CDFF",
        light ? "#FFD8ECFA" : "#FF1E3A50",
        "#FFFF99A4",
        "#FFFCE4A6",
        light ? "#24000000" : "#22FFFFFF",
        light ? "#3D000000" : "#44FFFFFF",
        accent,
        light ? "#FFF4F6F9" : "#FF182234",
        light ? "#FFF4F6F9" : "#F2182234",
        light ? "#E6FFFFFF" : "#2EFFFFFF",
        light ? "#FFFFFFFF" : "#46FFFFFF",
        light ? "#FFE7EBF0" : "#20FFFFFF",
        "#A060CDFF");

    /// <summary>
    /// Represents fake ollama client and keeps its related state and behavior together.
    /// </summary>
    private sealed class FakeOllamaClient(string response) : IOllamaClient
    {
        /// <summary>
        /// Gets or updates complete calls, the bindable or domain state represented by this property.
        /// </summary>
        public int CompleteCalls { get; private set; }

        /// <summary>
        /// Reports whether available async applies to the current state.
        /// </summary>
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
            Task.FromResult(true);

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
        public Task<string> CompleteAsync(
            OllamaChatRequest request,
            CancellationToken cancellationToken)
        {
            CompleteCalls++;
            return Task.FromResult(response);
        }

        /// <summary>
        /// Performs chat with tools asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<OllamaToolResponse> ChatWithToolsAsync(
            OllamaToolRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaToolResponse(string.Empty, []));

        /// <summary>
        /// Performs pull model asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task PullModelAsync(
            string model,
            IProgress<double>? progress,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        /// <summary>
        /// Performs delete model asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task DeleteModelAsync(string model, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    /// <summary>
    /// Represents test paths and keeps its related state and behavior together.
    /// </summary>
    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(
                Path.GetTempPath(),
                "haven-generative-ai-tests-" + Guid.NewGuid().ToString("N"));
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
            try
            {
                Directory.Delete(DataDirectory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}

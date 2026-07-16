using Haven.Core;

namespace Haven.Application;

public interface IGenerativeThemeValidator
{
    GenerativeThemeValidationResult Validate(GenerativeThemePack theme);
}

public interface IGenerativeThemeStore
{
    Task<IReadOnlyList<GenerativeThemePack>> GetThemesAsync(CancellationToken cancellationToken);
    Task<GenerativeThemeSelection> GetSelectionAsync(CancellationToken cancellationToken);
    Task<GenerativeThemePack> GetActiveThemeAsync(CancellationToken cancellationToken);
    Task SaveAsync(GenerativeThemePack theme, CancellationToken cancellationToken);
    Task RenameAsync(Guid themeId, string name, CancellationToken cancellationToken);
    Task DeleteAsync(Guid themeId, CancellationToken cancellationToken);
    Task SelectAsync(Guid themeId, GenerativeThemeAppearance appearance, CancellationToken cancellationToken);
    Task SetAppearanceAsync(GenerativeThemeAppearance appearance, CancellationToken cancellationToken);
    Task<string> ExportAsync(Guid themeId, string destinationDirectory, CancellationToken cancellationToken);
    Task<GenerativeThemePack> ImportAsync(string sourcePath, CancellationToken cancellationToken);
}

public interface IGenerativeThemeAiService
{
    Task<GenerativeThemeProposal> CreateAsync(
        string prompt,
        string modelName,
        GenerativeThemePack? startingTheme,
        CancellationToken cancellationToken);
}

public interface IGenerativeUiRuntime
{
    GenerativeThemePack ActiveTheme { get; }
    GenerativeThemeAppearance Appearance { get; }
    event EventHandler? ThemeChanged;
    Task InitializeAsync(CancellationToken cancellationToken);
    Task ApplyAsync(Guid themeId, GenerativeThemeAppearance appearance, CancellationToken cancellationToken);
    Task PreviewAsync(GenerativeThemePack theme, GenerativeThemeAppearance appearance, CancellationToken cancellationToken);
    Task RevertPreviewAsync(CancellationToken cancellationToken);
    IReadOnlyList<GenerativeUiPlacement> GetPlacements(string region);
    IReadOnlyList<GeneratedPageDefinition> GetPages();
}

public static class GenerativeUiCatalog
{
    public const string ShellHeaderRight = "shell.header.right";
    public const string ChatComposerCenter = "chat.composer.center";
    public const string ChatComposerRight = "chat.composer.right";

    public static IReadOnlyList<GenerativeUiCatalogItem> Items { get; } =
    [
        new(
            "chat.temporary",
            "Temporary chat",
            "Toggle whether the current conversation is retained in history.",
            [ChatComposerCenter, ChatComposerRight, ShellHeaderRight],
            CanHide: true,
            CanMove: true,
            DefaultRegion: ChatComposerCenter,
            DefaultOrder: 10),
        new(
            "chat.model",
            "Model selector",
            "Open the current conversation's model picker.",
            [ChatComposerRight, ShellHeaderRight],
            CanHide: false,
            CanMove: true,
            DefaultRegion: ChatComposerRight,
            DefaultOrder: 20),
        new(
            "chat.effort",
            "Effort selector",
            "Choose the reasoning effort used by the current conversation.",
            [ChatComposerRight, ShellHeaderRight],
            CanHide: true,
            CanMove: true,
            DefaultRegion: ChatComposerRight,
            DefaultOrder: 30),
        new(
            "chat.context",
            "Context usage",
            "Show token-context usage and retain the existing Compact now action.",
            [ChatComposerRight, ShellHeaderRight],
            CanHide: true,
            CanMove: true,
            DefaultRegion: ChatComposerRight,
            DefaultOrder: 40)
    ];

    public static IReadOnlyList<GeneratedCommandDescriptor> PageCommands { get; } =
    [
        new("home", "Haven Home", "Open the main dashboard.", "home"),
        new("new-chat", "New chat", "Start a clean general conversation.", "plus"),
        new("chat", "Haven Chat", "Open general chat.", "chat"),
        new("teach", "Teach", "Open the Teach surface.", "book"),
        new("call", "Call", "Open the live Call surface.", "call"),
        new("do", "Do", "Open the agentic Do surface.", "tasks"),
        new("studio", "Studio", "Open Haven Studio.", "code"),
        new("browse", "Browse", "Open Haven Browse.", "globe"),
        new("plan", "Plan", "Open Haven Plan.", "calendar"),
        new("automations", "Scheduled Actions", "Open local automations.", "clock"),
        new("settings", "Settings", "Open Haven Settings.", "settings")
    ];

    public static GenerativeLayoutManifest DefaultLayout => new(
        Items.Select(item => new GenerativeUiPlacement(
            item.Id,
            item.DefaultRegion,
            item.DefaultOrder,
            IsVisible: true,
            Presentation: "default")).ToArray(),
        []);
}

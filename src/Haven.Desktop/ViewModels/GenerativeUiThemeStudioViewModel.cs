using System.Collections.ObjectModel;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

public sealed class GenerativeUiThemeStudioViewModel : ObservableObject
{
    private readonly IGenerativeThemeStore _store;
    private readonly IGenerativeUiRuntime _runtime;
    private readonly IGenerativeThemeAiService _ai;
    private readonly IGenerativeThemeValidator _validator;
    private readonly IOllamaClient _models;
    private GenerativeThemeAppearance _selectedAppearance;
    private ModelDescriptor? _selectedModel;
    private string _aiPrompt = string.Empty;
    private string _status = string.Empty;
    private string _proposalSummary = string.Empty;
    private bool _isBusy;
    private bool _isStudioOpen;
    private int _studioTabIndex;
    private GenerativeThemePack? _draftTheme;
    private ThemeCardViewModel? _renameCandidate;
    private ThemeCardViewModel? _deleteCandidate;
    private string _renameText = string.Empty;
    private string _draftName = string.Empty;
    private string _draftDescription = string.Empty;
    private string _draftAuthor = "Created with Haven Studio";
    private string _draftFontFamily = "Segoe UI Variable, Segoe UI, Montserrat, sans-serif";
    private double _draftBaseFontSize = 14;
    private double _draftHeadingScale = 1.35;
    private double _draftLetterSpacing;
    private double _draftControlRadius = 10;
    private double _draftCardRadius = 14;
    private double _draftSurfaceRadius = 16;
    private double _draftSpacingScale = 1;
    private bool _draftShowCardBorders;
    private bool _draftUseAcrylic = true;

    public GenerativeUiThemeStudioViewModel(
        IGenerativeThemeStore store,
        IGenerativeUiRuntime runtime,
        IGenerativeThemeAiService ai,
        IGenerativeThemeValidator validator,
        IOllamaClient models)
    {
        _store = store;
        _runtime = runtime;
        _ai = ai;
        _validator = validator;
        _models = models;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        CreateWithStudioCommand = new RelayCommand(CreateNewTheme);
        GenerateWithAiCommand = new AsyncRelayCommand(GenerateWithAiAsync, () => !IsBusy && SelectedModel is not null && !string.IsNullOrWhiteSpace(AiPrompt));
        PreviewManualCommand = new AsyncRelayCommand(PreviewManualAsync, () => !IsBusy && IsStudioOpen);
        SaveAndApplyCommand = new AsyncRelayCommand(SaveAndApplyAsync, () => !IsBusy && IsStudioOpen);
        CancelStudioCommand = new AsyncRelayCommand(CancelStudioAsync, () => !IsBusy && IsStudioOpen);
        ConfirmRenameCommand = new AsyncRelayCommand(ConfirmRenameAsync, () => RenameCandidate is not null && !string.IsNullOrWhiteSpace(RenameText));
        CancelRenameCommand = new RelayCommand(CancelRename);
        ConfirmDeleteCommand = new AsyncRelayCommand(ConfirmDeleteAsync, () => DeleteCandidate is not null && !IsBusy);
        CancelDeleteCommand = new RelayCommand(CancelDelete);
        AddTimerPageCommand = new RelayCommand(AddTimerPage, () => IsStudioOpen);
        AddShortcutPageCommand = new RelayCommand(AddShortcutPage, () => IsStudioOpen);
        RemovePageCommand = new RelayCommand<GeneratedPageEditorViewModel>(RemovePage);
        ImportRequestedCommand = new RelayCommand(() => ImportRequested?.Invoke(this, EventArgs.Empty));
        LightPalette = new ThemePaletteEditorViewModel();
        DarkPalette = new ThemePaletteEditorViewModel();
    }

    public ObservableCollection<ThemeCardViewModel> Themes { get; } = [];
    public ObservableCollection<ModelDescriptor> Models { get; } = [];
    public ObservableCollection<ThemePlacementEditorViewModel> Placements { get; } = [];
    public ObservableCollection<GeneratedPageEditorViewModel> Pages { get; } = [];
    public IReadOnlyList<GenerativeThemeAppearance> Appearances { get; } = Enum.GetValues<GenerativeThemeAppearance>();
    public IReadOnlyList<string> Presentations { get; } = ["default", "compact", "labelled", "icon"];
    public ThemePaletteEditorViewModel LightPalette { get; }
    public ThemePaletteEditorViewModel DarkPalette { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand CreateWithStudioCommand { get; }
    public AsyncRelayCommand GenerateWithAiCommand { get; }
    public AsyncRelayCommand PreviewManualCommand { get; }
    public AsyncRelayCommand SaveAndApplyCommand { get; }
    public AsyncRelayCommand CancelStudioCommand { get; }
    public AsyncRelayCommand ConfirmRenameCommand { get; }
    public RelayCommand CancelRenameCommand { get; }
    public AsyncRelayCommand ConfirmDeleteCommand { get; }
    public RelayCommand CancelDeleteCommand { get; }
    public RelayCommand AddTimerPageCommand { get; }
    public RelayCommand AddShortcutPageCommand { get; }
    public RelayCommand<GeneratedPageEditorViewModel> RemovePageCommand { get; }
    public RelayCommand ImportRequestedCommand { get; }
    public event EventHandler<ThemeExportRequestedEventArgs>? ExportRequested;
    public event EventHandler? ImportRequested;

    public GenerativeThemeAppearance SelectedAppearance
    {
        get => _selectedAppearance;
        set
        {
            if (!SetProperty(ref _selectedAppearance, value)) return;
            _ = ApplyAppearanceAsync(value);
        }
    }
    public ModelDescriptor? SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (!SetProperty(ref _selectedModel, value)) return;
            GenerateWithAiCommand.RaiseCanExecuteChanged();
        }
    }
    public string AiPrompt
    {
        get => _aiPrompt;
        set
        {
            if (!SetProperty(ref _aiPrompt, value)) return;
            GenerateWithAiCommand.RaiseCanExecuteChanged();
        }
    }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string ProposalSummary { get => _proposalSummary; private set => SetProperty(ref _proposalSummary, value); }
    public ObservableCollection<string> ProposalChanges { get; } = [];
    public ObservableCollection<string> SafetyNotes { get; } = [];
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            RaiseCommandStates();
        }
    }
    public bool IsStudioOpen
    {
        get => _isStudioOpen;
        private set
        {
            if (!SetProperty(ref _isStudioOpen, value)) return;
            RaiseCommandStates();
        }
    }
    public int StudioTabIndex { get => _studioTabIndex; set => SetProperty(ref _studioTabIndex, value); }
    public bool IsRenaming => RenameCandidate is not null;
    public ThemeCardViewModel? RenameCandidate
    {
        get => _renameCandidate;
        private set
        {
            if (!SetProperty(ref _renameCandidate, value)) return;
            RaisePropertyChanged(nameof(IsRenaming));
            ConfirmRenameCommand.RaiseCanExecuteChanged();
        }
    }
    public string RenameText
    {
        get => _renameText;
        set
        {
            if (!SetProperty(ref _renameText, value)) return;
            ConfirmRenameCommand.RaiseCanExecuteChanged();
        }
    }
    public bool IsDeleteConfirming => DeleteCandidate is not null;
    public ThemeCardViewModel? DeleteCandidate
    {
        get => _deleteCandidate;
        private set
        {
            if (!SetProperty(ref _deleteCandidate, value)) return;
            RaisePropertyChanged(nameof(IsDeleteConfirming));
            ConfirmDeleteCommand.RaiseCanExecuteChanged();
        }
    }

    public string DraftName { get => _draftName; set => SetProperty(ref _draftName, value); }
    public string DraftDescription { get => _draftDescription; set => SetProperty(ref _draftDescription, value); }
    public string DraftAuthor { get => _draftAuthor; set => SetProperty(ref _draftAuthor, value); }
    public string DraftFontFamily { get => _draftFontFamily; set => SetProperty(ref _draftFontFamily, value); }
    public double DraftBaseFontSize { get => _draftBaseFontSize; set => SetProperty(ref _draftBaseFontSize, value); }
    public double DraftHeadingScale { get => _draftHeadingScale; set => SetProperty(ref _draftHeadingScale, value); }
    public double DraftLetterSpacing { get => _draftLetterSpacing; set => SetProperty(ref _draftLetterSpacing, value); }
    public double DraftControlRadius { get => _draftControlRadius; set => SetProperty(ref _draftControlRadius, value); }
    public double DraftCardRadius { get => _draftCardRadius; set => SetProperty(ref _draftCardRadius, value); }
    public double DraftSurfaceRadius { get => _draftSurfaceRadius; set => SetProperty(ref _draftSurfaceRadius, value); }
    public double DraftSpacingScale { get => _draftSpacingScale; set => SetProperty(ref _draftSpacingScale, value); }
    public bool DraftShowCardBorders { get => _draftShowCardBorders; set => SetProperty(ref _draftShowCardBorders, value); }
    public bool DraftUseAcrylic { get => _draftUseAcrylic; set => SetProperty(ref _draftUseAcrylic, value); }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (!_runtime.ActiveTheme?.Equals(null) ?? false)
        {
            // The runtime is usually initialized by App before Settings opens.
        }
        await RefreshAsync(cancellationToken);
    }

    public async Task ImportAsync(string sourcePath, CancellationToken cancellationToken)
    {
        try
        {
            IsBusy = true;
            var imported = await _store.ImportAsync(sourcePath, cancellationToken);
            Status = $"Imported {imported.Name}. Review and apply it from the theme list.";
            await RefreshAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Status = "Theme import failed: " + ex.Message;
            throw;
        }
        finally { IsBusy = false; }
    }

    public async Task<string> ExportAsync(Guid themeId, string destinationDirectory, CancellationToken cancellationToken)
    {
        try
        {
            IsBusy = true;
            var path = await _store.ExportAsync(themeId, destinationDirectory, cancellationToken);
            Status = "Theme shared as " + path;
            return path;
        }
        finally { IsBusy = false; }
    }

    private Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            IsBusy = true;
            var themes = await _store.GetThemesAsync(cancellationToken);
            var selection = await _store.GetSelectionAsync(cancellationToken);
            Themes.Clear();
            foreach (var theme in themes)
            {
                Themes.Add(new ThemeCardViewModel(
                    theme,
                    theme.Id == selection.ActiveThemeId,
                    ApplyThemeAsync,
                    OpenEditStudio,
                    DuplicateTheme,
                    BeginRename,
                    BeginDelete,
                    RequestExport));
            }
            _selectedAppearance = selection.Appearance;
            RaisePropertyChanged(nameof(SelectedAppearance));

            var installed = await _models.GetModelsAsync(cancellationToken);
            var previousModel = SelectedModel?.Name;
            Models.Clear();
            foreach (var model in installed.OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase)) Models.Add(model);
            SelectedModel = Models.FirstOrDefault(model => model.Name.Equals(previousModel, StringComparison.OrdinalIgnoreCase)) ?? Models.FirstOrDefault();
            Status = Models.Count == 0
                ? "Theme library loaded. Install or connect a model to use Create with AI; manual Studio remains available."
                : "Theme library loaded.";
        }
        catch (Exception ex)
        {
            Status = "Theme library failed to load: " + ex.Message;
        }
        finally { IsBusy = false; }
    }

    private async Task ApplyThemeAsync(ThemeCardViewModel card)
    {
        try
        {
            IsBusy = true;
            await _runtime.ApplyAsync(card.Theme.Id, SelectedAppearance, CancellationToken.None);
            await RefreshAsync(CancellationToken.None);
            Status = $"Applied {card.Theme.Name} ({SelectedAppearance}).";
        }
        catch (Exception ex) { Status = "Could not apply theme: " + ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task ApplyAppearanceAsync(GenerativeThemeAppearance appearance)
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            await _runtime.ApplyAsync(_runtime.ActiveTheme.Id, appearance, CancellationToken.None);
            Status = "Global appearance switched to " + appearance + ".";
            foreach (var card in Themes) card.IsActive = card.Theme.Id == _runtime.ActiveTheme.Id;
        }
        catch (Exception ex) { Status = "Could not switch appearance: " + ex.Message; }
        finally { IsBusy = false; }
    }

    private void CreateNewTheme()
    {
        var source = _runtime.ActiveTheme;
        SetDraft(source with
        {
            Id = Guid.NewGuid(),
            Name = source.Name + " remix",
            Description = "A custom theme created with Haven Studio.",
            Author = "Created with Haven Studio",
            Origin = GenerativeThemeOrigin.Manual,
            IsBuiltIn = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        StudioTabIndex = 0;
        IsStudioOpen = true;
        AiPrompt = "Create a polished Haven theme. Keep every important control functional and include both light and dark palettes.";
        Status = "Theme Studio opened. Generate with AI or configure the validated manifest manually.";
    }

    private void OpenEditStudio(ThemeCardViewModel card)
    {
        SetDraft(card.Theme.IsBuiltIn
            ? card.Theme with
            {
                Id = Guid.NewGuid(),
                Name = card.Theme.Name + " copy",
                IsBuiltIn = false,
                Origin = GenerativeThemeOrigin.Manual,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
            : card.Theme);
        StudioTabIndex = 1;
        IsStudioOpen = true;
        Status = card.Theme.IsBuiltIn ? "Built-in themes are immutable, so Studio created an editable copy." : "Editing custom theme.";
    }

    private void DuplicateTheme(ThemeCardViewModel card)
    {
        SetDraft(card.Theme with
        {
            Id = Guid.NewGuid(),
            Name = card.Theme.Name + " copy",
            IsBuiltIn = false,
            Origin = GenerativeThemeOrigin.Manual,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        StudioTabIndex = 1;
        IsStudioOpen = true;
        Status = "Editable theme copy created. Save and apply after review.";
    }

    private void BeginRename(ThemeCardViewModel card)
    {
        if (card.Theme.IsBuiltIn) return;
        RenameCandidate = card;
        RenameText = card.Theme.Name;
    }

    private async Task ConfirmRenameAsync()
    {
        if (RenameCandidate is null) return;
        try
        {
            IsBusy = true;
            await _store.RenameAsync(RenameCandidate.Theme.Id, RenameText, CancellationToken.None);
            RenameCandidate = null;
            await RefreshAsync(CancellationToken.None);
            Status = "Theme renamed.";
        }
        catch (Exception ex) { Status = "Rename failed: " + ex.Message; }
        finally { IsBusy = false; }
    }

    private void CancelRename()
    {
        RenameCandidate = null;
        RenameText = string.Empty;
    }

    private void BeginDelete(ThemeCardViewModel card)
    {
        if (!card.Theme.IsBuiltIn) DeleteCandidate = card;
    }

    private async Task ConfirmDeleteAsync()
    {
        if (DeleteCandidate is null) return;
        try
        {
            IsBusy = true;
            var name = DeleteCandidate.Theme.Name;
            await _store.DeleteAsync(DeleteCandidate.Theme.Id, CancellationToken.None);
            DeleteCandidate = null;
            await _runtime.RevertPreviewAsync(CancellationToken.None);
            await RefreshAsync(CancellationToken.None);
            Status = "Deleted " + name + ".";
        }
        catch (Exception ex) { Status = "Delete failed: " + ex.Message; }
        finally { IsBusy = false; }
    }

    private void CancelDelete() => DeleteCandidate = null;

    private void RequestExport(ThemeCardViewModel card) =>
        ExportRequested?.Invoke(this, new ThemeExportRequestedEventArgs(card.Theme.Id, card.Theme.Name));

    private async Task GenerateWithAiAsync()
    {
        if (SelectedModel is null) return;
        try
        {
            IsBusy = true;
            Status = "Theme Studio is generating a complete validated proposal…";
            var starting = TryBuildDraft(out var draftIssues) ? BuildDraft() : _draftTheme;
            if (draftIssues.Count > 0) SafetyNotes.ReplaceWith(draftIssues);
            var proposal = await _ai.CreateAsync(AiPrompt, SelectedModel.Name, starting, CancellationToken.None);
            SetDraft(proposal.Theme);
            ProposalSummary = proposal.Summary;
            ProposalChanges.ReplaceWith(proposal.Changes);
            SafetyNotes.ReplaceWith(proposal.SafetyNotes);
            await _runtime.PreviewAsync(proposal.Theme, SelectedAppearance, CancellationToken.None);
            Status = "AI proposal is previewing. Review both variants, layout and pages before Save and apply.";
        }
        catch (Exception ex)
        {
            Status = "Theme generation failed: " + ex.Message;
        }
        finally { IsBusy = false; }
    }

    private async Task PreviewManualAsync()
    {
        try
        {
            IsBusy = true;
            var draft = BuildDraft();
            var validation = _validator.Validate(draft);
            SafetyNotes.ReplaceWith(validation.Issues.Select(issue => (issue.IsError ? "Error: " : "Note: ") + issue.Path + " — " + issue.Message));
            if (!validation.IsValid || validation.NormalizedTheme is null)
            {
                Status = "Manual preview blocked. Fix the validation errors listed below.";
                return;
            }
            _draftTheme = validation.NormalizedTheme;
            await _runtime.PreviewAsync(validation.NormalizedTheme, SelectedAppearance, CancellationToken.None);
            Status = "Manual theme preview is live. No changes are saved yet.";
        }
        catch (Exception ex) { Status = "Manual preview failed: " + ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task SaveAndApplyAsync()
    {
        try
        {
            IsBusy = true;
            var validation = _validator.Validate(BuildDraft());
            SafetyNotes.ReplaceWith(validation.Issues.Select(issue => (issue.IsError ? "Error: " : "Note: ") + issue.Path + " — " + issue.Message));
            if (!validation.IsValid || validation.NormalizedTheme is null)
            {
                Status = "Theme was not saved because validation failed.";
                return;
            }
            await _store.SaveAsync(validation.NormalizedTheme, CancellationToken.None);
            await _runtime.ApplyAsync(validation.NormalizedTheme.Id, SelectedAppearance, CancellationToken.None);
            IsStudioOpen = false;
            _draftTheme = null;
            await RefreshAsync(CancellationToken.None);
            Status = "Theme saved and applied. All moved controls retain their original commands and state.";
        }
        catch (Exception ex) { Status = "Save failed: " + ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task CancelStudioAsync()
    {
        try
        {
            IsBusy = true;
            await _runtime.RevertPreviewAsync(CancellationToken.None);
            IsStudioOpen = false;
            _draftTheme = null;
            ProposalSummary = string.Empty;
            ProposalChanges.Clear();
            SafetyNotes.Clear();
            Status = "Theme Studio changes discarded.";
        }
        finally { IsBusy = false; }
    }

    private void AddTimerPage()
    {
        var pageNumber = Pages.Count + 1;
        Pages.Add(new GeneratedPageEditorViewModel(new GeneratedPageDefinition(
            "timer-" + Guid.NewGuid().ToString("N")[..8],
            "Focus timer " + pageNumber,
            "A local timer page created by Theme Studio.",
            "clock",
            Pages.Count * 10,
            [new GeneratedWidgetDefinition("timer", GeneratedWidgetKind.Timer, "Focus session", "Stay focused until the timer completes.", null, 25 * 60, [])])));
        Status = "Added a functional timer page to the draft.";
    }

    private void AddShortcutPage()
    {
        Pages.Add(new GeneratedPageEditorViewModel(new GeneratedPageDefinition(
            "shortcuts-" + Guid.NewGuid().ToString("N")[..8],
            "Haven shortcuts",
            "Quick access to frequently used Haven surfaces.",
            "grid",
            Pages.Count * 10,
            [new GeneratedWidgetDefinition("shortcuts", GeneratedWidgetKind.ShortcutGrid, "Shortcuts", null, null, 0, ["new-chat", "studio", "browse", "plan", "settings"])])));
        Status = "Added a functional shortcut page to the draft.";
    }

    private void RemovePage(GeneratedPageEditorViewModel? page)
    {
        if (page is not null) Pages.Remove(page);
    }

    private void SetDraft(GenerativeThemePack theme)
    {
        _draftTheme = theme;
        DraftName = theme.Name;
        DraftDescription = theme.Description;
        DraftAuthor = theme.Author;
        DraftFontFamily = theme.Typography.FontFamily;
        DraftBaseFontSize = theme.Typography.BaseFontSize;
        DraftHeadingScale = theme.Typography.HeadingScale;
        DraftLetterSpacing = theme.Typography.LetterSpacing;
        DraftControlRadius = theme.Shape.ControlRadius;
        DraftCardRadius = theme.Shape.CardRadius;
        DraftSurfaceRadius = theme.Shape.SurfaceRadius;
        DraftSpacingScale = theme.Shape.SpacingScale;
        DraftShowCardBorders = theme.Shape.ShowCardBorders;
        DraftUseAcrylic = theme.Shape.UseAcrylic;
        LightPalette.Load(theme.Light);
        DarkPalette.Load(theme.Dark);
        Placements.Clear();
        foreach (var item in GenerativeUiCatalog.Items)
        {
            var placement = theme.Layout.Placements.FirstOrDefault(value => value.ItemId.Equals(item.Id, StringComparison.OrdinalIgnoreCase))
                            ?? new GenerativeUiPlacement(item.Id, item.DefaultRegion, item.DefaultOrder, !item.CanHide, "default");
            Placements.Add(new ThemePlacementEditorViewModel(item, placement));
        }
        Pages.Clear();
        foreach (var page in theme.Pages) Pages.Add(new GeneratedPageEditorViewModel(page));
    }

    private bool TryBuildDraft(out IReadOnlyList<string> issues)
    {
        try
        {
            var validation = _validator.Validate(BuildDraft());
            issues = validation.Issues.Select(issue => issue.Path + ": " + issue.Message).ToArray();
            return validation.IsValid;
        }
        catch (Exception ex)
        {
            issues = [ex.Message];
            return false;
        }
    }

    private GenerativeThemePack BuildDraft()
    {
        var source = _draftTheme ?? _runtime.ActiveTheme with
        {
            Id = Guid.NewGuid(),
            IsBuiltIn = false,
            Origin = GenerativeThemeOrigin.Manual,
            CreatedAt = DateTimeOffset.UtcNow
        };
        return source with
        {
            SchemaVersion = 1,
            Name = DraftName,
            Description = DraftDescription,
            Author = DraftAuthor,
            IsBuiltIn = false,
            Origin = source.Origin == GenerativeThemeOrigin.AiGenerated ? source.Origin : GenerativeThemeOrigin.Manual,
            UpdatedAt = DateTimeOffset.UtcNow,
            Light = LightPalette.ToPalette(),
            Dark = DarkPalette.ToPalette(),
            Typography = new GenerativeThemeTypography(DraftFontFamily, DraftBaseFontSize, DraftHeadingScale, DraftLetterSpacing),
            Shape = new GenerativeThemeShape(DraftControlRadius, DraftCardRadius, DraftSurfaceRadius, DraftSpacingScale, DraftShowCardBorders, DraftUseAcrylic),
            Layout = new GenerativeLayoutManifest(Placements.Select(placement => placement.ToPlacement()).ToArray(), []),
            Pages = Pages.Select(page => page.ToDefinition()).ToArray()
        };
    }

    private void RaiseCommandStates()
    {
        RefreshCommand.RaiseCanExecuteChanged();
        GenerateWithAiCommand.RaiseCanExecuteChanged();
        PreviewManualCommand.RaiseCanExecuteChanged();
        SaveAndApplyCommand.RaiseCanExecuteChanged();
        CancelStudioCommand.RaiseCanExecuteChanged();
        ConfirmDeleteCommand.RaiseCanExecuteChanged();
    }
}

public sealed class ThemeCardViewModel : ObservableObject
{
    private bool _isActive;

    public ThemeCardViewModel(
        GenerativeThemePack theme,
        bool isActive,
        Func<ThemeCardViewModel, Task> apply,
        Action<ThemeCardViewModel> edit,
        Action<ThemeCardViewModel> duplicate,
        Action<ThemeCardViewModel> rename,
        Action<ThemeCardViewModel> delete,
        Action<ThemeCardViewModel> export)
    {
        Theme = theme;
        _isActive = isActive;
        ApplyCommand = new AsyncRelayCommand(() => apply(this));
        EditCommand = new RelayCommand(() => edit(this));
        DuplicateCommand = new RelayCommand(() => duplicate(this));
        RenameCommand = new RelayCommand(() => rename(this), () => !Theme.IsBuiltIn);
        DeleteCommand = new RelayCommand(() => delete(this), () => !Theme.IsBuiltIn);
        ExportCommand = new RelayCommand(() => export(this));
    }

    public GenerativeThemePack Theme { get; }
    public string Name => Theme.Name;
    public string Description => Theme.Description;
    public string OriginLabel => Theme.IsBuiltIn ? "Built in" : Theme.Origin.ToString();
    public string PagesLabel => Theme.Pages.Count == 0 ? "No generated pages" : $"{Theme.Pages.Count} generated page{(Theme.Pages.Count == 1 ? string.Empty : "s")}";
    public string LightPreview => Theme.Light.Background;
    public string DarkPreview => Theme.Dark.Background;
    public string AccentPreview => Theme.Dark.Accent;
    public bool IsCustom => !Theme.IsBuiltIn;
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }
    public AsyncRelayCommand ApplyCommand { get; }
    public RelayCommand EditCommand { get; }
    public RelayCommand DuplicateCommand { get; }
    public RelayCommand RenameCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand ExportCommand { get; }
}

public sealed class ThemePlacementEditorViewModel : ObservableObject
{
    private string _region;
    private int _order;
    private bool _isVisible;
    private string _presentation;

    public ThemePlacementEditorViewModel(GenerativeUiCatalogItem item, GenerativeUiPlacement placement)
    {
        Item = item;
        _region = placement.Region;
        _order = placement.Order;
        _isVisible = placement.IsVisible;
        _presentation = placement.Presentation;
    }

    public GenerativeUiCatalogItem Item { get; }
    public string DisplayName => Item.DisplayName;
    public string Description => Item.Description;
    public IReadOnlyList<string> AllowedRegions => Item.AllowedRegions;
    public bool CanHide => Item.CanHide;
    public string Region { get => _region; set => SetProperty(ref _region, value); }
    public int Order { get => _order; set => SetProperty(ref _order, value); }
    public bool IsVisible { get => _isVisible; set => SetProperty(ref _isVisible, value); }
    public string Presentation { get => _presentation; set => SetProperty(ref _presentation, value); }
    public GenerativeUiPlacement ToPlacement() => new(Item.Id, Region, Order, IsVisible, Presentation);
}

public sealed class GeneratedPageEditorViewModel : ObservableObject
{
    private string _title;
    private string _description;
    private string _iconKey;
    private int _order;

    public GeneratedPageEditorViewModel(GeneratedPageDefinition definition)
    {
        Definition = definition;
        _title = definition.Title;
        _description = definition.Description;
        _iconKey = definition.IconKey;
        _order = definition.Order;
    }

    public GeneratedPageDefinition Definition { get; }
    public string Id => Definition.Id;
    public string Title { get => _title; set => SetProperty(ref _title, value); }
    public string Description { get => _description; set => SetProperty(ref _description, value); }
    public string IconKey { get => _iconKey; set => SetProperty(ref _iconKey, value); }
    public int Order { get => _order; set => SetProperty(ref _order, value); }
    public string WidgetSummary => string.Join(", ", Definition.Widgets.Select(widget => widget.Kind.ToString()));
    public GeneratedPageDefinition ToDefinition() => Definition with { Title = Title, Description = Description, IconKey = IconKey, Order = Order };
}

public sealed class ThemePaletteEditorViewModel : ObservableObject
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public string Background { get => Get(nameof(Background)); set => Set(nameof(Background), value); }
    public string Elevated { get => Get(nameof(Elevated)); set => Set(nameof(Elevated), value); }
    public string Panel { get => Get(nameof(Panel)); set => Set(nameof(Panel), value); }
    public string Panel2 { get => Get(nameof(Panel2)); set => Set(nameof(Panel2), value); }
    public string Panel3 { get => Get(nameof(Panel3)); set => Set(nameof(Panel3), value); }
    public string PanelHover { get => Get(nameof(PanelHover)); set => Set(nameof(PanelHover), value); }
    public string Text { get => Get(nameof(Text)); set => Set(nameof(Text), value); }
    public string TextSoft { get => Get(nameof(TextSoft)); set => Set(nameof(TextSoft), value); }
    public string Muted { get => Get(nameof(Muted)); set => Set(nameof(Muted), value); }
    public string Muted2 { get => Get(nameof(Muted2)); set => Set(nameof(Muted2), value); }
    public string Accent { get => Get(nameof(Accent)); set => Set(nameof(Accent), value); }
    public string AccentInk { get => Get(nameof(AccentInk)); set => Set(nameof(AccentInk), value); }
    public string AccentSoft { get => Get(nameof(AccentSoft)); set => Set(nameof(AccentSoft), value); }
    public string Blue { get => Get(nameof(Blue)); set => Set(nameof(Blue), value); }
    public string BlueSoft { get => Get(nameof(BlueSoft)); set => Set(nameof(BlueSoft), value); }
    public string Danger { get => Get(nameof(Danger)); set => Set(nameof(Danger), value); }
    public string Warning { get => Get(nameof(Warning)); set => Set(nameof(Warning), value); }
    public string Line { get => Get(nameof(Line)); set => Set(nameof(Line), value); }
    public string LineStrong { get => Get(nameof(LineStrong)); set => Set(nameof(LineStrong), value); }
    public string Nub { get => Get(nameof(Nub)); set => Set(nameof(Nub), value); }
    public string AcrylicTint { get => Get(nameof(AcrylicTint)); set => Set(nameof(AcrylicTint), value); }
    public string AcrylicFallback { get => Get(nameof(AcrylicFallback)); set => Set(nameof(AcrylicFallback), value); }
    public string Button { get => Get(nameof(Button)); set => Set(nameof(Button), value); }
    public string ButtonHover { get => Get(nameof(ButtonHover)); set => Set(nameof(ButtonHover), value); }
    public string ButtonPressed { get => Get(nameof(ButtonPressed)); set => Set(nameof(ButtonPressed), value); }
    public string Focus { get => Get(nameof(Focus)); set => Set(nameof(Focus), value); }

    public void Load(GenerativeThemePalette palette)
    {
        _values[nameof(Background)] = palette.Background;
        _values[nameof(Elevated)] = palette.Elevated;
        _values[nameof(Panel)] = palette.Panel;
        _values[nameof(Panel2)] = palette.Panel2;
        _values[nameof(Panel3)] = palette.Panel3;
        _values[nameof(PanelHover)] = palette.PanelHover;
        _values[nameof(Text)] = palette.Text;
        _values[nameof(TextSoft)] = palette.TextSoft;
        _values[nameof(Muted)] = palette.Muted;
        _values[nameof(Muted2)] = palette.Muted2;
        _values[nameof(Accent)] = palette.Accent;
        _values[nameof(AccentInk)] = palette.AccentInk;
        _values[nameof(AccentSoft)] = palette.AccentSoft;
        _values[nameof(Blue)] = palette.Blue;
        _values[nameof(BlueSoft)] = palette.BlueSoft;
        _values[nameof(Danger)] = palette.Danger;
        _values[nameof(Warning)] = palette.Warning;
        _values[nameof(Line)] = palette.Line;
        _values[nameof(LineStrong)] = palette.LineStrong;
        _values[nameof(Nub)] = palette.Nub;
        _values[nameof(AcrylicTint)] = palette.AcrylicTint;
        _values[nameof(AcrylicFallback)] = palette.AcrylicFallback;
        _values[nameof(Button)] = palette.Button;
        _values[nameof(ButtonHover)] = palette.ButtonHover;
        _values[nameof(ButtonPressed)] = palette.ButtonPressed;
        _values[nameof(Focus)] = palette.Focus;
        foreach (var key in _values.Keys.ToArray()) RaisePropertyChanged(key);
    }

    public GenerativeThemePalette ToPalette() => new(
        Background, Elevated, Panel, Panel2, Panel3, PanelHover, Text, TextSoft, Muted, Muted2,
        Accent, AccentInk, AccentSoft, Blue, BlueSoft, Danger, Warning, Line, LineStrong, Nub,
        AcrylicTint, AcrylicFallback, Button, ButtonHover, ButtonPressed, Focus);

    private string Get(string key) => _values.TryGetValue(key, out var value) ? value : "#FF000000";
    private void Set(string key, string value)
    {
        if (_values.TryGetValue(key, out var current) && current == value) return;
        _values[key] = value;
        RaisePropertyChanged(key);
    }
}

public sealed record ThemeExportRequestedEventArgs(Guid ThemeId, string ThemeName);

internal static class ObservableCollectionExtensions
{
    public static void ReplaceWith<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items) collection.Add(item);
    }
}

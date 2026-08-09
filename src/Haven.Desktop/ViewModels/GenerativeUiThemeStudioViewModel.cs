/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/GenerativeUiThemeStudioViewModel.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns GenerativeUiThemeStudioViewModel, ThemeCardViewModel, ThemePlacementEditorViewModel, GeneratedPageEditorViewModel, ThemePaletteEditorViewModel, ThemeExportRequestedEventArgs, ObservableCollectionExtensions. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Keeping UI state here makes the XAML declarative and keeps behavior testable without recreating the full window.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.ObjectModel;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Represents generative ui theme studio view model and keeps its related state and behavior together.
/// </summary>
public sealed class GenerativeUiThemeStudioViewModel : ObservableObject
{
    /// <summary>
    /// Stores store locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IGenerativeThemeStore _store;
    /// <summary>
    /// Stores runtime locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IGenerativeUiRuntime _runtime;
    /// <summary>
    /// Stores ai locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IGenerativeThemeAiService _ai;
    /// <summary>
    /// Stores validator locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IGenerativeThemeValidator _validator;
    /// <summary>
    /// Stores models locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IOllamaClient _models;
    /// <summary>
    /// Stores selected appearance locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private GenerativeThemeAppearance _selectedAppearance;
    /// <summary>
    /// Stores selected model locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private ModelDescriptor? _selectedModel;
    /// <summary>
    /// Stores ai prompt locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _aiPrompt = string.Empty;
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _status = string.Empty;
    /// <summary>
    /// Stores proposal summary locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _proposalSummary = string.Empty;
    /// <summary>
    /// Stores is busy locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isBusy;
    /// <summary>
    /// Stores is studio open locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isStudioOpen;
    /// <summary>
    /// Stores studio tab index locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _studioTabIndex;
    /// <summary>
    /// Stores draft theme locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private GenerativeThemePack? _draftTheme;
    /// <summary>
    /// Stores rename candidate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private ThemeCardViewModel? _renameCandidate;
    /// <summary>
    /// Stores delete candidate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private ThemeCardViewModel? _deleteCandidate;
    /// <summary>
    /// Stores rename text locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _renameText = string.Empty;
    /// <summary>
    /// Stores draft name locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _draftName = string.Empty;
    /// <summary>
    /// Stores draft description locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _draftDescription = string.Empty;
    /// <summary>
    /// Stores draft author locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _draftAuthor = "Created with Haven Studio";
    /// <summary>
    /// Stores draft font family locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _draftFontFamily = "Montserrat";
    /// <summary>
    /// Stores draft base font size locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private double _draftBaseFontSize = 14;
    /// <summary>
    /// Stores draft heading scale locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private double _draftHeadingScale = 1.35;
    /// <summary>
    /// Stores draft letter spacing locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private double _draftLetterSpacing;
    /// <summary>
    /// Stores draft control radius locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private double _draftControlRadius = 10;
    /// <summary>
    /// Stores draft card radius locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private double _draftCardRadius = 14;
    /// <summary>
    /// Stores draft surface radius locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private double _draftSurfaceRadius = 16;
    /// <summary>
    /// Stores draft spacing scale locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private double _draftSpacingScale = 1;
    /// <summary>
    /// Stores draft show card borders locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _draftShowCardBorders;
    /// <summary>
    /// Stores draft use acrylic locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
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

    /// <summary>
    /// Gets or updates themes, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<ThemeCardViewModel> Themes { get; } = [];
    /// <summary>
    /// Gets or updates models, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<ModelDescriptor> Models { get; } = [];
    /// <summary>
    /// Gets or updates placements, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<ThemePlacementEditorViewModel> Placements { get; } = [];
    /// <summary>
    /// Gets or updates pages, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<GeneratedPageEditorViewModel> Pages { get; } = [];
    /// <summary>
    /// Gets or updates appearances, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<GenerativeThemeAppearance> Appearances { get; } = Enum.GetValues<GenerativeThemeAppearance>();
    /// <summary>
    /// Gets or updates presentations, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<string> Presentations { get; } = ["default", "compact", "labelled", "icon"];
    /// <summary>
    /// Gets or updates light palette, the bindable or domain state represented by this property.
    /// </summary>
    public ThemePaletteEditorViewModel LightPalette { get; }
    /// <summary>
    /// Gets or updates dark palette, the bindable or domain state represented by this property.
    /// </summary>
    public ThemePaletteEditorViewModel DarkPalette { get; }
    /// <summary>
    /// Gets or updates refresh command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand RefreshCommand { get; }
    /// <summary>
    /// Creates with studio command with the invariants required by its callers.
    /// </summary>
    public RelayCommand CreateWithStudioCommand { get; }
    /// <summary>
    /// Gets or updates generate with ai command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand GenerateWithAiCommand { get; }
    /// <summary>
    /// Gets or updates preview manual command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand PreviewManualCommand { get; }
    /// <summary>
    /// Gets or updates save and apply command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand SaveAndApplyCommand { get; }
    /// <summary>
    /// Reports whether cancel studio command is true for the current state.
    /// </summary>
    public AsyncRelayCommand CancelStudioCommand { get; }
    /// <summary>
    /// Gets or updates confirm rename command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand ConfirmRenameCommand { get; }
    /// <summary>
    /// Reports whether cancel rename command is true for the current state.
    /// </summary>
    public RelayCommand CancelRenameCommand { get; }
    /// <summary>
    /// Gets or updates confirm delete command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand ConfirmDeleteCommand { get; }
    /// <summary>
    /// Reports whether cancel delete command is true for the current state.
    /// </summary>
    public RelayCommand CancelDeleteCommand { get; }
    /// <summary>
    /// Gets or updates add timer page command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand AddTimerPageCommand { get; }
    /// <summary>
    /// Gets or updates add shortcut page command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand AddShortcutPageCommand { get; }
    /// <summary>
    /// Gets or updates remove page command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<GeneratedPageEditorViewModel> RemovePageCommand { get; }
    /// <summary>
    /// Gets or updates import requested command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand ImportRequestedCommand { get; }
    /// <summary>
    /// Stores export requested locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event EventHandler<ThemeExportRequestedEventArgs>? ExportRequested;
    /// <summary>
    /// Stores import requested locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
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
    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>
    /// Gets or updates proposal summary, the bindable or domain state represented by this property.
    /// </summary>
    public string ProposalSummary { get => _proposalSummary; private set => SetProperty(ref _proposalSummary, value); }
    /// <summary>
    /// Gets or updates proposal changes, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<string> ProposalChanges { get; } = [];
    /// <summary>
    /// Gets or updates safety notes, the bindable or domain state represented by this property.
    /// </summary>
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
    /// <summary>
    /// Gets or updates studio tab index, the bindable or domain state represented by this property.
    /// </summary>
    public int StudioTabIndex { get => _studioTabIndex; set => SetProperty(ref _studioTabIndex, value); }
    /// <summary>
    /// Reports whether renaming applies to the current state.
    /// </summary>
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
    /// <summary>
    /// Reports whether delete confirming applies to the current state.
    /// </summary>
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

    /// <summary>
    /// Gets or updates draft name, the bindable or domain state represented by this property.
    /// </summary>
    public string DraftName { get => _draftName; set => SetProperty(ref _draftName, value); }
    /// <summary>
    /// Gets or updates draft description, the bindable or domain state represented by this property.
    /// </summary>
    public string DraftDescription { get => _draftDescription; set => SetProperty(ref _draftDescription, value); }
    /// <summary>
    /// Gets or updates draft author, the bindable or domain state represented by this property.
    /// </summary>
    public string DraftAuthor { get => _draftAuthor; set => SetProperty(ref _draftAuthor, value); }
    /// <summary>
    /// Gets or updates draft font family, the bindable or domain state represented by this property.
    /// </summary>
    public string DraftFontFamily { get => _draftFontFamily; set => SetProperty(ref _draftFontFamily, value); }
    /// <summary>
    /// Gets or updates draft base font size, the bindable or domain state represented by this property.
    /// </summary>
    public double DraftBaseFontSize { get => _draftBaseFontSize; set => SetProperty(ref _draftBaseFontSize, value); }
    /// <summary>
    /// Gets or updates draft heading scale, the bindable or domain state represented by this property.
    /// </summary>
    public double DraftHeadingScale { get => _draftHeadingScale; set => SetProperty(ref _draftHeadingScale, value); }
    /// <summary>
    /// Gets or updates draft letter spacing, the bindable or domain state represented by this property.
    /// </summary>
    public double DraftLetterSpacing { get => _draftLetterSpacing; set => SetProperty(ref _draftLetterSpacing, value); }
    /// <summary>
    /// Gets or updates draft control radius, the bindable or domain state represented by this property.
    /// </summary>
    public double DraftControlRadius { get => _draftControlRadius; set => SetProperty(ref _draftControlRadius, value); }
    /// <summary>
    /// Gets or updates draft card radius, the bindable or domain state represented by this property.
    /// </summary>
    public double DraftCardRadius { get => _draftCardRadius; set => SetProperty(ref _draftCardRadius, value); }
    /// <summary>
    /// Gets or updates draft surface radius, the bindable or domain state represented by this property.
    /// </summary>
    public double DraftSurfaceRadius { get => _draftSurfaceRadius; set => SetProperty(ref _draftSurfaceRadius, value); }
    /// <summary>
    /// Gets or updates draft spacing scale, the bindable or domain state represented by this property.
    /// </summary>
    public double DraftSpacingScale { get => _draftSpacingScale; set => SetProperty(ref _draftSpacingScale, value); }
    /// <summary>
    /// Gets or updates draft show card borders, the bindable or domain state represented by this property.
    /// </summary>
    public bool DraftShowCardBorders { get => _draftShowCardBorders; set => SetProperty(ref _draftShowCardBorders, value); }
    /// <summary>
    /// Gets or updates draft use acrylic, the bindable or domain state represented by this property.
    /// </summary>
    public bool DraftUseAcrylic { get => _draftUseAcrylic; set => SetProperty(ref _draftUseAcrylic, value); }

    /// <summary>
    /// Performs initialize asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (!_runtime.ActiveTheme?.Equals(null) ?? false)
        {
            // The runtime is usually initialized by App before Settings opens.
        }
        await RefreshAsync(cancellationToken);
    }

    /// <summary>
    /// Performs import asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs export asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs refresh asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    /// <summary>
    /// Performs refresh asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs apply theme asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs apply appearance asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Creates new theme with the invariants required by its callers.
    /// </summary>
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

    /// <summary>
    /// Performs the open edit studio step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the duplicate theme step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the begin rename step owned by this component.
    /// </summary>
    private void BeginRename(ThemeCardViewModel card)
    {
        if (card.Theme.IsBuiltIn) return;
        RenameCandidate = card;
        RenameText = card.Theme.Name;
    }

    /// <summary>
    /// Performs confirm rename asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Reports whether cancel rename is true for the current state.
    /// </summary>
    private void CancelRename()
    {
        RenameCandidate = null;
        RenameText = string.Empty;
    }

    /// <summary>
    /// Performs the begin delete step owned by this component.
    /// </summary>
    private void BeginDelete(ThemeCardViewModel card)
    {
        if (!card.Theme.IsBuiltIn) DeleteCandidate = card;
    }

    /// <summary>
    /// Performs confirm delete asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Reports whether cancel delete is true for the current state.
    /// </summary>
    private void CancelDelete() => DeleteCandidate = null;

    /// <summary>
    /// Performs the request export step owned by this component.
    /// </summary>
    private void RequestExport(ThemeCardViewModel card) =>
        ExportRequested?.Invoke(this, new ThemeExportRequestedEventArgs(card.Theme.Id, card.Theme.Name));

    /// <summary>
    /// Performs generate with ai asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs preview manual asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs save and apply asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Reports whether cancel studio async is true for the current state.
    /// </summary>
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

    /// <summary>
    /// Performs the add timer page step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the add shortcut page step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the remove page step owned by this component.
    /// </summary>
    private void RemovePage(GeneratedPageEditorViewModel? page)
    {
        if (page is not null) Pages.Remove(page);
    }

    /// <summary>
    /// Performs the set draft step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Attempts to build draft and reports the result without using failure for normal control flow.
    /// </summary>
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

    /// <summary>
    /// Builds draft from the currently available inputs.
    /// </summary>
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

    /// <summary>
    /// Performs the raise command states step owned by this component.
    /// </summary>
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

/// <summary>
/// Represents theme card view model and keeps its related state and behavior together.
/// </summary>
public sealed class ThemeCardViewModel : ObservableObject
{
    /// <summary>
    /// Stores is active locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
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

    /// <summary>
    /// Gets or updates theme, the bindable or domain state represented by this property.
    /// </summary>
    public GenerativeThemePack Theme { get; }
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public string Name => Theme.Name;
    /// <summary>
    /// Gets or updates description, the bindable or domain state represented by this property.
    /// </summary>
    public string Description => Theme.Description;
    /// <summary>
    /// Gets or updates origin label, the bindable or domain state represented by this property.
    /// </summary>
    public string OriginLabel => Theme.IsBuiltIn ? "Built in" : Theme.Origin.ToString();
    /// <summary>
    /// Gets or updates pages label, the bindable or domain state represented by this property.
    /// </summary>
    public string PagesLabel => Theme.Pages.Count == 0 ? "No generated pages" : $"{Theme.Pages.Count} generated page{(Theme.Pages.Count == 1 ? string.Empty : "s")}";
    /// <summary>
    /// Gets or updates light preview, the bindable or domain state represented by this property.
    /// </summary>
    public string LightPreview => Theme.Light.Background;
    /// <summary>
    /// Gets or updates dark preview, the bindable or domain state represented by this property.
    /// </summary>
    public string DarkPreview => Theme.Dark.Background;
    /// <summary>
    /// Gets or updates accent preview, the bindable or domain state represented by this property.
    /// </summary>
    public string AccentPreview => Theme.Dark.Accent;
    /// <summary>
    /// Reports whether custom applies to the current state.
    /// </summary>
    public bool IsCustom => !Theme.IsBuiltIn;
    /// <summary>
    /// Reports whether active applies to the current state.
    /// </summary>
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }
    /// <summary>
    /// Gets or updates apply command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand ApplyCommand { get; }
    /// <summary>
    /// Gets or updates edit command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand EditCommand { get; }
    /// <summary>
    /// Gets or updates duplicate command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand DuplicateCommand { get; }
    /// <summary>
    /// Gets or updates rename command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand RenameCommand { get; }
    /// <summary>
    /// Gets or updates delete command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand DeleteCommand { get; }
    /// <summary>
    /// Gets or updates export command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand ExportCommand { get; }
}

/// <summary>
/// Represents theme placement editor view model and keeps its related state and behavior together.
/// </summary>
public sealed class ThemePlacementEditorViewModel : ObservableObject
{
    /// <summary>
    /// Stores region locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _region;
    /// <summary>
    /// Stores order locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _order;
    /// <summary>
    /// Stores is visible locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isVisible;
    /// <summary>
    /// Stores presentation locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _presentation;

    public ThemePlacementEditorViewModel(GenerativeUiCatalogItem item, GenerativeUiPlacement placement)
    {
        Item = item;
        _region = placement.Region;
        _order = placement.Order;
        _isVisible = placement.IsVisible;
        _presentation = placement.Presentation;
    }

    /// <summary>
    /// Gets or updates item, the bindable or domain state represented by this property.
    /// </summary>
    public GenerativeUiCatalogItem Item { get; }
    /// <summary>
    /// Gets or updates display name, the bindable or domain state represented by this property.
    /// </summary>
    public string DisplayName => Item.DisplayName;
    /// <summary>
    /// Gets or updates description, the bindable or domain state represented by this property.
    /// </summary>
    public string Description => Item.Description;
    /// <summary>
    /// Gets or updates allowed regions, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<string> AllowedRegions => Item.AllowedRegions;
    /// <summary>
    /// Reports whether hide applies to the current state.
    /// </summary>
    public bool CanHide => Item.CanHide;
    /// <summary>
    /// Gets or updates region, the bindable or domain state represented by this property.
    /// </summary>
    public string Region { get => _region; set => SetProperty(ref _region, value); }
    /// <summary>
    /// Gets or updates order, the bindable or domain state represented by this property.
    /// </summary>
    public int Order { get => _order; set => SetProperty(ref _order, value); }
    /// <summary>
    /// Reports whether visible applies to the current state.
    /// </summary>
    public bool IsVisible { get => _isVisible; set => SetProperty(ref _isVisible, value); }
    /// <summary>
    /// Gets or updates presentation, the bindable or domain state represented by this property.
    /// </summary>
    public string Presentation { get => _presentation; set => SetProperty(ref _presentation, value); }
    /// <summary>
    /// Performs the to placement step owned by this component.
    /// </summary>
    public GenerativeUiPlacement ToPlacement() => new(Item.Id, Region, Order, IsVisible, Presentation);
}

/// <summary>
/// Represents generated page editor view model and keeps its related state and behavior together.
/// </summary>
public sealed class GeneratedPageEditorViewModel : ObservableObject
{
    /// <summary>
    /// Stores title locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _title;
    /// <summary>
    /// Stores description locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _description;
    /// <summary>
    /// Stores icon key locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _iconKey;
    /// <summary>
    /// Stores order locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _order;

    public GeneratedPageEditorViewModel(GeneratedPageDefinition definition)
    {
        Definition = definition;
        _title = definition.Title;
        _description = definition.Description;
        _iconKey = definition.IconKey;
        _order = definition.Order;
    }

    /// <summary>
    /// Gets or updates definition, the bindable or domain state represented by this property.
    /// </summary>
    public GeneratedPageDefinition Definition { get; }
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public string Id => Definition.Id;
    /// <summary>
    /// Gets or updates title, the bindable or domain state represented by this property.
    /// </summary>
    public string Title { get => _title; set => SetProperty(ref _title, value); }
    /// <summary>
    /// Gets or updates description, the bindable or domain state represented by this property.
    /// </summary>
    public string Description { get => _description; set => SetProperty(ref _description, value); }
    /// <summary>
    /// Gets or updates icon key, the bindable or domain state represented by this property.
    /// </summary>
    public string IconKey { get => _iconKey; set => SetProperty(ref _iconKey, value); }
    /// <summary>
    /// Gets or updates order, the bindable or domain state represented by this property.
    /// </summary>
    public int Order { get => _order; set => SetProperty(ref _order, value); }
    /// <summary>
    /// Gets or updates widget summary, the bindable or domain state represented by this property.
    /// </summary>
    public string WidgetSummary => string.Join(", ", Definition.Widgets.Select(widget => widget.Kind.ToString()));
    /// <summary>
    /// Performs the to definition step owned by this component.
    /// </summary>
    public GeneratedPageDefinition ToDefinition() => Definition with { Title = Title, Description = Description, IconKey = IconKey, Order = Order };
}

/// <summary>
/// Represents theme palette editor view model and keeps its related state and behavior together.
/// </summary>
public sealed class ThemePaletteEditorViewModel : ObservableObject
{
    /// <summary>
    /// Stores values locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or updates background, the bindable or domain state represented by this property.
    /// </summary>
    public string Background { get => Get(nameof(Background)); set => Set(nameof(Background), value); }
    /// <summary>
    /// Gets or updates elevated, the bindable or domain state represented by this property.
    /// </summary>
    public string Elevated { get => Get(nameof(Elevated)); set => Set(nameof(Elevated), value); }
    /// <summary>
    /// Gets or updates panel, the bindable or domain state represented by this property.
    /// </summary>
    public string Panel { get => Get(nameof(Panel)); set => Set(nameof(Panel), value); }
    /// <summary>
    /// Gets or updates panel2, the bindable or domain state represented by this property.
    /// </summary>
    public string Panel2 { get => Get(nameof(Panel2)); set => Set(nameof(Panel2), value); }
    /// <summary>
    /// Gets or updates panel3, the bindable or domain state represented by this property.
    /// </summary>
    public string Panel3 { get => Get(nameof(Panel3)); set => Set(nameof(Panel3), value); }
    /// <summary>
    /// Gets or updates panel hover, the bindable or domain state represented by this property.
    /// </summary>
    public string PanelHover { get => Get(nameof(PanelHover)); set => Set(nameof(PanelHover), value); }
    /// <summary>
    /// Gets or updates text, the bindable or domain state represented by this property.
    /// </summary>
    public string Text { get => Get(nameof(Text)); set => Set(nameof(Text), value); }
    /// <summary>
    /// Gets or updates text soft, the bindable or domain state represented by this property.
    /// </summary>
    public string TextSoft { get => Get(nameof(TextSoft)); set => Set(nameof(TextSoft), value); }
    /// <summary>
    /// Gets or updates muted, the bindable or domain state represented by this property.
    /// </summary>
    public string Muted { get => Get(nameof(Muted)); set => Set(nameof(Muted), value); }
    /// <summary>
    /// Gets or updates muted2, the bindable or domain state represented by this property.
    /// </summary>
    public string Muted2 { get => Get(nameof(Muted2)); set => Set(nameof(Muted2), value); }
    /// <summary>
    /// Gets or updates accent, the bindable or domain state represented by this property.
    /// </summary>
    public string Accent { get => Get(nameof(Accent)); set => Set(nameof(Accent), value); }
    /// <summary>
    /// Gets or updates accent ink, the bindable or domain state represented by this property.
    /// </summary>
    public string AccentInk { get => Get(nameof(AccentInk)); set => Set(nameof(AccentInk), value); }
    /// <summary>
    /// Gets or updates accent soft, the bindable or domain state represented by this property.
    /// </summary>
    public string AccentSoft { get => Get(nameof(AccentSoft)); set => Set(nameof(AccentSoft), value); }
    /// <summary>
    /// Gets or updates blue, the bindable or domain state represented by this property.
    /// </summary>
    public string Blue { get => Get(nameof(Blue)); set => Set(nameof(Blue), value); }
    /// <summary>
    /// Gets or updates blue soft, the bindable or domain state represented by this property.
    /// </summary>
    public string BlueSoft { get => Get(nameof(BlueSoft)); set => Set(nameof(BlueSoft), value); }
    /// <summary>
    /// Gets or updates danger, the bindable or domain state represented by this property.
    /// </summary>
    public string Danger { get => Get(nameof(Danger)); set => Set(nameof(Danger), value); }
    /// <summary>
    /// Gets or updates warning, the bindable or domain state represented by this property.
    /// </summary>
    public string Warning { get => Get(nameof(Warning)); set => Set(nameof(Warning), value); }
    /// <summary>
    /// Gets or updates line, the bindable or domain state represented by this property.
    /// </summary>
    public string Line { get => Get(nameof(Line)); set => Set(nameof(Line), value); }
    /// <summary>
    /// Gets or updates line strong, the bindable or domain state represented by this property.
    /// </summary>
    public string LineStrong { get => Get(nameof(LineStrong)); set => Set(nameof(LineStrong), value); }
    /// <summary>
    /// Gets or updates nub, the bindable or domain state represented by this property.
    /// </summary>
    public string Nub { get => Get(nameof(Nub)); set => Set(nameof(Nub), value); }
    /// <summary>
    /// Gets or updates acrylic tint, the bindable or domain state represented by this property.
    /// </summary>
    public string AcrylicTint { get => Get(nameof(AcrylicTint)); set => Set(nameof(AcrylicTint), value); }
    /// <summary>
    /// Gets or updates acrylic fallback, the bindable or domain state represented by this property.
    /// </summary>
    public string AcrylicFallback { get => Get(nameof(AcrylicFallback)); set => Set(nameof(AcrylicFallback), value); }
    /// <summary>
    /// Gets or updates button, the bindable or domain state represented by this property.
    /// </summary>
    public string Button { get => Get(nameof(Button)); set => Set(nameof(Button), value); }
    /// <summary>
    /// Gets or updates button hover, the bindable or domain state represented by this property.
    /// </summary>
    public string ButtonHover { get => Get(nameof(ButtonHover)); set => Set(nameof(ButtonHover), value); }
    /// <summary>
    /// Gets or updates button pressed, the bindable or domain state represented by this property.
    /// </summary>
    public string ButtonPressed { get => Get(nameof(ButtonPressed)); set => Set(nameof(ButtonPressed), value); }
    /// <summary>
    /// Gets or updates focus, the bindable or domain state represented by this property.
    /// </summary>
    public string Focus { get => Get(nameof(Focus)); set => Set(nameof(Focus), value); }

    /// <summary>
    /// Performs the load step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the to palette step owned by this component.
    /// </summary>
    public GenerativeThemePalette ToPalette() => new(
        Background, Elevated, Panel, Panel2, Panel3, PanelHover, Text, TextSoft, Muted, Muted2,
        Accent, AccentInk, AccentSoft, Blue, BlueSoft, Danger, Warning, Line, LineStrong, Nub,
        AcrylicTint, AcrylicFallback, Button, ButtonHover, ButtonPressed, Focus);

    /// <summary>
    /// Retrieves this member for the current operation.
    /// </summary>
    private string Get(string key) => _values.TryGetValue(key, out var value) ? value : "#FF000000";
    /// <summary>
    /// Performs the set step owned by this component.
    /// </summary>
    private void Set(string key, string value)
    {
        if (_values.TryGetValue(key, out var current) && current == value) return;
        _values[key] = value;
        RaisePropertyChanged(key);
    }
}

/// <summary>
/// Represents theme export requested event args and keeps its related state and behavior together.
/// </summary>
public sealed record ThemeExportRequestedEventArgs(Guid ThemeId, string ThemeName);

/// <summary>
/// Represents observable collection extensions and keeps its related state and behavior together.
/// </summary>
internal static class ObservableCollectionExtensions
{
    public static void ReplaceWith<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items) collection.Add(item);
    }
}

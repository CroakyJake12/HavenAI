using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views;

namespace Haven.Desktop;

public sealed partial class MainWindow : Window
{
    private MainWindowViewModel? _viewModel;
    private ExperienceShellHost? _experienceShell;
    private Button? _studioExperienceButton;
    private Button? _notesExperienceButton;

    public MainWindow()
    {
        InitializeComponent();
        InstallGenerativeUiHeaderSlot();
        InstallExperienceShell();
        DataContextChanged += (_, _) => AttachViewModel(DataContext as MainWindowViewModel);
        Opened += OnWindowOpened;
        Closed += (_, _) =>
        {
            AttachViewModel(null);
            if (_studioExperienceButton is not null)
                _studioExperienceButton.Click -= OnStudioExperienceClicked;
            _studioExperienceButton = null;
            _notesExperienceButton = null;
            _experienceShell?.Dispose();
        };
    }

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        RefineExperienceRail();
        await Task.Delay(1200);
        if (IsVisible) RefineExperienceRail();
    }

    private void InstallGenerativeUiHeaderSlot()
    {
        if (Content is not Grid root) return;
        var headerBorder = root.Children
            .OfType<Border>()
            .FirstOrDefault(border => Grid.GetRow(border) == 1 && border.Child is Grid);
        if (headerBorder?.Child is not Grid headerGrid) return;
        var rightHeader = headerGrid.Children
            .OfType<StackPanel>()
            .FirstOrDefault(panel => Grid.GetColumn(panel) == 2);
        if (rightHeader is null || rightHeader.Children.OfType<GenerativeUiSlot>().Any()) return;
        rightHeader.Children.Insert(0, new GenerativeUiSlot
        {
            Region = GenerativeUiCatalog.ShellHeaderRight
        });
    }

    private void InstallExperienceShell()
    {
        if (Content is not Control existingShell || existingShell is ExperienceShellHost) return;
        HideLegacyProductSwitcher(existingShell);
        _experienceShell = new ExperienceShellHost(existingShell);
        Content = _experienceShell;
    }

    private static void HideLegacyProductSwitcher(Control shell)
    {
        if (shell is not Grid root) return;
        var headerBorder = root.Children
            .OfType<Border>()
            .FirstOrDefault(border => Grid.GetRow(border) == 1 && border.Child is Grid);
        if (headerBorder?.Child is not Grid headerGrid) return;
        var leftHeader = headerGrid.Children
            .OfType<StackPanel>()
            .FirstOrDefault(panel => Grid.GetColumn(panel) == 0);
        var legacySwitcher = leftHeader?.Children
            .OfType<Button>()
            .FirstOrDefault(button => button.Classes.Contains("product"));
        if (legacySwitcher is not null) legacySwitcher.IsVisible = false;
    }

    private void RefineExperienceRail()
    {
        if (_experienceShell is null) return;
        EnsureNotesExperienceButton();
        var currentStudioButton = _experienceShell.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(button => button.Name == "ExperienceStudioButton");
        if (!ReferenceEquals(_studioExperienceButton, currentStudioButton))
        {
            if (_studioExperienceButton is not null)
                _studioExperienceButton.Click -= OnStudioExperienceClicked;
            _studioExperienceButton = currentStudioButton;
        }
        if (_studioExperienceButton is not null && _studioExperienceButton.Flyout is not null)
        {
            _studioExperienceButton.Flyout = null;
            _studioExperienceButton.Click += OnStudioExperienceClicked;
            ToolTip.SetTip(_studioExperienceButton, "Studio");
        }
        UpdateExperienceFamilyState();
    }

    private void EnsureNotesExperienceButton()
    {
        if (_experienceShell is null) return;
        var current = _experienceShell.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(button => button.Name == "ExperienceNotesButton");
        if (current is not null)
        {
            _notesExperienceButton = current;
            return;
        }
        var chat = _experienceShell.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(button => button.Name == "ExperienceChatButton");
        if (chat?.Parent is not StackPanel experiencePanel) return;

        var notes = new Button
        {
            Name = "ExperienceNotesButton",
            Width = 50,
            Height = 48,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = new TextBlock { Text = "▤", FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center }
        };
        notes.Classes.Add("icon");
        ToolTip.SetTip(notes, "Notes, Present, Data, Tasks and Imagine");
        var menu = new StackPanel { Width = 330, Spacing = 4 };
        foreach (var kind in Enum.GetValues<NotesExperienceKind>())
            menu.Children.Add(NotesEntry(kind));
        notes.Flyout = new Flyout { Placement = PlacementMode.Right, Content = menu };
        var index = experiencePanel.Children.IndexOf(chat);
        experiencePanel.Children.Insert(Math.Min(index + 1, experiencePanel.Children.Count), notes);
        _notesExperienceButton = notes;
    }

    private Button NotesEntry(NotesExperienceKind kind)
    {
        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                ColumnSpacing = 10,
                Children =
                {
                    new TextBlock { Text = kind == NotesExperienceKind.Notes ? "▤" : "◇", FontSize = 17, VerticalAlignment = VerticalAlignment.Center },
                    WithColumn(new StackPanel
                    {
                        Spacing = 1,
                        Children =
                        {
                            new TextBlock { Text = NotesExperienceNavigation.DisplayName(kind), FontWeight = FontWeight.SemiBold },
                            new TextBlock { Text = NotesExperienceNavigation.Description(kind), Classes = { "muted" }, FontSize = 10, TextWrapping = TextWrapping.Wrap }
                        }
                    }, 1)
                }
            }
        };
        button.Classes.Add("sidebar");
        button.Click += async (_, _) =>
        {
            if (_viewModel is not null) await NotesExperienceNavigation.OpenAsync(_viewModel, kind);
            UpdateExperienceFamilyState();
        };
        return button;
    }

    private async void OnStudioExperienceClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
            await _viewModel.NavigateStudioCommand.ExecuteAsync();
    }

    private void AttachViewModel(MainWindowViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.CopyRequested -= OnCopyRequested;
            _viewModel.DictateRequested -= OnDictateRequested;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.CopyRequested += OnCopyRequested;
            _viewModel.DictateRequested += OnDictateRequested;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
        Dispatcher.UIThread.Post(RefineExperienceRail);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.CurrentPage)
            or nameof(MainWindowViewModel.CurrentChat)
            or nameof(MainWindowViewModel.ProductName))
            Dispatcher.UIThread.Post(RefineExperienceRail);
    }

    private void UpdateExperienceFamilyState()
    {
        if (_experienceShell is null || _viewModel is null) return;
        var plan = _experienceShell.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(button => button.Name == "ExperiencePlanButton");
        if (plan is not null)
        {
            var active = _viewModel.CurrentSurface == HavenSurface.Plan
                         || _viewModel.CurrentPage is AutomationsPageViewModel;
            plan.Background = active
                ? ResourceBrush("HavenAccentSoftBrush", Color.FromArgb(72, 0, 120, 212))
                : Brushes.Transparent;
        }
        if (_notesExperienceButton is not null)
        {
            var active = _viewModel.CurrentPage is NotesWorkspaceView or BlankNotesExperienceView;
            _notesExperienceButton.Background = active
                ? ResourceBrush("HavenAccentSoftBrush", Color.FromArgb(72, 0, 120, 212))
                : Brushes.Transparent;
        }
    }

    private static IBrush ResourceBrush(string key, Color fallback) =>
        Avalonia.Application.Current?.Resources[key] as IBrush ?? new SolidColorBrush(fallback);

    private async void OnCopyRequested(object? sender, string content)
    {
        if (Clipboard is not null) await Clipboard.SetTextAsync(content);
    }

    private void OnDictateRequested(object? sender, EventArgs e)
    {
        if (!OperatingSystem.IsWindows()) return;
        keybd_event(0x5B, 0, 0, UIntPtr.Zero);
        keybd_event(0x48, 0, 0, UIntPtr.Zero);
        keybd_event(0x48, 0, 2, UIntPtr.Zero);
        keybd_event(0x5B, 0, 2, UIntPtr.Zero);
    }

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (control && e.Key == Key.K)
        {
            vm.OpenCommandPaletteCommand.Execute(null);
            e.Handled = true;
        }
        else if (control && e.Key == Key.N && CurrentNotesViewModel(vm) is { } notesForNew)
        {
            await notesForNew.NewDocumentCommand.ExecuteAsync();
            e.Handled = true;
        }
        else if (control && e.Key == Key.N)
        {
            vm.NewChatCommand.Execute(null);
            e.Handled = true;
        }
        else if (control && e.Key == Key.S && CurrentNotesViewModel(vm) is { } notesForSave)
        {
            await notesForSave.SaveCommand.ExecuteAsync();
            e.Handled = true;
        }
        else if (control && e.Key == Key.S)
        {
            vm.SaveCurrentCommand.Execute(null);
            e.Handled = true;
        }
        else if (control && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.R && vm.CurrentPage is BrowserPageViewModel browser)
        {
            browser.HardReloadCommand.Execute(null);
            e.Handled = true;
        }
    }

    private static NotesWorkspaceViewModel? CurrentNotesViewModel(MainWindowViewModel shell) =>
        shell.CurrentPage is NotesWorkspaceView { DataContext: NotesWorkspaceViewModel notes } ? notes : null;

    private void OnExitClicked(object? sender, RoutedEventArgs e) => Close();

    private async void OnAboutClicked(object? sender, RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Title = "About Haven",
            Width = 430,
            Height = 260,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Haven", FontSize = 26, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = "A local-first AI workspace for Chat, Notes, Teaching, Do, Studio and Browse.", TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = "Native Avalonia workspace", Opacity = 0.65 }
                }
            }
        };
        await dialog.ShowDialog(this);
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    private void Tab_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control control && control.DataContext is WorkspaceTabViewModel tab)
            tab.IsHovered = true;
    }

    private void Tab_PointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Control control && control.DataContext is WorkspaceTabViewModel tab)
            tab.IsHovered = false;
    }

    private static T WithColumn<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }
}

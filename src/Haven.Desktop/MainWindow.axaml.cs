using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Desktop.Controls;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop;

public sealed partial class MainWindow : Window
{
    private MainWindowViewModel? _viewModel;
    private ExperienceShellHost? _experienceShell;
    private Button? _studioExperienceButton;

    public MainWindow()
    {
        InitializeComponent();
        InstallGenerativeUiHeaderSlot();
        InstallExperienceShell();
        DataContextChanged += (_, _) => AttachViewModel(DataContext as MainWindowViewModel);
        Opened += (_, _) => RefineExperienceRail();
        Closed += (_, _) =>
        {
            AttachViewModel(null);
            _experienceShell?.Dispose();
        };
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
        _studioExperienceButton ??= _experienceShell.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(button => button.Name == "ExperienceStudioButton");
        if (_studioExperienceButton is not null && _studioExperienceButton.Flyout is not null)
        {
            _studioExperienceButton.Flyout = null;
            _studioExperienceButton.Click += OnStudioExperienceClicked;
            ToolTip.SetTip(_studioExperienceButton, "Studio");
        }
        UpdateExperienceFamilyState();
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
            Dispatcher.UIThread.Post(UpdateExperienceFamilyState);
    }

    private void UpdateExperienceFamilyState()
    {
        if (_experienceShell is null || _viewModel is null) return;
        var plan = _experienceShell.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(button => button.Name == "ExperiencePlanButton");
        if (plan is null) return;
        var active = _viewModel.CurrentSurface == HavenSurface.Plan
                     || _viewModel.CurrentPage is AutomationsPageViewModel;
        plan.Background = active
            ? ResourceBrush("HavenAccentSoftBrush", Color.FromArgb(72, 0, 120, 212))
            : Brushes.Transparent;
    }

    private static IBrush ResourceBrush(string key, Color fallback) =>
        Application.Current?.Resources[key] as IBrush ?? new SolidColorBrush(fallback);

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

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (control && e.Key == Key.K) { vm.OpenCommandPaletteCommand.Execute(null); e.Handled = true; }
        else if (control && e.Key == Key.N) { vm.NewChatCommand.Execute(null); e.Handled = true; }
        else if (control && e.Key == Key.S) { vm.SaveCurrentCommand.Execute(null); e.Handled = true; }
        else if (control && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.R && vm.CurrentPage is BrowserPageViewModel browser)
        {
            browser.HardReloadCommand.Execute(null);
            e.Handled = true;
        }
    }

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
                    new TextBlock { Text = "A local-first AI workspace for Chat, Teaching, Do, Studio and Browse.", TextWrapping = TextWrapping.Wrap },
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
}

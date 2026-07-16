using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Haven.Application;
using Haven.Desktop.Controls;
using Haven.Desktop.ViewModels;
using System.Runtime.InteropServices;

namespace Haven.Desktop;

public sealed partial class MainWindow : Window
{
    private MainWindowViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        InstallGenerativeUiHeaderSlot();
        DataContextChanged += (_, _) => AttachViewModel(DataContext as MainWindowViewModel);
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

    private void AttachViewModel(MainWindowViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.CopyRequested -= OnCopyRequested;
            _viewModel.DictateRequested -= OnDictateRequested;
        }
        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.CopyRequested += OnCopyRequested;
            _viewModel.DictateRequested += OnDictateRequested;
        }
    }

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
                Margin = new Avalonia.Thickness(24),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Haven", FontSize = 26, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                    new TextBlock { Text = "A local-first AI workspace for Chat, Teaching, Do, Studio and Browse.", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
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

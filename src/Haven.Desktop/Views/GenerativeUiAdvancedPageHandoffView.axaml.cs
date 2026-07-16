using Avalonia.Controls;
using Avalonia.Interactivity;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views;

public sealed partial class GenerativeUiAdvancedPageHandoffView : UserControl
{
    private bool _isOpening;

    public GenerativeUiAdvancedPageHandoffView() => InitializeComponent();

    private async void OnOpenStudioClicked(object? sender, RoutedEventArgs e)
    {
        if (_isOpening) return;
        if (TopLevel.GetTopLevel(this)?.DataContext is not MainWindowViewModel shell)
        {
            StatusText.Text = "The Haven shell is not available, so the Studio handoff could not be opened.";
            return;
        }

        try
        {
            _isOpening = true;
            if (sender is Button button) button.IsEnabled = false;
            StatusText.Text = "Opening a fresh Haven Studio chat and preparing the reviewed specification…";
            await GenerativeModeStudioHandoff.OpenAsync(
                shell,
                RequestBox.Text ?? string.Empty,
                CancellationToken.None);
            StatusText.Text = "The specification is ready in Haven Studio. Review or edit it, then send it when you are satisfied.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Studio handoff failed: " + ex.Message;
        }
        finally
        {
            _isOpening = false;
            if (sender is Button button) button.IsEnabled = true;
        }
    }
}
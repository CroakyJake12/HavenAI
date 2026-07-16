using Avalonia.Controls;
using Avalonia.Interactivity;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views;

public sealed partial class GenerativeUiAdvancedPageHandoffView : UserControl, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private bool _isOpening;
    private bool _disposed;

    public GenerativeUiAdvancedPageHandoffView()
    {
        InitializeComponent();
        DetachedFromVisualTree += (_, _) => CancelLifetime();
    }

    private async void OnOpenStudioClicked(object? sender, RoutedEventArgs e)
    {
        if (_isOpening || _disposed) return;
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
                _lifetime.Token);
            if (!_disposed)
                StatusText.Text = "The specification is ready in Haven Studio. Review or edit it, then send it when you are satisfied.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The Settings view closed or navigated away. Avoid stale UI updates.
        }
        catch (Exception ex)
        {
            if (!_disposed) StatusText.Text = "Studio handoff failed: " + ex.Message;
        }
        finally
        {
            _isOpening = false;
            if (!_disposed && sender is Button button) button.IsEnabled = true;
        }
    }

    private void CancelLifetime()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
    }

    public void Dispose()
    {
        if (!_disposed) CancelLifetime();
        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }
}

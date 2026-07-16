using Avalonia.Controls;
using Avalonia.Interactivity;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views;

public sealed partial class GenerativeUiAdvancedPageHandoffView : UserControl, IDisposable
{
    private CancellationTokenSource? _operation;
    private bool _isOpening;
    private bool _disposed;

    public GenerativeUiAdvancedPageHandoffView()
    {
        InitializeComponent();
        DetachedFromVisualTree += (_, _) => CancelOperation();
    }

    private async void OnOpenStudioClicked(object? sender, RoutedEventArgs e)
    {
        if (_isOpening || _disposed) return;
        if (TopLevel.GetTopLevel(this)?.DataContext is not MainWindowViewModel shell)
        {
            StatusText.Text = "The Haven shell is not available, so the Studio handoff could not be opened.";
            return;
        }

        CancellationTokenSource? operation = null;
        try
        {
            _isOpening = true;
            operation = new CancellationTokenSource();
            _operation = operation;
            if (sender is Button button) button.IsEnabled = false;
            StatusText.Text = "Opening a fresh Haven Studio chat and preparing the reviewed specification…";
            await GenerativeModeStudioHandoff.OpenAsync(
                shell,
                RequestBox.Text ?? string.Empty,
                operation.Token);
            if (!_disposed && !operation.IsCancellationRequested)
                StatusText.Text = "The specification is ready in Haven Studio. Review or edit it, then send it when you are satisfied.";
        }
        catch (OperationCanceledException) when (operation?.IsCancellationRequested == true)
        {
            // The Settings view closed or navigated away. Avoid stale UI updates.
        }
        catch (Exception ex)
        {
            if (!_disposed) StatusText.Text = "Studio handoff failed: " + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_operation, operation)) _operation = null;
            operation?.Dispose();
            _isOpening = false;
            if (!_disposed && sender is Button button) button.IsEnabled = true;
        }
    }

    private void CancelOperation()
    {
        try
        {
            _operation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Completion won the race with view detachment.
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelOperation();
        _operation?.Dispose();
        _operation = null;
        GC.SuppressFinalize(this);
    }
}

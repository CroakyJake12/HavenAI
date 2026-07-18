/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Views/GenerativeUiAdvancedPageHandoffView.axaml.cs, in the Desktop view layer, where Avalonia controls connect XAML interaction to view models.
 * What: This file owns GenerativeUiAdvancedPageHandoffView. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia.Controls;
using Avalonia.Interactivity;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views;

/// <summary>
/// Represents generative ui advanced page handoff view and keeps its related state and behavior together.
/// </summary>
public sealed partial class GenerativeUiAdvancedPageHandoffView : UserControl, IDisposable
{
    /// <summary>
    /// Stores operation locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private CancellationTokenSource? _operation;
    /// <summary>
    /// Stores is opening locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isOpening;
    /// <summary>
    /// Stores disposed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _disposed;

    public GenerativeUiAdvancedPageHandoffView()
    {
        InitializeComponent();
        DetachedFromVisualTree += (_, _) => CancelOperation();
    }

    /// <summary>
    /// Handles the open studio clicked event raised by the UI or runtime.
    /// </summary>
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

    /// <summary>
    /// Reports whether cancel operation is true for the current state.
    /// </summary>
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

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
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

/*
 * FILE DOCUMENTATION
 * Where: src/Haven.OldHaven/Controls/NotesHtmlPreviewControl.cs, in the Desktop controls layer, containing reusable Avalonia behavior and visual building blocks.
 * What: This file owns NotesHtmlPreviewControl. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text;
using Avalonia.Controls;
using Haven.Core;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Controls;

/// <summary>
/// Represents notes html preview control and keeps its related state and behavior together.
/// </summary>
public sealed class NotesHtmlPreviewControl : UserControl, IDisposable
{
    /// <summary>
    /// Stores web view locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly NativeWebView _webView = new();
    /// <summary>
    /// Stores error locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TextBlock _error = new()
    {
        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        Margin = new Avalonia.Thickness(10),
        IsVisible = false
    };
    /// <summary>
    /// Stores disposed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _disposed;

    public NotesHtmlPreviewControl()
    {
        var grid = new Grid();
        grid.Children.Add(_webView);
        grid.Children.Add(_error);
        Content = grid;
        MinHeight = 220;
        _webView.NavigationStarted += OnNavigationStarted;
        _webView.NewWindowRequested += OnNewWindowRequested;
    }

    /// <summary>
    /// Performs the update preview step owned by this component.
    /// </summary>
    public void UpdatePreview(NotesHtmlData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var sandbox = NotesHtmlSandbox.Build(data);
        if (!string.IsNullOrWhiteSpace(sandbox.Error))
        {
            _error.Text = sandbox.Error + " The source remains editable; preview is blocked until permissions and source agree.";
            _error.IsVisible = true;
            _webView.IsVisible = false;
            return;
        }
        _error.IsVisible = false;
        _webView.IsVisible = true;
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(sandbox.DocumentHtml));
        _webView.Navigate(new Uri("data:text/html;charset=utf-8;base64," + base64, UriKind.Absolute));
    }

    /// <summary>
    /// Handles the navigation started event raised by the UI or runtime.
    /// </summary>
    private static void OnNavigationStarted(object? sender, WebViewNavigationStartingEventArgs args)
    {
        if (args.Request?.Scheme is "data" or "about") return;
        args.Cancel = true;
    }

    /// <summary>
    /// Handles the new window requested event raised by the UI or runtime.
    /// </summary>
    private static void OnNewWindowRequested(object? sender, WebViewNewWindowRequestedEventArgs args) =>
        args.Handled = true;

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _webView.NavigationStarted -= OnNavigationStarted;
        _webView.NewWindowRequested -= OnNewWindowRequested;
        Content = null;
        GC.SuppressFinalize(this);
    }
}

using System.Text;
using Avalonia.Controls;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Controls;

public sealed class NotesHtmlPreviewControl : UserControl, IDisposable
{
    private readonly NativeWebView _webView = new();
    private readonly TextBlock _error = new()
    {
        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        Margin = new Avalonia.Thickness(10),
        IsVisible = false
    };
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

    private static void OnNavigationStarted(object? sender, WebViewNavigationStartingEventArgs args)
    {
        if (args.Request?.Scheme is "data" or "about") return;
        args.Cancel = true;
    }

    private static void OnNewWindowRequested(object? sender, WebViewNewWindowRequestedEventArgs args) =>
        args.Handled = true;

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

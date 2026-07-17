using Avalonia.Controls;
using Haven.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views;

public sealed partial class BrowserSafetyView : UserControl
{
    public BrowserSafetyView()
    {
        InitializeComponent();
        if (App.Services is not null)
            DataContext = ActivatorUtilities.CreateInstance<BrowserSafetyViewModel>(App.Services);
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is IDisposable disposable) disposable.Dispose();
        DataContext = null;
        DetachedFromVisualTree -= OnDetachedFromVisualTree;
    }
}

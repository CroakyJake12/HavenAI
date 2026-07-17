using Avalonia.Controls;
using Haven.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views;

public sealed partial class BrowserSafetyView : UserControl
{
    public BrowserSafetyView()
    {
        InitializeComponent();
        CreateViewModel();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e) => CreateViewModel();

    private void OnDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is IDisposable disposable) disposable.Dispose();
        DataContext = null;
    }

    private void CreateViewModel()
    {
        if (DataContext is not null || App.Services is null) return;
        DataContext = ActivatorUtilities.CreateInstance<BrowserSafetyViewModel>(App.Services);
    }
}

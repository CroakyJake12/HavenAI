using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Haven.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views;

public sealed partial class ProviderConnectionsView : UserControl
{
    public ProviderConnectionsView()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<ProviderConnectionsViewModel>();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

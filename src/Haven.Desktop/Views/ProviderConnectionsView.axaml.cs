using Avalonia.Controls;
using Haven.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views;

public sealed partial class ProviderConnectionsView : UserControl
{
    public ProviderConnectionsView()
    {
        InitializeComponent();
        DataContext = App.Services?.GetService<ProviderConnectionsViewModel>();
    }
}

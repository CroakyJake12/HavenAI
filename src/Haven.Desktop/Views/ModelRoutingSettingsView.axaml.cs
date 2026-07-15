using Avalonia.Controls;
using Haven.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views;

public sealed partial class ModelRoutingSettingsView : UserControl
{
    public ModelRoutingSettingsView()
    {
        InitializeComponent();
        if (App.Services is not null)
            DataContext = ActivatorUtilities.CreateInstance<ModelRoutingSettingsViewModel>(App.Services);
    }
}

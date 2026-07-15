using Avalonia.Controls;
using Haven.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views;

public sealed partial class LanguageServerSettingsView : UserControl
{
    public LanguageServerSettingsView()
    {
        InitializeComponent();
        if (App.Services is not null)
            DataContext = ActivatorUtilities.CreateInstance<LanguageServerSettingsViewModel>(App.Services);
    }
}

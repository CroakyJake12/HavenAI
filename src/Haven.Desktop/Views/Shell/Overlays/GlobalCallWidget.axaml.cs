using Avalonia.Controls;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views.Shell.Overlays;

public sealed partial class GlobalCallWidget : UserControl
{
    public GlobalCallWidget()
    {
        InitializeComponent();
    }

    public GlobalCallWidget(InChatCallWidgetViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }
}

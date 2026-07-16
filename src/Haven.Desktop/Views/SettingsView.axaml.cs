using Avalonia.Controls;

namespace Haven.Desktop.Views;

public sealed partial class SettingsView : UserControl, IDisposable
{
    private GenerativeUiThemeSelectorView? _generativeUi;

    public SettingsView()
    {
        InitializeComponent();
        ReplaceLegacyAppearanceEditor();
    }

    public void Dispose()
    {
        _generativeUi?.Dispose();
        _generativeUi = null;
        DataContext = null;
    }

    private void ReplaceLegacyAppearanceEditor()
    {
        if (Content is not ScrollViewer { Content: StackPanel stack }) return;
        var legacyAppearance = stack.Children.OfType<Border>().FirstOrDefault();
        if (legacyAppearance is null) return;
        var index = stack.Children.IndexOf(legacyAppearance);
        if (index < 0) return;
        _generativeUi = new GenerativeUiThemeSelectorView();
        stack.Children.RemoveAt(index);
        stack.Children.Insert(index, _generativeUi);
    }
}

/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/MainWindow.DeveloperTools.cs in the Desktop composition layer.
 * What: Connects debug keyboard shortcuts in MainWindow to Haven's built-in visual inspector.
 * How: F12 toggles the inspector and Ctrl+Shift+C opens the transparent element picker.
 * Why: The integration remains tiny and isolated from the production workspace shell.
 */

using Avalonia.Input;
using Haven.Desktop.DeveloperTools;

namespace Haven.Desktop;

public sealed partial class MainWindow
{
#if DEBUG
    private HavenDeveloperToolsSession? _developerTools;
#endif

    protected override void OnKeyDown(KeyEventArgs e)
    {
#if DEBUG
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (e.Key == Key.F12)
        {
            (_developerTools ??= new HavenDeveloperToolsSession(this)).Toggle();
            e.Handled = true;
            return;
        }

        if (control && shift && e.Key == Key.C)
        {
            (_developerTools ??= new HavenDeveloperToolsSession(this)).BeginInspect();
            e.Handled = true;
            return;
        }
#endif
        base.OnKeyDown(e);
    }
}

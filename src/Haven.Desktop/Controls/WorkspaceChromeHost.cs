using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Haven.Desktop.Controls;

/// <summary>
/// Reframes the existing MainWindow content with a compact workspace chrome. The legacy
/// tab strip, menu and status controls are detached; the modern controls bind to the same
/// MainWindow view model so the underlying pages and commands remain authoritative.
/// </summary>
public sealed partial class WorkspaceChromeHost : Grid, IDisposable
{
    private readonly ExperienceShellHost _experienceShell;
    private bool _disposed;

    public WorkspaceChromeHost(Control existingShell)
    {
        ArgumentNullException.ThrowIfNull(existingShell);

        RowDefinitions = new RowDefinitions("58,*");
        Background = Brushes.Transparent;

        ExtractLegacyChrome(existingShell);
        Children.Add(BuildModernTopBar());

        _experienceShell = new ExperienceShellHost(existingShell);
        Grid.SetRow(_experienceShell, 1);
        Children.Add(_experienceShell);

        InitializeModernChrome();
    }

    private static void ExtractLegacyChrome(Control existingShell)
    {
        if (existingShell is not Grid root) return;

        var tabStrip = root.Children
            .OfType<Border>()
            .FirstOrDefault(border => Grid.GetRow(border) == 0 && Grid.GetRowSpan(border) == 1);

        var headerBorder = root.Children
            .OfType<Border>()
            .FirstOrDefault(border => Grid.GetRow(border) == 1 && border.Child is Grid);

        if (headerBorder?.Child is Grid headerGrid)
        {
            var leftHeader = headerGrid.Children
                .OfType<StackPanel>()
                .FirstOrDefault(panel => Grid.GetColumn(panel) == 0);
            var legacyMenu = leftHeader?.Children.OfType<Menu>().FirstOrDefault();
            if (legacyMenu is not null) leftHeader!.Children.Remove(legacyMenu);

            var statusControls = headerGrid.Children
                .OfType<StackPanel>()
                .FirstOrDefault(panel => Grid.GetColumn(panel) == 2);
            if (statusControls is not null) headerGrid.Children.Remove(statusControls);
        }

        if (tabStrip is not null) root.Children.Remove(tabStrip);
        if (headerBorder is not null) root.Children.Remove(headerBorder);

        // The original page/content area already occupies row 2. Collapsing the detached
        // rows lets it fill the workspace while preserving all existing overlays and bindings.
        root.RowDefinitions = new RowDefinitions("0,0,*");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeModernChrome();
        _experienceShell.Dispose();
        GC.SuppressFinalize(this);
    }
}

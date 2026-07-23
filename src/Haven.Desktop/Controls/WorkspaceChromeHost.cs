/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Controls/WorkspaceChromeHost.cs, in the Desktop controls layer, containing reusable Avalonia behavior and visual building blocks.
 * What: This file owns WorkspaceChromeHost. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

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
    /// <summary>
    /// Stores experience shell locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ExperienceShellHost _experienceShell;
    /// <summary>
    /// Stores disposed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _disposed;

    public WorkspaceChromeHost(Control existingShell)
    {
        ArgumentNullException.ThrowIfNull(existingShell);

        RowDefinitions = new RowDefinitions("66,*");
        Background = Brushes.Transparent;
        ClipToBounds = true;

        ExtractLegacyChrome(existingShell);

        Children.Add(BuildMagicalBackdrop());
        Children.Add(BuildFloatingTopRail(BuildModernTopBar()));

        _experienceShell = new ExperienceShellHost(existingShell);
        Grid.SetRow(_experienceShell, 1);
        Children.Add(_experienceShell);

        InitializeModernChrome();
        InitializeActionsBridge();
        InitializeMagicalTheme();
    }

    /// <summary>
    /// Performs the extract legacy chrome step owned by this component.
    /// </summary>
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

        // Remove the background border that blocks the custom backdrop
        var backgroundBorder = root.Children
            .OfType<Border>()
            .FirstOrDefault(border => Grid.GetRowSpan(border) > 1 && border.IsHitTestVisible == false);
        if (backgroundBorder is not null) root.Children.Remove(backgroundBorder);

        // The original page/content area already occupies row 2. Collapsing the detached
        // rows lets it fill the workspace while preserving all existing overlays and bindings.
        root.RowDefinitions = new RowDefinitions("0,0,*");
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeMagicalTheme();
        DisposeActionsBridge();
        DisposeModernChrome();
        _experienceShell.Dispose();
        GC.SuppressFinalize(this);
    }
}

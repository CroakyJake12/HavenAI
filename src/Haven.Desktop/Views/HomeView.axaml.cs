/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Views/HomeView.axaml.cs, in the Desktop view layer, where Avalonia controls connect XAML interaction to view models.
 * What: This file owns HomeView. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia.Controls;
using Avalonia.Input;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views;

/// <summary>
/// Represents home view and keeps its related state and behavior together.
/// </summary>
public sealed partial class HomeView : UserControl
{
    /// <summary>
    /// Stores dragged tile locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private DashboardTileViewModel? _draggedTile;

    public HomeView() => InitializeComponent();

    /// <summary>
    /// Performs the tile_pointer pressed step owned by this component.
    /// </summary>
    private void Tile_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not HomePageViewModel { IsCustomizing: true } ||
            sender is not Border { DataContext: DashboardTileViewModel tile } ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _draggedTile = tile;
        e.Handled = true;
    }

    /// <summary>
    /// Performs the tile_pointer entered step owned by this component.
    /// </summary>
    private async void Tile_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _draggedTile = null;
            return;
        }
        if (_draggedTile is null || DataContext is not HomePageViewModel viewModel ||
            sender is not Border { DataContext: DashboardTileViewModel target } || ReferenceEquals(target, _draggedTile)) return;
        var targetIndex = viewModel.Tiles.IndexOf(target);
        if (targetIndex >= 0) await viewModel.MoveToIndexAsync(_draggedTile, targetIndex);
    }

    /// <summary>
    /// Performs the tile_pointer released step owned by this component.
    /// </summary>
    private void Tile_PointerReleased(object? sender, PointerReleasedEventArgs e) => _draggedTile = null;
}

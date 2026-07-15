using Avalonia.Controls;
using Avalonia.Input;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views;

public sealed partial class HomeView : UserControl
{
    private DashboardTileViewModel? _draggedTile;

    public HomeView() => InitializeComponent();

    private void Tile_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not HomePageViewModel { IsCustomizing: true } ||
            sender is not Border { DataContext: DashboardTileViewModel tile } ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _draggedTile = tile;
        e.Handled = true;
    }

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

    private void Tile_PointerReleased(object? sender, PointerReleasedEventArgs e) => _draggedTile = null;
}

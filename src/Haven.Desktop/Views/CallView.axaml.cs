using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views;

public partial class CallView : UserControl
{
    private INotifyCollectionChanged? _observedTranscript;

    public CallView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CallPageViewModel viewModel)
            await viewModel.InitializeAsync();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_observedTranscript is not null)
            _observedTranscript.CollectionChanged -= OnTranscriptCollectionChanged;
        _observedTranscript = (DataContext as CallPageViewModel)?.Transcript;
        if (_observedTranscript is not null)
            _observedTranscript.CollectionChanged += OnTranscriptCollectionChanged;
    }

    private void OnTranscriptCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        TranscriptScroller.ScrollToEnd();

    private async void OnPushToTalkPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not CallPageViewModel viewModel) return;
        e.Pointer.Capture(sender as Control);
        e.Handled = true;
        await viewModel.BeginPushToTalkAsync();
    }

    private async void OnPushToTalkReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is not CallPageViewModel viewModel) return;
        e.Pointer.Capture(null);
        e.Handled = true;
        await viewModel.EndPushToTalkAsync();
    }

    private async void OnPushToTalkCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (DataContext is CallPageViewModel viewModel)
            await viewModel.EndPushToTalkAsync();
    }
}

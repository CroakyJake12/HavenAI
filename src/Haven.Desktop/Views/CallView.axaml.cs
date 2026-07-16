using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views;

public partial class CallView : UserControl
{
    private INotifyCollectionChanged? _observedTranscript;
    private Button? _voicePreviewButton;
    private CancellationTokenSource? _voicePreviewCancellation;

    public CallView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CallPageViewModel viewModel) return;
        await viewModel.InitializeAsync();
        EnsureVoicePreview(viewModel);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_observedTranscript is not null)
            _observedTranscript.CollectionChanged -= OnTranscriptCollectionChanged;
        _observedTranscript = (DataContext as CallPageViewModel)?.Transcript;
        if (_observedTranscript is not null)
            _observedTranscript.CollectionChanged += OnTranscriptCollectionChanged;
        _voicePreviewButton = null;
    }

    private void EnsureVoicePreview(CallPageViewModel viewModel)
    {
        if (_voicePreviewButton is not null) return;
        var voiceSelector = this.GetVisualDescendants()
            .OfType<ComboBox>()
            .FirstOrDefault(combo => ReferenceEquals(combo.ItemsSource, viewModel.Voices));
        if (voiceSelector?.Parent is not StackPanel voicePanel) return;

        var preview = new Button
        {
            Content = "Preview selected voice",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };
        preview.Classes.Add("secondary");
        preview.Click += OnPreviewVoiceClicked;
        var selectorIndex = voicePanel.Children.IndexOf(voiceSelector);
        voicePanel.Children.Insert(Math.Min(selectorIndex + 1, voicePanel.Children.Count), preview);
        _voicePreviewButton = preview;
    }

    private async void OnPreviewVoiceClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CallPageViewModel viewModel || _voicePreviewButton is null) return;
        var services = App.Services;
        if (services is null) return;

        _voicePreviewCancellation?.Cancel();
        _voicePreviewCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _voicePreviewCancellation = cancellation;
        _voicePreviewButton.IsEnabled = false;
        _voicePreviewButton.Content = "Playing preview…";
        try
        {
            await services.GetRequiredService<CallVoicePreviewController>().PreviewAsync(
                viewModel.SelectedVoice,
                viewModel.SelectedOutputDevice?.Id,
                cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Starting another preview or leaving Call intentionally stops playback.
        }
        catch (Exception ex)
        {
            services.GetRequiredService<NotificationService>().Show(
                "Voice preview unavailable",
                ex.Message,
                ToastKind.Warning,
                TimeSpan.FromSeconds(8));
        }
        finally
        {
            if (ReferenceEquals(_voicePreviewCancellation, cancellation))
            {
                _voicePreviewCancellation = null;
                if (_voicePreviewButton is not null)
                {
                    _voicePreviewButton.IsEnabled = true;
                    _voicePreviewButton.Content = "Preview selected voice";
                }
            }
            cancellation.Dispose();
        }
    }

    private async void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        var cancellation = _voicePreviewCancellation;
        _voicePreviewCancellation = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
        var services = App.Services;
        if (services is null) return;
        try
        {
            await services.GetRequiredService<CallVoicePreviewController>().StopAsync(CancellationToken.None);
        }
        catch (ObjectDisposedException)
        {
            // Shutdown may dispose the singleton before the visual tree detaches.
        }
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

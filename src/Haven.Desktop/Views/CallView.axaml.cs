using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views;

public partial class CallView : UserControl
{
    private INotifyCollectionChanged? _observedTranscript;
    private Button? _voicePreviewButton;
    private bool _previewingVoice;

    public CallView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
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
        if (_previewingVoice
            || DataContext is not CallPageViewModel viewModel
            || viewModel.SelectedVoice is null
            || App.Services?.GetService<ISpeechOutputService>() is not { } output)
            return;

        try
        {
            _previewingVoice = true;
            if (_voicePreviewButton is not null)
            {
                _voicePreviewButton.IsEnabled = false;
                _voicePreviewButton.Content = "Playing preview…";
            }
            await output.SpeakAsync(
                $"Hello. This is {viewModel.SelectedVoice.Name}, ready for your Haven call.",
                viewModel.SelectedVoice.Id,
                viewModel.SelectedOutputDevice?.Id,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            ToolTip.SetTip(_voicePreviewButton, "Voice preview failed: " + ex.Message);
        }
        finally
        {
            _previewingVoice = false;
            if (_voicePreviewButton is not null)
            {
                _voicePreviewButton.IsEnabled = true;
                _voicePreviewButton.Content = "Preview selected voice";
            }
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

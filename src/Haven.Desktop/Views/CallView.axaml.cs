using System.Collections.Specialized;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views;

public partial class CallView : UserControl
{
    private INotifyCollectionChanged? _observedTranscript;
    private Button? _voicePreviewButton;
    private Button? _transcriptExportButton;
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
        App.Services?.GetService<CallCompletionController>();
        await viewModel.InitializeAsync();
        EnsureVoicePreview(viewModel);
        EnsureTranscriptExport(viewModel);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_observedTranscript is not null)
            _observedTranscript.CollectionChanged -= OnTranscriptCollectionChanged;
        _observedTranscript = (DataContext as CallPageViewModel)?.Transcript;
        if (_observedTranscript is not null)
            _observedTranscript.CollectionChanged += OnTranscriptCollectionChanged;
        _voicePreviewButton = null;
        _transcriptExportButton = null;
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

    private void EnsureTranscriptExport(CallPageViewModel viewModel)
    {
        if (_transcriptExportButton is not null) return;
        var startButton = this.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(button => string.Equals(button.Content as string, "Start local call", StringComparison.Ordinal));
        if (startButton?.Parent is not StackPanel setupPanel) return;
        var export = new Button
        {
            Content = "Export transcript",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            IsEnabled = viewModel.Transcript.Count > 0
        };
        export.Classes.Add("secondary");
        export.Click += OnExportTranscriptClicked;
        var startIndex = setupPanel.Children.IndexOf(startButton);
        setupPanel.Children.Insert(Math.Min(startIndex + 1, setupPanel.Children.Count), export);
        _transcriptExportButton = export;
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

    private async void OnExportTranscriptClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CallPageViewModel viewModel || viewModel.Transcript.Count == 0) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        try
        {
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Haven Call transcript",
                SuggestedFileName = $"haven-call-{DateTime.Now:yyyy-MM-dd-HHmm}.md",
                FileTypeChoices =
                [
                    new FilePickerFileType("Markdown") { Patterns = ["*.md"] },
                    new FilePickerFileType("Text") { Patterns = ["*.txt"] }
                ]
            });
            if (file is null) return;
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            await writer.WriteAsync(CallTranscriptExportFormatter.ToMarkdown(viewModel.Transcript, DateTimeOffset.Now));
            await writer.FlushAsync();
            App.Services?.GetService<NotificationService>()?.Show(
                "Transcript exported",
                file.Name,
                ToastKind.Info,
                TimeSpan.FromSeconds(6));
        }
        catch (Exception ex)
        {
            App.Services?.GetService<NotificationService>()?.Show(
                "Transcript export failed",
                ex.Message,
                ToastKind.Warning,
                TimeSpan.FromSeconds(8));
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

    private void OnTranscriptCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        TranscriptScroller.ScrollToEnd();
        if (_transcriptExportButton is not null && DataContext is CallPageViewModel viewModel)
            _transcriptExportButton.IsEnabled = viewModel.Transcript.Count > 0;
    }

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

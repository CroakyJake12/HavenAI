/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Views/CallView.axaml.cs, in the Desktop view layer, where Avalonia controls connect XAML interaction to view models.
 * What: This file owns CallView. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

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

/// <summary>
/// Represents call view and keeps its related state and behavior together.
/// </summary>
public partial class CallView : UserControl
{
    /// <summary>
    /// Stores observed transcript locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private INotifyCollectionChanged? _observedTranscript;
    /// <summary>
    /// Stores voice preview button locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private Button? _voicePreviewButton;
    /// <summary>
    /// Stores transcript export button locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private Button? _transcriptExportButton;
    /// <summary>
    /// Stores voice preview cancellation locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private CancellationTokenSource? _voicePreviewCancellation;

    public CallView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    /// <summary>
    /// Handles the loaded event raised by the UI or runtime.
    /// </summary>
    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CallPageViewModel viewModel) return;
        ObserveTranscript(viewModel);
        App.Services?.GetService<CallCompletionController>();
        await viewModel.InitializeAsync();
        EnsureVoicePreview(viewModel);
        EnsureTranscriptExport(viewModel);
    }

    /// <summary>
    /// Handles the data context changed event raised by the UI or runtime.
    /// </summary>
    private void OnDataContextChanged(object? sender, EventArgs e) =>
        ObserveTranscript(DataContext as CallPageViewModel);

    /// <summary>
    /// Performs the observe transcript step owned by this component.
    /// </summary>
    private void ObserveTranscript(CallPageViewModel? viewModel)
    {
        if (_observedTranscript is not null)
            _observedTranscript.CollectionChanged -= OnTranscriptCollectionChanged;
        _observedTranscript = viewModel?.Transcript;
        if (_observedTranscript is not null)
            _observedTranscript.CollectionChanged += OnTranscriptCollectionChanged;
    }

    /// <summary>
    /// Performs the ensure voice preview step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the ensure transcript export step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Handles the preview voice clicked event raised by the UI or runtime.
    /// </summary>
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

    /// <summary>
    /// Handles the export transcript clicked event raised by the UI or runtime.
    /// </summary>
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

    /// <summary>
    /// Handles the detached from visual tree event raised by the UI or runtime.
    /// </summary>
    private async void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ObserveTranscript(null);
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

    /// <summary>
    /// Handles the transcript collection changed event raised by the UI or runtime.
    /// </summary>
    private void OnTranscriptCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (TranscriptScroller.ItemCount > 0)
            TranscriptScroller.ScrollIntoView(TranscriptScroller.ItemCount - 1);
        if (_transcriptExportButton is not null && DataContext is CallPageViewModel viewModel)
            _transcriptExportButton.IsEnabled = viewModel.Transcript.Count > 0;
    }

    /// <summary>
    /// Handles the push to talk pressed event raised by the UI or runtime.
    /// </summary>
    private async void OnPushToTalkPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not CallPageViewModel viewModel) return;
        e.Pointer.Capture(sender as Control);
        e.Handled = true;
        await viewModel.BeginPushToTalkAsync();
    }

    /// <summary>
    /// Handles the push to talk released event raised by the UI or runtime.
    /// </summary>
    private async void OnPushToTalkReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is not CallPageViewModel viewModel) return;
        e.Pointer.Capture(null);
        e.Handled = true;
        await viewModel.EndPushToTalkAsync();
    }

    /// <summary>
    /// Handles the push to talk capture lost event raised by the UI or runtime.
    /// </summary>
    private async void OnPushToTalkCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (DataContext is CallPageViewModel viewModel)
            await viewModel.EndPushToTalkAsync();
    }
}

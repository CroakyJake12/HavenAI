using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Pages.Mail;

/// <summary>Compatibility host for Mail. All visible Mail UI is owned by Haven.UI.</summary>
public sealed class MailPage : ContentControl, IDisposable
{
    private readonly MailPageViewModel _viewModel;
    private readonly MailHavenScene _scene;
    private readonly HavenSceneControl _sceneHost;
    private bool _refreshQueued;
    private bool _disposed;

    public MailPage()
    {
        var services = App.Services ?? throw new InvalidOperationException("Haven services are not initialized.");
        _viewModel = new MailPageViewModel(
            services.GetRequiredService<IMailService>(),
            services.GetRequiredService<IProviderModelClient>());
        _scene = new MailHavenScene(_viewModel);
        _sceneHost = new HavenSceneControl { Root = _scene.Root };
        Content = _sceneHost;
        Background = Brushes.Transparent;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Observe(_viewModel.Accounts);
        Observe(_viewModel.Folders);
        Observe(_viewModel.Messages);
        Observe(_viewModel.ThreadMessages);
        Observe(_viewModel.ComposeAttachments);
        _scene.AttachRequested += OnAttachRequested;
        _scene.AttachmentDownloadRequested += OnAttachmentDownloadRequested;
        AttachedToVisualTree += OnAttached;
    }

    internal MailHavenScene Scene => _scene;
    internal HavenSceneControl SceneHost => _sceneHost;

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e) => QueueRefresh();
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) => QueueRefresh();
    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => QueueRefresh();

    private void Observe(INotifyCollectionChanged collection) => collection.CollectionChanged += OnCollectionChanged;
    private void Unobserve(INotifyCollectionChanged collection) => collection.CollectionChanged -= OnCollectionChanged;

    private void QueueRefresh()
    {
        if (_disposed || _refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _refreshQueued = false;
            if (!_disposed) _scene.Refresh();
        }, DispatcherPriority.Background);
    }

    private async void OnAttachRequested(object? sender, EventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        try
        {
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Attach files to email",
                AllowMultiple = true
            });
            foreach (var file in files)
            {
                await using var stream = await file.OpenReadAsync();
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory);
                _viewModel.AddComposeAttachment(file.Name, GuessContentType(file.Name), memory.ToArray());
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async void OnAttachmentDownloadRequested(object? sender, MailAttachmentDescriptor attachment)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        try
        {
            var bytes = await _viewModel.DownloadAttachmentAsync(attachment, CancellationToken.None);
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save mail attachment",
                SuggestedFileName = attachment.FileName,
                ShowOverwritePrompt = true
            });
            if (file is null) return;
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await stream.WriteAsync(bytes);
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (MailProviderException) { }
    }

    private static string GuessContentType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".txt" => "text/plain",
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".csv" => "text/csv",
        ".json" => "application/json",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        _ => "application/octet-stream"
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        AttachedToVisualTree -= OnAttached;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        Unobserve(_viewModel.Accounts);
        Unobserve(_viewModel.Folders);
        Unobserve(_viewModel.Messages);
        Unobserve(_viewModel.ThreadMessages);
        Unobserve(_viewModel.ComposeAttachments);
        _scene.AttachRequested -= OnAttachRequested;
        _scene.AttachmentDownloadRequested -= OnAttachmentDownloadRequested;
        _sceneHost.Root = null;
        _scene.Dispose();
        _viewModel.Dispose();
        GC.SuppressFinalize(this);
    }
}

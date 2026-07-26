using System.Collections.Specialized;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Haven.Desktop.Views.Shell.NativePresentation;

internal sealed partial class NativeProjectsPage
{
    private bool _refreshPending;

    private async void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        AttachNotifications();
        await RefreshProjectsAsync(refreshSource: false);
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        DetachNotifications();
    }

    private void AttachNotifications()
    {
        DetachNotifications();

        _notifySource = NativePresentationReflection.NotifySource(_source);
        if (_notifySource is not null)
        {
            _notifySource.PropertyChanged += OnSourcePropertyChanged;
        }

        _observedCollection = FindProjectCollection();
        _notifyCollection = _observedCollection as INotifyCollectionChanged;
        if (_notifyCollection is not null)
        {
            _notifyCollection.CollectionChanged += OnProjectCollectionChanged;
        }
    }

    private void DetachNotifications()
    {
        if (_notifySource is not null)
        {
            _notifySource.PropertyChanged -= OnSourcePropertyChanged;
            _notifySource = null;
        }

        if (_notifyCollection is not null)
        {
            _notifyCollection.CollectionChanged -= OnProjectCollectionChanged;
            _notifyCollection = null;
        }

        _observedCollection = null;
    }

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        QueueRefresh();
    }

    private void OnProjectCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (_disposed)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () => _ = RefreshProjectsAsync(refreshSource: false),
            DispatcherPriority.Background);
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        _ = RefreshProjectsAsync(refreshSource: false);
    }

    private async void OnRefreshClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await RefreshProjectsAsync(refreshSource: true);
    }

    private async void OnNewProjectClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await OpenCreatorAsync();
    }

    private async Task OpenCreatorAsync()
    {
        if (_disposed)
        {
            return;
        }

        _newProjectButton.IsEnabled = false;
        SetStatus("Opening the project creator…");

        try
        {
            var handled = await NativePresentationReflection.ExecuteCommandAsync(
                _source,
                null,
                "NewProjectCommand",
                "CreateProjectCommand",
                "OpenProjectCreatorCommand",
                "SwitchToCreateCommand");

            if (!handled)
            {
                var invocation = await NativePresentationReflection.InvokeAsync(
                    _source,
                    ["OpenProjectCreatorAsync", "OpenProjectCreator", "CreateProjectAsync", "SwitchToCreate"],
                    Array.Empty<object?>());
                handled = invocation.Invoked;
            }

            if (!handled)
            {
                await _openCreator();
            }

            SetStatus(string.Empty);
            ProjectCreatorOpened?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetStatus($"The project creator could not be opened: {ex.Message}", isError: true);
        }
        finally
        {
            if (!_disposed)
            {
                _newProjectButton.IsEnabled = true;
            }
        }
    }

    private async Task ConnectExistingAsync()
    {
        if (_disposed)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
        {
            SetStatus("Folder selection is unavailable on this device.", isError: true);
            return;
        }

        try
        {
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "Connect an existing project folder",
                    AllowMultiple = false
                });

            var folder = folders.FirstOrDefault();
            if (folder is null)
            {
                return;
            }

            using (folder)
            {
                var path = folder.TryGetLocalPath();
                if (string.IsNullOrWhiteSpace(path))
                {
                    SetStatus("The selected folder does not expose a local path and cannot be connected.", isError: true);
                    return;
                }

                SetStatus("Connecting the selected project folder…");

                var handled = await NativePresentationReflection.ExecuteCommandAsync(
                    _source,
                    path,
                    "ConnectExistingFolderCommand",
                    "ConnectFolderCommand",
                    "AddExistingProjectCommand",
                    "ImportProjectCommand");

                if (!handled)
                {
                    var invocation = await NativePresentationReflection.InvokeAsync(
                        _source,
                        ["AddPathAsync", "ConnectExistingAsync", "ConnectFolderAsync", "AddExistingProjectAsync", "ImportProjectAsync"],
                        path);
                    handled = invocation.Invoked;
                }

                if (!handled)
                {
                    await _openCreator();
                    SetStatus("The project creator was opened. Choose the existing-folder option to finish connecting it.");
                    ProjectCreatorOpened?.Invoke(this, EventArgs.Empty);
                    return;
                }
            }

            AttachNotifications();
            await RefreshProjectsAsync(refreshSource: false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetStatus($"The folder could not be connected: {ex.Message}", isError: true);
        }
    }

    private async Task RefreshProjectsAsync(bool refreshSource)
    {
        if (_disposed)
        {
            return;
        }

        if (_refreshing)
        {
            _refreshPending = true;
            return;
        }

        _refreshing = true;
        _refreshButton.IsEnabled = false;

        try
        {
            do
            {
                _refreshPending = false;

                if (refreshSource)
                {
                    SetStatus("Refreshing projects…");
                    await RefreshSourceAsync();
                    refreshSource = false;
                    AttachNotifications();
                }

                var rows = await ReadRowsAsync(_lifetime.Token);
                RenderRows(rows);
            }
            while (_refreshPending && !_disposed);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetStatus($"Projects could not be refreshed: {ex.Message}", isError: true);
        }
        finally
        {
            _refreshing = false;
            if (!_disposed)
            {
                _refreshButton.IsEnabled = true;
            }
        }
    }

    private async Task RefreshSourceAsync()
    {
        var handled = await NativePresentationReflection.ExecuteCommandAsync(
            _source,
            null,
            "RefreshProjectsCommand",
            "RefreshCommand",
            "ReloadCommand",
            "LoadProjectsCommand");

        if (handled)
        {
            return;
        }

        await NativePresentationReflection.InvokeAsync(
            _source,
            ["RefreshAsync", "ReloadAsync", "LoadProjectsAsync", "LoadAsync"],
            Array.Empty<object?>());
    }
}

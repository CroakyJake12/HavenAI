using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Events;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views.Pages.ContainerSettings;

public sealed partial class ContainerSettingsPage : UserControl
{
    private readonly ContainerItemViewModel _item;
    private readonly IContainerRepository _repository;
    private readonly Func<Task> _saved;
    private readonly Func<Task>? _closed;
    private string _name;
    private string _rootPath;
    private string _context;
    private string _instructions;
    private string _status = "Changes are stored locally.";
    private bool _isDeleted;
    private bool _isDeleteConfirming;

    public new event PropertyChangedEventHandler? PropertyChanged;

    public ContainerSettingsPage(
        HavenEventBus? bus,
        ContainerItemViewModel item,
        IContainerRepository repository,
        Func<Task> saved,
        Func<Task>? closed = null)
    {
        _item = item;
        _repository = repository;
        _saved = saved;
        _closed = closed;
        _name = item.Definition.Name;
        _rootPath = item.Definition.RootPath ?? string.Empty;
        _context = item.Definition.Context;
        _instructions = item.Definition.Instructions;
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsDeleted && !string.IsNullOrWhiteSpace(Name));
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => !IsDeleted);
        ArchiveCommand = new AsyncRelayCommand(ArchiveAsync, () => !IsDeleted);
        RequestDeleteCommand = new RelayCommand(() => IsDeleteConfirming = true);
        CancelDeleteCommand = new RelayCommand(() => IsDeleteConfirming = false);
        DiscardChangesCommand = new AsyncRelayCommand(DiscardChangesAsync);
        InitializeComponent();
        DataContext = this;
        if (bus is not null) WireEvents(bus);
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void RaisePropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public string Eyebrow => _item.Definition.Mode switch { HavenMode.Study => "SUBJECT SETTINGS", HavenMode.Tasks => "TASK GROUP SETTINGS", _ => "PROJECT SETTINGS" };
    public string ItemLabel => _item.Definition.Mode switch { HavenMode.Chat => "chat group", HavenMode.Study => "subject", HavenMode.Tasks => "task group", _ => "project" };
    public string ArchiveLabel => "Archive " + ItemLabel;
    public string DeleteLabel => "Delete " + ItemLabel;
    public new string Name { get => _name; set { if (SetProperty(ref _name, value)) SaveCommand.RaiseCanExecuteChanged(); } }
    public string RootPath { get => _rootPath; set => SetProperty(ref _rootPath, value); }
    public string Context { get => _context; set => SetProperty(ref _context, value); }
    public string Instructions { get => _instructions; set => SetProperty(ref _instructions, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool IsDeleted { get => _isDeleted; private set { if (!SetProperty(ref _isDeleted, value)) return; SaveCommand.RaiseCanExecuteChanged(); DeleteCommand.RaiseCanExecuteChanged(); ArchiveCommand.RaiseCanExecuteChanged(); } }
    public bool IsDeleteConfirming { get => _isDeleteConfirming; private set => SetProperty(ref _isDeleteConfirming, value); }
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }
    public AsyncRelayCommand ArchiveCommand { get; }
    public RelayCommand RequestDeleteCommand { get; }
    public RelayCommand CancelDeleteCommand { get; }
    public AsyncRelayCommand DiscardChangesCommand { get; }

    public void SetRootPath(string path) => RootPath = path;

    private async Task SaveAsync()
    {
        try
        {
            var definition = _item.Definition with
            {
                Name = Name.Trim(),
                RootPath = string.IsNullOrWhiteSpace(RootPath) ? null : Path.GetFullPath(RootPath.Trim()),
                Context = Context.Trim(),
                Instructions = Instructions.Trim(),
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await _repository.UpsertAsync(definition, CancellationToken.None);
            await _saved();
            Status = "Project settings saved.";
            if (_closed is not null) await _closed();
        }
        catch (Exception ex)
        {
            Status = $"Could not save settings: {ex.Message}";
        }
    }

    private async Task DiscardChangesAsync()
    {
        Name = _item.Definition.Name;
        RootPath = _item.Definition.RootPath ?? string.Empty;
        Context = _item.Definition.Context;
        Instructions = _item.Definition.Instructions;
        Status = "Changes discarded.";
        if (_closed is not null) await _closed();
    }

    private async Task DeleteAsync()
    {
        try
        {
            await _repository.DeleteAsync(_item.Id, CancellationToken.None);
            IsDeleted = true;
            await _saved();
            Status = "Project deleted. Its saved conversations remain in history.";
        }
        catch (Exception ex)
        {
            Status = $"Could not delete project: {ex.Message}";
        }
    }

    private async Task ArchiveAsync()
    {
        try
        {
            await _repository.UpsertAsync(_item.Definition with { IsArchived = true, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
            IsDeleted = true;
            await _saved();
            Status = $"{string.Concat(char.ToUpperInvariant(ItemLabel[0]), ItemLabel[1..])} archived. Restore it from Archive when needed.";
        }
        catch (Exception ex) { Status = $"Could not archive {ItemLabel}: {ex.Message}"; }
    }

    public IReadOnlyDictionary<string, Button> GetActionButtons() => InnerView.GetActionButtons();

    private void WireEvents(HavenEventBus bus)
    {
        var buttons = InnerView.GetActionButtons();

        bus.RegisterElement("ContainerSettings.Actions.Save", buttons["Save"]);
        bus.WirePointerEvents("ContainerSettings.Actions.Save", buttons["Save"]);
        buttons["Save"].Click += async (_, _) =>
        {
            bus.Fire("ContainerSettings.Actions.Save");
            if (SaveCommand.CanExecute(null)) await SaveCommand.ExecuteAsync();
        };

        bus.RegisterElement("ContainerSettings.Actions.Archive", buttons["Archive"]);
        bus.WirePointerEvents("ContainerSettings.Actions.Archive", buttons["Archive"]);
        buttons["Archive"].Click += async (_, _) =>
        {
            bus.Fire("ContainerSettings.Actions.Archive");
            if (ArchiveCommand.CanExecute(null)) await ArchiveCommand.ExecuteAsync();
        };

        bus.RegisterElement("ContainerSettings.Actions.RequestDelete", buttons["Delete"]);
        bus.WirePointerEvents("ContainerSettings.Actions.RequestDelete", buttons["Delete"]);
        buttons["Delete"].Click += (_, _) =>
        {
            bus.Fire("ContainerSettings.Actions.RequestDelete");
            if (RequestDeleteCommand.CanExecute(null)) RequestDeleteCommand.Execute(null);
        };

        bus.RegisterElement("ContainerSettings.Actions.CancelDelete", buttons["CancelDelete"]);
        bus.WirePointerEvents("ContainerSettings.Actions.CancelDelete", buttons["CancelDelete"]);
        buttons["CancelDelete"].Click += (_, _) =>
        {
            bus.Fire("ContainerSettings.Actions.CancelDelete");
            if (CancelDeleteCommand.CanExecute(null)) CancelDeleteCommand.Execute(null);
        };

        bus.RegisterElement("ContainerSettings.Actions.Delete", buttons["ConfirmDelete"]);
        bus.WirePointerEvents("ContainerSettings.Actions.Delete", buttons["ConfirmDelete"]);
        buttons["ConfirmDelete"].Click += async (_, _) =>
        {
            bus.Fire("ContainerSettings.Actions.Delete");
            if (DeleteCommand.CanExecute(null)) await DeleteCommand.ExecuteAsync();
        };

        bus.RegisterElement("ContainerSettings.Actions.Discard", buttons["Discard"]);
        bus.WirePointerEvents("ContainerSettings.Actions.Discard", buttons["Discard"]);
        buttons["Discard"].Click += async (_, _) =>
        {
            bus.Fire("ContainerSettings.Actions.Discard");
            if (DiscardChangesCommand.CanExecute(null)) await DiscardChangesCommand.ExecuteAsync();
        };
    }
}

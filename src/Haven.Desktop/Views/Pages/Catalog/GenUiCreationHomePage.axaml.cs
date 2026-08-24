using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Events;

namespace Haven.Desktop.Views.Pages.Catalog;

public sealed partial class GenUiCreationHomePage : UserControl, IDisposable
{
    private readonly HavenEventBus _bus;
    private readonly IGenUiAppRepository _apps;
    private readonly GenUiAppSessionService _sessions;
    private readonly GenerativeUiEventRouter _router;
    private readonly GenUiInstanceStore _instances;
    private readonly Func<string, Task> _generate;
    private GenerativeUiSurface? _surface;
    private bool _disposed;

    public GenUiCreationHomePage(HavenEventBus bus, IGenUiAppRepository apps, GenUiAppSessionService sessions,
        GenerativeUiEventRouter router, GenUiInstanceStore instances, Func<string, Task> generate)
    {
        _bus = bus; _apps = apps; _sessions = sessions; _router = router; _instances = instances; _generate = generate;
        InitializeComponent();
        Register("GenUiCreate.Prompt", PromptBox);
        Register("GenUiCreate.Generate", GenerateButton);
        Register("GenUiCreate.OpenExisting", RefreshButton);
        Register("GenUiCreate.Import", ImportButton);
        GenerateButton.Click += async (_, _) => await GenerateAsync();
        RefreshButton.Click += async (_, _) => await RefreshAsync();
        ImportButton.Click += async (_, _) => await ImportAsync();
        PopulateExamples();
        _ = RefreshAsync();
    }

    private void PopulateExamples()
    {
        ExamplesPanel.Children.Clear();
        foreach (var example in GenUiFirstTurnBenchmarkCatalog.Cases.Take(8))
        {
            var captured = example;
            var button = new HavenButton
            {
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(12, 10),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock { Text = DisplayName(captured.Id), FontWeight = Avalonia.Media.FontWeight.ExtraBold },
                        new TextBlock { Text = captured.Prompt, Classes = { "muted" }, FontSize = 10, TextWrapping = Avalonia.Media.TextWrapping.Wrap }
                    }
                }
            };
            button.Classes.Add("sidebar");
            button.Click += (_, _) => PromptBox.Text = captured.Prompt;
            ExamplesPanel.Children.Add(button);
        }
    }

    private async Task GenerateAsync()
    {
        var prompt = (PromptBox.Text ?? string.Empty).Trim();
        if (prompt.Length == 0) { StatusText.Text = "Describe what you want Haven to create first."; return; }
        StatusText.Text = "Opening a generation thread…";
        await _generate(prompt);
    }
    private async Task RefreshAsync()
    {
        if (_disposed) return;
        try
        {
            var pinnedTask = _apps.GetPinnedAsync(8, CancellationToken.None);
            var recentTask = _apps.GetRecentAsync(12, CancellationToken.None);
            await Task.WhenAll(pinnedTask, recentTask);
            var pinned = await pinnedTask;
            var recent = await recentTask;
            var pinnedIds = pinned.Select(app => app.Document.Origin.InstanceId).ToHashSet();
            PopulateApps(PinnedPanel, pinned, pinnedIds);
            PopulateApps(RecentPanel, recent, pinnedIds);
            PinnedEmptyText.IsVisible = pinned.Count == 0;
            RecentEmptyText.IsVisible = recent.Count == 0;
            StatusText.Text = recent.Count == 0 ? "No saved generated apps yet." : $"{recent.Count} recent generated app(s).";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            StatusText.Text = "Could not load generated apps: " + ex.Message;
        }
    }

    private void PopulateApps(StackPanel panel, IReadOnlyList<GenUiAppDefinition> apps, HashSet<Guid> pinnedIds)
    {
        panel.Children.Clear();
        foreach (var app in apps)
        {
            var captured = app;
            var instanceId = app.Document.Origin.InstanceId;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            var open = new HavenButton
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 8),
                Content = new StackPanel
                {
                    Spacing = 1,
                    Children =
                    {
                        new TextBlock { Text = captured.Document.Title, FontWeight = Avalonia.Media.FontWeight.ExtraBold },
                        new TextBlock { Text = $"{captured.AppId} · {captured.Document.UpdatedAt.LocalDateTime:g}", Classes = { "muted" }, FontSize = 10 }
                    }
                }
            };
            open.Classes.Add("sidebar");
            open.Click += async (_, _) => await OpenExistingAsync(instanceId);
            var pin = new HavenButton { Content = pinnedIds.Contains(instanceId) ? "Unpin" : "Pin", Classes = { "chrome" } };
            Grid.SetColumn(pin, 1);
            pin.Click += async (_, _) => { await _apps.SetPinnedAsync(instanceId, !pinnedIds.Contains(instanceId), CancellationToken.None); await RefreshAsync(); };
            row.Children.Add(open); row.Children.Add(pin); panel.Children.Add(row);
        }
    }

    private async Task OpenExistingAsync(Guid instanceId)
    {
        try
        {
            var app = await _sessions.OpenAsync(instanceId, CancellationToken.None);
            if (app is null) { StatusText.Text = "That generated app no longer exists."; return; }
            _surface?.Dispose();
            _surface = new GenerativeUiSurface(_router, _instances) { HorizontalAlignment = HorizontalAlignment.Stretch };
            _surface.PresentExisting(app.Document);
            ExistingTitle.Text = app.Document.Title;
            ExistingHost.Content = _surface;
            ExistingHostCard.IsVisible = true;
            StatusText.Text = $"Opened {app.Document.Title}.";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            StatusText.Text = "Could not open generated app: " + ex.Message;
        }
    }
    private async Task ImportAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null) { StatusText.Text = "File import is unavailable on this surface."; return; }
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import generated UI definition", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Haven GenUI JSON") { Patterns = ["*.json"] }]
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is null) return;
        try
        {
            var json = await File.ReadAllTextAsync(path);
            var definition = JsonSerializer.Deserialize<GenUiAppDefinition>(json) ?? throw new InvalidDataException("The file does not contain a GenUI app definition.");
            var validation = GenUiSemanticValidator.ValidateAndRepair(definition);
            if (!validation.IsValid) throw new InvalidDataException(string.Join(" ", validation.Errors));
            await _sessions.SaveAsync(validation.Definition, CancellationToken.None);
            await RefreshAsync();
            await OpenExistingAsync(validation.Definition.Document.Origin.InstanceId);
            StatusText.Text = $"Imported {validation.Definition.Document.Title}.";
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or InvalidOperationException)
        {
            StatusText.Text = "Import failed: " + ex.Message;
        }
    }

    private static string DisplayName(string id) => string.Join(' ', id.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    private void Register(string name, Control control) { _bus.RegisterElement(name, control); _bus.WirePointerEvents(name, control); }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _surface?.Dispose();
        _surface = null;
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Events;
using Haven.Desktop.HavenUI.Components.Buttons;

namespace Haven.Desktop.Views.Pages.Archive;

public sealed partial class ArchivePage : UserControl
{
    private readonly HavenEventBus _bus;
    private readonly HavenMode _mode;
    private readonly IConversationRepository _conversations;
    private readonly IContainerRepository _containers;

    public ArchivePage(HavenEventBus bus, HavenMode mode, IConversationRepository conversations, IContainerRepository containers)
    {
        _bus = bus;
        _mode = mode;
        _conversations = conversations;
        _containers = containers;

        InitializeComponent();
        TitleText.Text = _mode == HavenMode.Chat ? "Archived Conversations" : "Archived Subjects";
        WireEvents();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e) => _ = RefreshAsync();

    private void WireEvents()
    {
        _bus.RegisterElement("Archive.Actions.Refresh", RefreshButton);
        _bus.WirePointerEvents("Archive.Actions.Refresh", RefreshButton);
        RefreshButton.Click += async (_, _) =>
        {
            _bus.Fire("Archive.Actions.Refresh");
            await RefreshAsync();
        };
    }

    private async Task RefreshAsync()
    {
        ItemsPanel.Children.Clear();
        StatusText.Text = "Loading…";

        try
        {
            if (_mode == HavenMode.Study)
            {
                var subjects = await _containers.GetArchivedByModeAsync(_mode, 100, CancellationToken.None);
                foreach (var subject in subjects)
                    ItemsPanel.Children.Add(CreateSubjectCard(subject));
            }
            else
            {
                var conversations = await _conversations.GetArchivedAsync(_mode, 100, CancellationToken.None);
                foreach (var conv in conversations)
                    ItemsPanel.Children.Add(CreateConversationCard(conv));
            }

            StatusText.Text = ItemsPanel.Children.Count == 0
                ? "No archived items."
                : $"{ItemsPanel.Children.Count} archived item{(ItemsPanel.Children.Count == 1 ? "" : "s")}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to load: {ex.Message}";
        }
    }

    private Border CreateConversationCard(Conversation conv)
    {
        var qName = $"Archive.List.Item{ItemsPanel.Children.Count}";

        var titleBlock = new TextBlock { Text = conv.Title, FontWeight = Avalonia.Media.FontWeight.SemiBold };
        var kindText = conv.Kind.ToString();
        var updatedText = conv.UpdatedAt.ToLocalTime().ToString("g");
        var metaText = new TextBlock { Text = $"{kindText} · {updatedText}", Classes = { "muted" } };
        var metaStack = new StackPanel { Children = { titleBlock, metaText } };

        var restoreButton = new HavenButton { Content = "Restore", Classes = { "accent" } };
        var deleteButton = new HoldToConfirmButton { Content = "Delete forever", ActionLabel = "delete forever" };

        restoreButton.RegisterWithEvents($"{qName}.Restore", _bus);
        restoreButton.Click += async (_, _) =>
        {
            _bus.Fire($"{qName}.Restore");
            await RestoreConversationAsync(conv);
        };

        deleteButton.RegisterWithEvents($"{qName}.Delete", _bus);
        deleteButton.Click += async (_, _) =>
        {
            _bus.Fire($"{qName}.Delete");
            await DeleteConversationAsync(conv);
        };

        var buttonStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        buttonStack.Children.Add(restoreButton);
        buttonStack.Children.Add(deleteButton);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        grid.Children.Add(metaStack);
        Grid.SetColumn(buttonStack, 1);
        grid.Children.Add(buttonStack);

        var border = new HavenAdaptiveSurface { Classes = { "card" }, Child = grid };
        border.PointerEntered += (_, _) => _bus.Fire($"{qName}.Hover");
        border.PointerExited += (_, _) => _bus.Fire($"{qName}.Leave");
        return border;
    }

    private Border CreateSubjectCard(ContainerDefinition subject)
    {
        var qName = $"Archive.List.Item{ItemsPanel.Children.Count}";

        var titleBlock = new TextBlock { Text = subject.Name, FontWeight = Avalonia.Media.FontWeight.SemiBold };
        var updatedText = subject.UpdatedAt.ToLocalTime().ToString("g");
        var metaText = new TextBlock { Text = $"Subject · {updatedText}", Classes = { "muted" } };
        var metaStack = new StackPanel { Children = { titleBlock, metaText } };

        var restoreButton = new HavenButton { Content = "Restore", Classes = { "accent" } };
        var deleteButton = new HoldToConfirmButton { Content = "Delete forever", ActionLabel = "delete forever" };

        restoreButton.RegisterWithEvents($"{qName}.Restore", _bus);
        restoreButton.Click += async (_, _) =>
        {
            _bus.Fire($"{qName}.Restore");
            await RestoreSubjectAsync(subject);
        };

        deleteButton.RegisterWithEvents($"{qName}.Delete", _bus);
        deleteButton.Click += async (_, _) =>
        {
            _bus.Fire($"{qName}.Delete");
            await DeleteSubjectAsync(subject);
        };

        var buttonStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        buttonStack.Children.Add(restoreButton);
        buttonStack.Children.Add(deleteButton);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        grid.Children.Add(metaStack);
        Grid.SetColumn(buttonStack, 1);
        grid.Children.Add(buttonStack);

        var border = new HavenAdaptiveSurface { Classes = { "card" }, Child = grid };
        border.PointerEntered += (_, _) => _bus.Fire($"{qName}.Hover");
        border.PointerExited += (_, _) => _bus.Fire($"{qName}.Leave");
        return border;
    }

    private async Task RestoreConversationAsync(Conversation conv)
    {
        try
        {
            await _conversations.UpsertConversationAsync(conv with { IsArchived = false, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
            await RefreshAsync();
            StatusText.Text = $"Restored \"{conv.Title}\".";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Restore failed: {ex.Message}";
        }
    }

    private async Task DeleteConversationAsync(Conversation conv)
    {
        try
        {
            await _conversations.DeleteConversationAsync(conv.Id, CancellationToken.None);
            await RefreshAsync();
            StatusText.Text = $"Deleted \"{conv.Title}\" forever.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Delete failed: {ex.Message}";
        }
    }

    private async Task RestoreSubjectAsync(ContainerDefinition subject)
    {
        try
        {
            await _containers.UpsertAsync(subject with { IsArchived = false, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
            await RefreshAsync();
            StatusText.Text = $"Restored \"{subject.Name}\".";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Restore failed: {ex.Message}";
        }
    }

    private async Task DeleteSubjectAsync(ContainerDefinition subject)
    {
        try
        {
            await _containers.DeleteAndDetachConversationsAsync(subject.Id, CancellationToken.None);
            await RefreshAsync();
            StatusText.Text = $"Deleted \"{subject.Name}\" forever.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Delete failed: {ex.Message}";
        }
    }
}

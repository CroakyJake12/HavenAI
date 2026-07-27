using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Events;

namespace Haven.Desktop.Views.Pages.Home;

/// <summary>
/// Mockup-native New Haven dashboard. The pinned shelf comes from the pin store;
/// every later row is derived from recent mode usage and conversation activity.
/// </summary>
public sealed partial class NewDashboardPage : UserControl, IDisposable
{
    private readonly HavenEventBus _bus;
    private readonly IModeRegistry _modes;
    private readonly IModeUsageRepository _usage;
    private readonly IPinRepository _pins;
    private readonly IConversationRepository _conversations;
    private readonly DispatcherTimer _clock;
    private bool _disposed;

    public NewDashboardPage(
        HavenEventBus bus,
        IModeRegistry modes,
        IModeUsageRepository usage,
        IPinRepository pins,
        IConversationRepository conversations)
    {
        _bus = bus;
        _modes = modes;
        _usage = usage;
        _pins = pins;
        _conversations = conversations;
        InitializeComponent();

        _clock = new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background, (_, _) => RefreshClock());
        EditPinnedButton.Click += (_, _) => ManageAppsRequested?.Invoke(this, EventArgs.Empty);
        EditWithHavenButton.Click += (_, _) => EditWithHavenRequested?.Invoke(this, EventArgs.Empty);
        Register("Dashboard.Pinned.Edit", EditPinnedButton);
        Register("Dashboard.EditWithHaven", EditWithHavenButton);
        RefreshClock();
    }

    public event EventHandler<ModeDefinition>? ModeRequested;
    public event EventHandler<Conversation>? ConversationRequested;
    public event EventHandler? ManageAppsRequested;
    public event EventHandler? EditWithHavenRequested;

    public async Task ActivateAsync(CancellationToken cancellationToken)
    {
        if (_disposed) return;
        _clock.Start();
        await RefreshAsync(cancellationToken);
    }

    public void Deactivate() => _clock.Stop();

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var modesTask = _modes.GetModesAsync(cancellationToken);
        var pinsTask = _pins.GetPinsAsync(cancellationToken);
        var usageTask = _usage.GetRecentUsageAsync(90, cancellationToken);
        var conversationsTask = _conversations.GetRecentAsync(null, 120, cancellationToken);
        await Task.WhenAll(modesTask, pinsTask, usageTask, conversationsTask);

        var modes = (await modesTask).Where(mode => mode.IsEnabled).ToArray();
        var pins = await pinsTask;
        var usage = await usageTask;
        var conversations = (await conversationsTask).Where(item => !item.IsArchived).ToArray();
        var modeById = modes.ToDictionary(mode => mode.Id);

        PinnedPanel.Children.Clear();
        foreach (var pin in pins.OrderBy(pin => pin.SortOrder))
        {
            if (modeById.TryGetValue(pin.ModeId, out var mode))
                PinnedPanel.Children.Add(BuildModeCard(mode, "Pinned app"));
        }
        PinnedEmptyText.IsVisible = PinnedPanel.Children.Count == 0;

        var usageByMode = usage
            .GroupBy(item => item.ModeId)
            .ToDictionary(group => group.Key, group => group.Sum(item =>
                item.TurnCount * 4d + item.CompletionCount * 6d + Math.Min(item.TotalDuration.TotalMinutes, 240d) / 4d));
        var recentByBaseMode = conversations
            .GroupBy(item => item.Mode)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.UpdatedAt).ToArray());

        var ranked = modes
            .Select(mode => new
            {
                Mode = mode,
                Score = usageByMode.GetValueOrDefault(mode.Id)
                        + recentByBaseMode.GetValueOrDefault(mode.BaseMode, []).Length * 3d
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Mode.Name, StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToArray();

        DynamicRowsPanel.Children.Clear();
        foreach (var item in ranked)
        {
            var row = new StackPanel { Spacing = 12 };
            row.Children.Add(new TextBlock
            {
                Text = $"Continue with {item.Mode.Name}",
                FontSize = 16,
                FontWeight = FontWeight.ExtraBold
            });

            var cards = new WrapPanel { Orientation = Orientation.Horizontal, ItemWidth = 220, ItemHeight = 148 };
            cards.Children.Add(BuildModeCard(item.Mode, $"{Math.Round(item.Score):0} recent activity points"));
            foreach (var conversation in recentByBaseMode.GetValueOrDefault(item.Mode.BaseMode, []).Take(4))
                cards.Children.Add(BuildConversationCard(conversation, item.Mode));
            row.Children.Add(cards);
            DynamicRowsPanel.Children.Add(row);
        }

        if (ranked.Length == 0)
        {
            DynamicRowsPanel.Children.Add(new TextBlock
            {
                Text = "Your personalised rows will appear as you use Haven.",
                Classes = { "muted" },
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 28)
            });
        }

    }

    private DashboardCard BuildModeCard(ModeDefinition mode, string detail)
    {
        var card = new DashboardCard
        {
            IconKey = mode.IconKey,
            TitleText = mode.Name,
            DetailText = detail
        };
        card.Click += (_, _) =>
        {
            _bus.Fire($"Dashboard.Mode.{mode.Key}.Click");
            ModeRequested?.Invoke(this, mode);
        };
        return card;
    }

    private DashboardCard BuildConversationCard(Conversation conversation, ModeDefinition mode)
    {
        var detail = conversation.UpdatedAt.ToLocalTime().ToString("ddd d MMM, HH:mm");
        var card = new DashboardCard
        {
            IconKey = "chat",
            TitleText = conversation.Title,
            DetailText = detail
        };
        card.Click += (_, _) =>
        {
            _bus.Fire("Dashboard.Conversation.Click");
            ConversationRequested?.Invoke(this, conversation);
        };
        ToolTip.SetTip(card, $"Open in {mode.Name}");
        return card;
    }

    private void RefreshClock()
    {
        var now = DateTimeOffset.Now;
        WelcomeText.Text = now.Hour < 12 ? "Good morning" : now.Hour < 18 ? "Good afternoon" : "Good evening";
        DateText.Text = $"{now:h:mmtt} · {now:dddd d MMMM}";
    }

    private void Register(string name, Control control)
    {
        _bus.RegisterElement(name, control);
        _bus.WirePointerEvents(name, control);
    }

    private static IBrush ResourceBrush(string key, Color fallback) =>
        Avalonia.Application.Current?.TryFindResource(key, out var value) == true && value is IBrush brush
            ? brush
            : new SolidColorBrush(fallback);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _clock.Stop();
    }
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

public sealed class CompanionDockViewModel : ObservableObject, IDisposable
{
    private readonly ICompanionDockService _dock;
    private readonly IConversationRepository _conversations;
    private readonly DispatcherTimer _refreshTimer;
    private bool _isExpanded;
    private bool _isCollapsed = true;
    private CompanionCardViewModel? _selectedCard;
    private int _selectedIndex;

    public CompanionDockViewModel(
        ICompanionDockService dock,
        IConversationRepository conversations)
    {
        _dock = dock;
        _conversations = conversations;
        Cards = [];
        ToggleExpandCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
        SelectCardCommand = new RelayCommand<CompanionCardViewModel>(SelectCard);
        CloseCardCommand = new RelayCommand<CompanionCardViewModel>(CloseCard);
        ExpandCardCommand = new RelayCommand<CompanionCardViewModel>(ExpandCard);
        _refreshTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background,
            async (_, _) => await RefreshCardsAsync());
    }

    public ObservableCollection<CompanionCardViewModel> Cards { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!SetProperty(ref _isExpanded, value)) return;
            IsCollapsed = !value;
            RaisePropertyChanged(nameof(HasCards));
            RaisePropertyChanged(nameof(CardCount));
        }
    }

    public bool IsCollapsed
    {
        get => _isCollapsed;
        private set => SetProperty(ref _isCollapsed, value);
    }

    public CompanionCardViewModel? SelectedCard
    {
        get => _selectedCard;
        set
        {
            if (!SetProperty(ref _selectedCard, value)) return;
            if (value is not null)
            {
                SelectedIndex = Cards.IndexOf(value);
                value.IsExpanded = true;
            }
        }
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set => SetProperty(ref _selectedIndex, value);
    }

    public bool HasCards => Cards.Count > 0;
    public int CardCount => Cards.Count;

    public RelayCommand ToggleExpandCommand { get; }
    public RelayCommand<CompanionCardViewModel> SelectCardCommand { get; }
    public RelayCommand<CompanionCardViewModel> CloseCardCommand { get; }
    public RelayCommand<CompanionCardViewModel> ExpandCardCommand { get; }

    public void Start() => _refreshTimer.Start();
    public void Stop() => _refreshTimer.Stop();

    public async Task DockAsync(Guid conversationId, SurfaceKind surface, string title, CancellationToken cancellationToken)
    {
        var existing = Cards.FirstOrDefault(c => c.ConversationId == conversationId);
        if (existing is not null) { existing.IsExpanded = true; SelectedCard = existing; return; }

        var card = new CompanionCardViewModel(conversationId, surface, title,
            async () => await UndockAsync(conversationId),
            () => { var c = Cards.FirstOrDefault(x => x.ConversationId == conversationId); if (c is not null) ExpandCard(c); });
        Cards.Add(card);
        await _dock.DockAsync(conversationId, surface, cancellationToken).ConfigureAwait(false);
        RaisePropertyChanged(nameof(HasCards));
        RaisePropertyChanged(nameof(CardCount));
        if (Cards.Count == 1) SelectedCard = card;
    }

    public async Task UndockAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        var card = Cards.FirstOrDefault(c => c.ConversationId == conversationId);
        if (card is null) return;
        Cards.Remove(card);
        card.Dispose();
        await _dock.UndockAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (ReferenceEquals(SelectedCard, card))
            SelectedCard = Cards.FirstOrDefault();
        RaisePropertyChanged(nameof(HasCards));
        RaisePropertyChanged(nameof(CardCount));
    }

    private async Task CloseCardAsync(CompanionCardViewModel? card, CancellationToken cancellationToken)
    {
        if (card is null) return;
        await UndockAsync(card.ConversationId, cancellationToken);
    }

    private void SelectCard(CompanionCardViewModel? card)
    {
        if (card is null) return;
        SelectedCard = card;
        card.IsExpanded = true;
    }

    private void CloseCard(CompanionCardViewModel? card)
    {
        if (card is null) return;
        Cards.Remove(card);
        card.Dispose();
        if (ReferenceEquals(SelectedCard, card))
            SelectedCard = Cards.FirstOrDefault();
        RaisePropertyChanged(nameof(HasCards));
        RaisePropertyChanged(nameof(CardCount));
    }

    private void ExpandCard(CompanionCardViewModel? card)
    {
        if (card is null) return;
        card.IsExpanded = !card.IsExpanded;
    }

    private async Task RefreshCardsAsync()
    {
        foreach (var card in Cards)
        {
            try
            {
                var conversation = await _conversations.GetAsync(card.ConversationId, CancellationToken.None);
                if (conversation is not null)
                {
                    card.Title = conversation.Title;
                    card.UpdatedAt = conversation.UpdatedAt;
                }
            }
            catch { }
        }
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        foreach (var card in Cards) card.Dispose();
        Cards.Clear();
    }
}

public sealed class CompanionCardViewModel : ObservableObject, IDisposable
{
    private bool _isExpanded;
    private bool _isPinned;
    private string _title;
    private DateTimeOffset _updatedAt;

    public CompanionCardViewModel(
        Guid conversationId,
        SurfaceKind surface,
        string title,
        Func<Task> close,
        Action expand)
    {
        ConversationId = conversationId;
        Surface = surface;
        _title = title;
        _updatedAt = DateTimeOffset.UtcNow;
        CloseCommand = new AsyncRelayCommand(() => close());
        ExpandCommand = new RelayCommand(() => expand());
        TogglePinCommand = new RelayCommand(() => IsPinned = !IsPinned);
    }

    public Guid ConversationId { get; }
    public SurfaceKind Surface { get; }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public DateTimeOffset UpdatedAt
    {
        get => _updatedAt;
        set => SetProperty(ref _updatedAt, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool IsPinned
    {
        get => _isPinned;
        set => SetProperty(ref _isPinned, value);
    }

    public string SurfaceIcon => Surface switch
    {
        SurfaceKind.Browse => "\uE774",
        SurfaceKind.Plan => "\uE7BA",
        SurfaceKind.Phone or SurfaceKind.Do => "\uE717",
        SurfaceKind.Chat => "\uE8AB",
        _ => "\uE790"
    };

    public string SurfaceLabel => Surface switch
    {
        SurfaceKind.Browse => "Browser",
        SurfaceKind.Plan => "Planner",
        SurfaceKind.Do => "Do",
        SurfaceKind.Phone => "Call",
        SurfaceKind.Chat => "Chat",
        _ => "Activity"
    };

    public AsyncRelayCommand CloseCommand { get; }
    public RelayCommand ExpandCommand { get; }
    public RelayCommand TogglePinCommand { get; }

    public void Dispose() { }
}

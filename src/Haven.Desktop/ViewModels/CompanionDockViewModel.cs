/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/CompanionDockViewModel.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns CompanionDockViewModel, CompanionCardViewModel. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Keeping UI state here makes the XAML declarative and keeps behavior testable without recreating the full window.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Represents companion dock view model and keeps its related state and behavior together.
/// </summary>
public sealed class CompanionDockViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Stores dock locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ICompanionDockService _dock;
    /// <summary>
    /// Stores conversations locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IConversationRepository _conversations;
    /// <summary>
    /// Stores refresh timer locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly DispatcherTimer _refreshTimer;
    /// <summary>
    /// Stores is expanded locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isExpanded;
    /// <summary>
    /// Stores is collapsed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isCollapsed = true;
    /// <summary>
    /// Stores selected card locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private CompanionCardViewModel? _selectedCard;
    /// <summary>
    /// Stores selected index locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
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

    /// <summary>
    /// Gets or updates cards, the bindable or domain state represented by this property.
    /// </summary>
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

    /// <summary>
    /// Reports whether cards applies to the current state.
    /// </summary>
    public bool HasCards => Cards.Count > 0;
    /// <summary>
    /// Gets or updates card count, the bindable or domain state represented by this property.
    /// </summary>
    public int CardCount => Cards.Count;

    /// <summary>
    /// Gets or updates toggle expand command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand ToggleExpandCommand { get; }
    /// <summary>
    /// Gets or updates select card command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<CompanionCardViewModel> SelectCardCommand { get; }
    /// <summary>
    /// Gets or updates close card command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<CompanionCardViewModel> CloseCardCommand { get; }
    /// <summary>
    /// Gets or updates expand card command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<CompanionCardViewModel> ExpandCardCommand { get; }

    /// <summary>
    /// Performs the start step owned by this component.
    /// </summary>
    public void Start() => _refreshTimer.Start();
    /// <summary>
    /// Performs the stop step owned by this component.
    /// </summary>
    public void Stop() => _refreshTimer.Stop();

    /// <summary>
    /// Performs dock asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs undock asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs close card asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task CloseCardAsync(CompanionCardViewModel? card, CancellationToken cancellationToken)
    {
        if (card is null) return;
        await UndockAsync(card.ConversationId, cancellationToken);
    }

    /// <summary>
    /// Performs the select card step owned by this component.
    /// </summary>
    private void SelectCard(CompanionCardViewModel? card)
    {
        if (card is null) return;
        SelectedCard = card;
        card.IsExpanded = true;
    }

    /// <summary>
    /// Performs the close card step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the expand card step owned by this component.
    /// </summary>
    private void ExpandCard(CompanionCardViewModel? card)
    {
        if (card is null) return;
        card.IsExpanded = !card.IsExpanded;
    }

    /// <summary>
    /// Performs refresh cards asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose()
    {
        _refreshTimer.Stop();
        foreach (var card in Cards) card.Dispose();
        Cards.Clear();
    }
}

/// <summary>
/// Represents companion card view model and keeps its related state and behavior together.
/// </summary>
public sealed class CompanionCardViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Stores is expanded locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isExpanded;
    /// <summary>
    /// Stores is pinned locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isPinned;
    /// <summary>
    /// Stores title locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _title;
    /// <summary>
    /// Stores updated at locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
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

    /// <summary>
    /// Gets or updates conversation id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid ConversationId { get; }
    /// <summary>
    /// Gets or updates surface, the bindable or domain state represented by this property.
    /// </summary>
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

    /// <summary>
    /// Gets or updates surface icon, the bindable or domain state represented by this property.
    /// </summary>
    public string SurfaceIcon => Surface switch
    {
        SurfaceKind.Browse => "globe",
        SurfaceKind.Plan => "calendar",
        SurfaceKind.Phone => "phone",
        SurfaceKind.Tasks => "tasks",
        SurfaceKind.Chat => "chat",
        _ => "home"
    };

    /// <summary>
    /// Gets or updates surface label, the bindable or domain state represented by this property.
    /// </summary>
    public string SurfaceLabel => Surface switch
    {
        SurfaceKind.Browse => "Browser",
        SurfaceKind.Plan => "Planner",
        SurfaceKind.Tasks => "Tasks",
        SurfaceKind.Phone => "Call",
        SurfaceKind.Chat => "Chat",
        _ => "Activity"
    };

    /// <summary>
    /// Gets or updates close command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand CloseCommand { get; }
    /// <summary>
    /// Gets or updates expand command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand ExpandCommand { get; }
    /// <summary>
    /// Gets or updates toggle pin command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand TogglePinCommand { get; }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose() { }
}

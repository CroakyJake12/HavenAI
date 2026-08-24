using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Application;
using Haven.Application.Play;
using Haven.Core;
using Haven.Desktop.HavenUI.Components;

namespace Haven.Desktop.Views.Pages.Play;

public sealed partial class PlayPage : UserControl
{
    private static readonly IReadOnlyList<PlayExperienceDescriptor> Experiences =
    [
        new("chess", "Chess", "Strategy", "A complete local board with legal turns, checkmate detection and a deterministic Haven opponent.", PlayExperienceKind.Chess),
        new("quick-quiz", "Quick quiz", "Quiz", "Five local questions with progress, scoring and instant continuation.", PlayExperienceKind.Quiz)
    ];

    private readonly PlaySessionService _sessions;
    private readonly GenerativeUiEventRouter _router;
    private PlaySessionSnapshot? _active;
    private int? _selectedSquare;
    private string _category = "All";
    private bool _activated;

    public PlayPage(PlaySessionService sessions, GenerativeUiEventRouter router)
    {
        _sessions = sessions;
        _router = router;
        InitializeComponent();

        CreateButton.Click += (_, _) => CreateRequested?.Invoke(this, EventArgs.Empty);
        BackButton.Click += (_, _) => ShowHome();
        RestartButton.Click += async (_, _) => await RestartActiveAsync();
        SearchBox.TextChanged += (_, _) => RenderFeatured();
        BuildCategories();
        RenderFeatured();
    }

    public event EventHandler? CreateRequested;

    public async Task ActivateAsync(CancellationToken cancellationToken)
    {
        PageStatus.Text = "Loading your local Play library…";
        try
        {
            await RefreshLibraryAsync(cancellationToken);
            _activated = true;
            PageStatus.Text = "Play runs deterministic rules locally. Model reasoning is only needed for experiences that explicitly ask for it.";
        }
        catch (OperationCanceledException)
        {
            PageStatus.Text = "Play loading was cancelled.";
        }
        catch (Exception exception)
        {
            PageStatus.Text = "Play could not load your saved sessions: " + exception.Message;
        }
    }

    private void BuildCategories()
    {
        CategoryPanel.Children.Clear();
        foreach (var category in new[] { "All", "Strategy", "Quiz", "AI-ready" })
        {
            var button = new HavenChipButton { Content = category, Margin = new Thickness(0, 0, 8, 8) };
            button.Click += (_, _) =>
            {
                _category = category;
                RenderFeatured();
            };
            CategoryPanel.Children.Add(button);
        }
    }

    private void RenderFeatured()
    {
        FeaturedPanel.Children.Clear();
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        var filtered = Experiences.Where(item =>
            (_category is "All" or "AI-ready" || item.Category.Equals(_category, StringComparison.OrdinalIgnoreCase))
            && (query.Length == 0 || item.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Category.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();

        foreach (var item in filtered) FeaturedPanel.Children.Add(BuildExperienceCard(item));
        if (filtered.Length == 0)
            FeaturedPanel.Children.Add(new TextBlock { Text = "No local Play experiences match that search.", Classes = { "muted" }, Margin = new Thickness(4, 10) });
    }

    private Control BuildExperienceCard(PlayExperienceDescriptor item)
    {
        var launch = new HavenPrimaryButton { Content = "Play", HorizontalAlignment = HorizontalAlignment.Left };
        launch.Click += async (_, _) =>
        {
            PageStatus.Text = "Starting " + item.Title + "…";
            var session = item.Kind == PlayExperienceKind.Chess
                ? await _sessions.StartChessAsync(CancellationToken.None)
                : await _sessions.StartQuizAsync(CancellationToken.None);
            await OpenSessionAsync(session);
        };
        return new HavenCard
        {
            Width = 310,
            MinHeight = 166,
            Margin = new Thickness(0, 0, 12, 12),
            Padding = new Thickness(16),
            Child = new StackPanel
            {
                Spacing = 7,
                Children =
                {
                    new TextBlock { Text = item.Title, FontSize = 18, FontWeight = FontWeight.ExtraBold },
                    new TextBlock { Text = item.Category + " · Local-first", Classes = { "muted" } },
                    new TextBlock { Text = item.Description, TextWrapping = TextWrapping.Wrap },
                    launch
                }
            }
        };
    }

    private async Task RefreshLibraryAsync(CancellationToken cancellationToken)
    {
        var recent = await _sessions.GetRecentAsync(cancellationToken);
        RecentPanel.Children.Clear();
        ContinuePanel.Children.Clear();

        var continuable = recent.FirstOrDefault(item => item.Status == PlaySessionStatus.Active);
        ContinueSection.IsVisible = continuable is not null;
        if (continuable is not null) ContinuePanel.Children.Add(BuildSessionCard(continuable, "Continue"));

        foreach (var item in recent.Take(8)) RecentPanel.Children.Add(BuildSessionCard(item, item.Status == PlaySessionStatus.Active ? "Resume" : "View"));
        RecentEmptyText.IsVisible = recent.Count == 0;
    }

    private Control BuildSessionCard(PlaySessionSummary item, string actionText)
    {
        var open = new HavenTertiaryButton { Content = actionText, HorizontalAlignment = HorizontalAlignment.Left };
        open.Click += async (_, _) =>
        {
            var session = await _sessions.GetAsync(item.Id, CancellationToken.None);
            if (session is null)
            {
                PageStatus.Text = "That saved Play session is no longer available.";
                await RefreshLibraryAsync(CancellationToken.None);
                return;
            }
            await OpenSessionAsync(session);
        };
        return new HavenCard
        {
            Width = 260,
            MinHeight = 128,
            Margin = new Thickness(0, 0, 12, 12),
            Padding = new Thickness(14),
            Child = new StackPanel
            {
                Spacing = 5,
                Children =
                {
                    new TextBlock { Text = item.Title, FontWeight = FontWeight.ExtraBold },
                    new TextBlock { Text = item.Subtitle, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap },
                    open
                }
            }
        };
    }

    private async Task OpenSessionAsync(PlaySessionSnapshot session)
    {
        _active = session;
        _selectedSquare = null;
        HomeSection.IsVisible = false;
        ExperienceSection.IsVisible = true;
        ExperienceTitle.Text = session.Title;
        RenderActive();
        await RefreshLibraryAsync(CancellationToken.None);
    }

    private void ShowHome()
    {
        _active = null;
        _selectedSquare = null;
        ExperienceSection.IsVisible = false;
        HomeSection.IsVisible = true;
        ExperienceHost.Content = null;
        if (_activated) _ = RefreshLibraryAsync(CancellationToken.None);
    }

    private void RenderActive()
    {
        if (_active is null) return;
        if (_active.Kind == PlayExperienceKind.Chess) RenderChess();
        else RenderQuiz();
    }

    private void RenderChess()
    {
        if (_active is null) return;
        var state = _sessions.ReadChess(_active);
        ExperienceStatus.Text = state.Result == "playing"
            ? (state.WhiteToMove ? "White to move · You are White" : "Black to move · Haven local opponent is thinking")
            : state.Result;

        var board = new Grid { Width = 520, Height = 520, HorizontalAlignment = HorizontalAlignment.Center };
        for (var i = 0; i < 8; i++)
        {
            board.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            board.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }

        var legal = _selectedSquare is int selected ? _sessions.GetLegalChessMoves(state, selected).ToHashSet() : [];
        for (var square = 0; square < 64; square++)
        {
            var captured = square;
            var piece = state.Board[square];
            HavenButtonBase cell = ((square / 8 + square % 8) % 2 == 0) ? new HavenSecondaryButton() : new HavenTertiaryButton();
            cell.Content = PieceGlyph(piece);
            cell.FontSize = 28;
            cell.Padding = new Thickness(0);
            cell.MinWidth = 44;
            cell.MinHeight = 44;
            cell.IsEnabled = state.Result == "playing" && state.WhiteToMove;
            AutomationProperties.SetName(cell, SquareName(square) + (piece == '.' ? string.Empty : " " + PieceName(piece)));
            if (_selectedSquare == square) cell.Classes.Add("selected");
            if (legal.Contains(square)) cell.Classes.Add("accent");
            cell.Click += async (_, _) => await OnChessSquareAsync(captured);
            Grid.SetRow(cell, square / 8);
            Grid.SetColumn(cell, square % 8);
            board.Children.Add(cell);
        }
        ExperienceHost.Content = board;
    }

    private async Task OnChessSquareAsync(int square)
    {
        if (_active is null) return;
        var state = _sessions.ReadChess(_active);
        if (!state.WhiteToMove || state.Result != "playing") return;

        if (_selectedSquare is null)
        {
            var piece = state.Board[square];
            if (piece != '.' && char.IsUpper(piece) && _sessions.GetLegalChessMoves(state, square).Count > 0)
            {
                _selectedSquare = square;
                RenderChess();
            }
            return;
        }

        var from = _selectedSquare.Value;
        if (!_sessions.GetLegalChessMoves(state, from).Contains(square))
        {
            _selectedSquare = null;
            RenderChess();
            return;
        }

        _selectedSquare = null;
        if (!await RouteAsync("play.chess.move", "chess-board", new { sessionId = _active.Id, from, to = square }, GenUiEventSource.User))
            return;
        _active = await _sessions.GetAsync(_active.Id, CancellationToken.None);
        RenderActive();
        await RunLocalOpponentAsync();
    }

    private async Task RunLocalOpponentAsync()
    {
        if (_active is null || _active.Kind != PlayExperienceKind.Chess) return;
        var state = _sessions.ReadChess(_active);
        if (state.WhiteToMove || state.Result != "playing") return;
        var move = _sessions.ChooseLocalChessOpponentMove(state);
        if (move is null) return;
        ExperienceStatus.Text = "Haven local opponent chose a legal move…";
        if (await RouteAsync("play.chess.move", "chess-board", new { sessionId = _active.Id, from = move.Value.From, to = move.Value.To }, GenUiEventSource.Agent))
        {
            _active = await _sessions.GetAsync(_active.Id, CancellationToken.None);
            RenderActive();
            await RefreshLibraryAsync(CancellationToken.None);
        }
    }

    private void RenderQuiz()
    {
        if (_active is null) return;
        var state = _sessions.ReadQuiz(_active);
        ExperienceStatus.Text = state.Completed
            ? $"Finished · {state.Score}/{state.Questions.Count}"
            : $"Question {state.QuestionIndex + 1} of {state.Questions.Count} · {state.Score} correct";

        if (state.Completed)
        {
            ExperienceHost.Content = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "Quiz complete", FontSize = 24, FontWeight = FontWeight.ExtraBold, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = $"Score: {state.Score}/{state.Questions.Count}", FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = "Restart to try the same local question set again.", Classes = { "muted" }, HorizontalAlignment = HorizontalAlignment.Center }
                }
            };
            return;
        }

        if (state.Questions.Count == 0 || state.QuestionIndex < 0 || state.QuestionIndex >= state.Questions.Count)
        {
            ExperienceHost.Content = new TextBlock { Text = "This saved quiz has no playable question data.", TextWrapping = TextWrapping.Wrap };
            return;
        }

        var question = state.Questions[state.QuestionIndex];
        var options = new StackPanel { Spacing = 8 };
        for (var i = 0; i < question.Options.Count; i++)
        {
            var answer = i;
            var button = new HavenSecondaryButton { Content = question.Options[i], HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetName(button, $"Answer {i + 1}: {question.Options[i]}");
            button.Click += async (_, _) =>
            {
                if (_active is null) return;
                if (!await RouteAsync("play.quiz.answer", "quiz-options", new { sessionId = _active.Id, answerIndex = answer }, GenUiEventSource.User)) return;
                _active = await _sessions.GetAsync(_active.Id, CancellationToken.None);
                RenderActive();
                await RefreshLibraryAsync(CancellationToken.None);
            };
            options.Children.Add(button);
        }

        ExperienceHost.Content = new StackPanel
        {
            MaxWidth = 680,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = question.Prompt, FontSize = 21, FontWeight = FontWeight.ExtraBold, TextWrapping = TextWrapping.Wrap },
                options,
                new TextBlock { Text = "Questions and scoring run locally. A future model-backed host can adapt this activity through the same semantic Play events.", Classes = { "muted" }, TextWrapping = TextWrapping.Wrap }
            }
        };
    }

    private async Task RestartActiveAsync()
    {
        if (_active is null) return;
        if (!await RouteAsync("play.session.restart", "experience", new { sessionId = _active.Id }, GenUiEventSource.User)) return;
        _active = await _sessions.GetAsync(_active.Id, CancellationToken.None);
        _selectedSquare = null;
        RenderActive();
        await RefreshLibraryAsync(CancellationToken.None);
    }

    private async Task<bool> RouteAsync(string actionId, string componentId, object payload, GenUiEventSource source)
    {
        if (_active is null) return false;
        var origin = new GenUiOrigin(Guid.Empty, PlaySessionService.AppTargetKey, null, _active.Id);
        var semanticEvent = new GenUiEvent(Guid.NewGuid(), GenUiEventType.ActionInvoked, DateTimeOffset.UtcNow, origin,
            componentId, actionId, _active.Id.ToString("N"), null, null, JsonSerializer.SerializeToElement(payload),
            source, source == GenUiEventSource.Agent ? "Local deterministic Play opponent action." : "Play user interaction.");
        var result = await _router.RouteAsync(semanticEvent,
            new GenUiActionBinding(actionId, GenUiRouteKind.App, PlaySessionService.AppTargetKey, CapabilityRiskClass.Low, false),
            CancellationToken.None);
        if (result.Status == GenUiActionStatus.Completed) return true;
        PageStatus.Text = result.Summary;
        return false;
    }

    private static string PieceGlyph(char piece) => piece switch
    {
        'K' => "♔", 'Q' => "♕", 'R' => "♖", 'B' => "♗", 'N' => "♘", 'P' => "♙",
        'k' => "♚", 'q' => "♛", 'r' => "♜", 'b' => "♝", 'n' => "♞", 'p' => "♟",
        _ => string.Empty
    };

    private static string PieceName(char piece) => char.ToLowerInvariant(piece) switch
    {
        'k' => "king", 'q' => "queen", 'r' => "rook", 'b' => "bishop", 'n' => "knight", 'p' => "pawn", _ => "empty"
    };

    private static string SquareName(int square) => $"{(char)('a' + square % 8)}{8 - square / 8}";

    private sealed record PlayExperienceDescriptor(string Key, string Title, string Category, string Description, PlayExperienceKind Kind);
}

using System.Text.Json;
using Haven.Core;

namespace Haven.Application.Play;

public enum PlayExperienceKind { Chess, Quiz }
public enum PlaySessionStatus { Active, Completed }

public sealed record PlaySessionSummary(Guid Id, PlayExperienceKind Kind, string Title, string Subtitle, PlaySessionStatus Status, DateTimeOffset UpdatedAt);
public sealed record PlaySessionSnapshot(Guid Id, PlayExperienceKind Kind, string Title, PlaySessionStatus Status, string PayloadJson, DateTimeOffset UpdatedAt);
public sealed record PlayLibraryState(int Version, List<PlaySessionSnapshot> Sessions);
public sealed record ChessGameState(
    string Board,
    bool WhiteToMove,
    string Result,
    DateTimeOffset UpdatedAt,
    bool WhiteKingSideCastle = true,
    bool WhiteQueenSideCastle = true,
    bool BlackKingSideCastle = true,
    bool BlackQueenSideCastle = true,
    int? EnPassantTarget = null);
public sealed record QuizQuestion(string Prompt, IReadOnlyList<string> Options, int CorrectIndex, string Explanation);
public sealed record QuizGameState(IReadOnlyList<QuizQuestion> Questions, int QuestionIndex, int Score, bool Completed, bool? LastAnswerCorrect, DateTimeOffset UpdatedAt);

/// <summary>Authoritative local-first state owner for Haven Play.</summary>
public sealed class PlaySessionService
{
    public const string AppTargetKey = "play";
    private const string StateKey = "play.sessions.v1";
    private const int MaximumRecentSessions = 24;

    private static readonly IReadOnlyList<QuizQuestion> QuizQuestions =
    [
        new("Which planet is known as the Red Planet?", ["Venus", "Mars", "Jupiter", "Mercury"], 1, "Mars appears red because iron minerals in its surface oxidise."),
        new("What does CPU stand for?", ["Central Processing Unit", "Computer Personal Utility", "Core Program Unit", "Central Peripheral Utility"], 0, "CPU stands for Central Processing Unit."),
        new("What is 12 × 8?", ["84", "92", "96", "108"], 2, "12 multiplied by 8 is 96."),
        new("Which gas do plants primarily absorb for photosynthesis?", ["Oxygen", "Nitrogen", "Carbon dioxide", "Hydrogen"], 2, "Plants use carbon dioxide, water and light energy during photosynthesis."),
        new("In chess, which piece can jump over other pieces?", ["Bishop", "Knight", "Queen", "Rook"], 1, "The knight is the only standard chess piece that can jump over occupied squares.")
    ];

    private readonly IVersionedSettingsStore _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PlayLibraryState? _state;

    public PlaySessionService(IVersionedSettingsStore settings, GenUiAppEventHandler appEvents)
    {
        _settings = settings;
        appEvents.Register(AppTargetKey, HandleSemanticEventAsync);
    }

    public IReadOnlyList<QuizQuestion> Questions => QuizQuestions;

    public async Task<IReadOnlyList<PlaySessionSummary>> GetRecentAsync(CancellationToken cancellationToken)
    {
        var state = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return state.Sessions.OrderByDescending(item => item.UpdatedAt).Select(ToSummary).ToArray();
    }

    public async Task<PlaySessionSnapshot?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var state = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return state.Sessions.FirstOrDefault(item => item.Id == id);
    }

    public Task<PlaySessionSnapshot> StartChessAsync(CancellationToken cancellationToken) =>
        CreateAsync(PlayExperienceKind.Chess, "Chess", JsonSerializer.Serialize(NewChessState()), cancellationToken);

    public Task<PlaySessionSnapshot> StartQuizAsync(CancellationToken cancellationToken) =>
        CreateAsync(PlayExperienceKind.Quiz, "Quick quiz",
            JsonSerializer.Serialize(new QuizGameState(QuizQuestions, 0, 0, false, null, DateTimeOffset.UtcNow)), cancellationToken);

    public Task<PlaySessionSnapshot> StartGeneratedQuizAsync(string title, IReadOnlyList<QuizQuestion> questions, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("A generated quiz needs a title.", nameof(title));
        ArgumentNullException.ThrowIfNull(questions);

        var validated = questions.Take(50).Select(question =>
        {
            var prompt = question.Prompt?.Trim() ?? string.Empty;
            var options = question.Options?.Take(8).Select(option => option?.Trim() ?? string.Empty).ToArray() ?? [];
            if (string.IsNullOrWhiteSpace(prompt) || options.Length is < 2 or > 8 || options.Any(string.IsNullOrWhiteSpace) ||
                question.CorrectIndex < 0 || question.CorrectIndex >= options.Length)
                return null;
            return new QuizQuestion(prompt, options, question.CorrectIndex, question.Explanation?.Trim() ?? string.Empty);
        }).Where(question => question is not null).Cast<QuizQuestion>().ToArray();

        if (validated.Length == 0) throw new ArgumentException("A generated quiz needs at least one valid question.", nameof(questions));
        var payload = JsonSerializer.Serialize(new QuizGameState(validated, 0, 0, false, null, DateTimeOffset.UtcNow));
        return CreateAsync(PlayExperienceKind.Quiz, title.Trim(), payload, cancellationToken);
    }

    public ChessGameState ReadChess(PlaySessionSnapshot session) =>
        JsonSerializer.Deserialize<ChessGameState>(session.PayloadJson)
        ?? throw new InvalidOperationException("The saved chess state is invalid.");

    public QuizGameState ReadQuiz(PlaySessionSnapshot session) =>
        JsonSerializer.Deserialize<QuizGameState>(session.PayloadJson)
        ?? throw new InvalidOperationException("The saved quiz state is invalid.");

    public IReadOnlyList<int> GetLegalChessMoves(ChessGameState state, int from) => ChessRules.GetLegalMoves(state, from);

    public (int From, int To)? ChooseLocalChessOpponentMove(ChessGameState state)
    {
        var moves = ChessRules.AllLegalMoves(state);
        if (moves.Count == 0) return null;
        var selected = moves.OrderByDescending(move => ChessRules.CaptureValue(state, move.From, move.To))
            .ThenBy(move => move.From).ThenBy(move => move.To).First();
        return (selected.From, selected.To);
    }

    public async Task<PlaySessionSnapshot> RestartAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await GetAsync(sessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Play session was not found.");
        var payload = session.Kind == PlayExperienceKind.Chess
            ? JsonSerializer.Serialize(NewChessState())
            : JsonSerializer.Serialize(new QuizGameState(ReadQuiz(session).Questions, 0, 0, false, null, DateTimeOffset.UtcNow));
        var restarted = session with { Status = PlaySessionStatus.Active, PayloadJson = payload, UpdatedAt = DateTimeOffset.UtcNow };
        await UpsertAsync(restarted, cancellationToken).ConfigureAwait(false);
        return restarted;
    }

    private async Task<PlaySessionSnapshot> CreateAsync(PlayExperienceKind kind, string title, string payload, CancellationToken cancellationToken)
    {
        var session = new PlaySessionSnapshot(Guid.NewGuid(), kind, title, PlaySessionStatus.Active, payload, DateTimeOffset.UtcNow);
        await UpsertAsync(session, cancellationToken).ConfigureAwait(false);
        return session;
    }

    private async Task<GenUiActionResult> HandleSemanticEventAsync(GenUiEvent semanticEvent, GenUiActionBinding binding, CancellationToken cancellationToken)
    {
        try
        {
            var payload = semanticEvent.StructuredPayload;
            if (!payload.TryGetProperty("sessionId", out var idElement) || !Guid.TryParse(idElement.GetString(), out var sessionId))
                return GenerativeUiEventRouter.Result(semanticEvent, GenUiActionStatus.Failed, "Play event did not include a valid session.");

            var session = await GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
            if (session is null)
                return GenerativeUiEventRouter.Result(semanticEvent, GenUiActionStatus.Unavailable, "That Play session no longer exists.");

            PlaySessionSnapshot updated = semanticEvent.ActionId switch
            {
                "play.chess.move" => ApplyChessMove(session, payload),
                "play.quiz.answer" => ApplyQuizAnswer(session, payload),
                "play.session.restart" => await RestartAsync(sessionId, cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException($"Play action '{semanticEvent.ActionId}' is not registered.")
            };
            if (semanticEvent.ActionId != "play.session.restart")
                await UpsertAsync(updated, cancellationToken).ConfigureAwait(false);
            return GenerativeUiEventRouter.Result(semanticEvent, GenUiActionStatus.Completed, "Play state updated.",
                JsonSerializer.SerializeToElement(new { sessionId = updated.Id, kind = updated.Kind.ToString(), status = updated.Status.ToString() }));
        }
        catch (OperationCanceledException)
        {
            return GenerativeUiEventRouter.Result(semanticEvent, GenUiActionStatus.Cancelled, "Play action was cancelled.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException or ArgumentException)
        {
            return GenerativeUiEventRouter.Result(semanticEvent, GenUiActionStatus.Failed, exception.Message);
        }
    }

    private static PlaySessionSnapshot ApplyChessMove(PlaySessionSnapshot session, JsonElement payload)
    {
        if (session.Kind != PlayExperienceKind.Chess) throw new InvalidOperationException("This session is not a chess game.");
        var from = payload.GetProperty("from").GetInt32();
        var to = payload.GetProperty("to").GetInt32();
        var state = JsonSerializer.Deserialize<ChessGameState>(session.PayloadJson)
            ?? throw new InvalidOperationException("The saved chess state is invalid.");
        var moved = ChessRules.ApplyMove(state, from, to);
        return session with
        {
            PayloadJson = JsonSerializer.Serialize(moved),
            Status = moved.Result == "playing" ? PlaySessionStatus.Active : PlaySessionStatus.Completed,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private PlaySessionSnapshot ApplyQuizAnswer(PlaySessionSnapshot session, JsonElement payload)
    {
        if (session.Kind != PlayExperienceKind.Quiz) throw new InvalidOperationException("This session is not a quiz.");
        var state = ReadQuiz(session);
        if (state.Completed) throw new InvalidOperationException("This quiz has already finished.");
        var answer = payload.GetProperty("answerIndex").GetInt32();
        if (state.Questions.Count == 0 || state.QuestionIndex < 0 || state.QuestionIndex >= state.Questions.Count)
            throw new InvalidOperationException("The saved quiz does not contain a playable question.");
        var question = state.Questions[state.QuestionIndex];
        if (answer < 0 || answer >= question.Options.Count) throw new InvalidOperationException("That quiz option does not exist.");

        var correct = answer == question.CorrectIndex;
        var next = state.QuestionIndex + 1;
        var completed = next >= state.Questions.Count;
        var updatedState = state with
        {
            QuestionIndex = completed ? state.QuestionIndex : next,
            Score = state.Score + (correct ? 1 : 0),
            Completed = completed,
            LastAnswerCorrect = correct,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        return session with
        {
            Status = completed ? PlaySessionStatus.Completed : PlaySessionStatus.Active,
            PayloadJson = JsonSerializer.Serialize(updatedState),
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private async Task<PlayLibraryState> LoadAsync(CancellationToken cancellationToken)
    {
        if (_state is not null) return _state;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state is not null) return _state;
            _state = Normalize(await _settings.GetAsync<PlayLibraryState>(StateKey, cancellationToken).ConfigureAwait(false)
                               ?? new PlayLibraryState(1, []));
            return _state;
        }
        finally { _gate.Release(); }
    }

    private async Task UpsertAsync(PlaySessionSnapshot session, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _state ??= Normalize(await _settings.GetAsync<PlayLibraryState>(StateKey, cancellationToken).ConfigureAwait(false)
                                 ?? new PlayLibraryState(1, []));
            _state.Sessions.RemoveAll(item => item.Id == session.Id);
            _state.Sessions.Insert(0, session);
            if (_state.Sessions.Count > MaximumRecentSessions)
                _state.Sessions.RemoveRange(MaximumRecentSessions, _state.Sessions.Count - MaximumRecentSessions);
            await _settings.SetAsync(StateKey, _state, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private static PlayLibraryState Normalize(PlayLibraryState state) =>
        new(1, state.Sessions.Where(item => item.Id != Guid.Empty && !string.IsNullOrWhiteSpace(item.Title))
            .OrderByDescending(item => item.UpdatedAt).Take(MaximumRecentSessions).ToList());

    private static PlaySessionSummary ToSummary(PlaySessionSnapshot item)
    {
        var subtitle = item.Kind == PlayExperienceKind.Chess ? ChessSubtitle(item) : QuizSubtitle(item);
        return new(item.Id, item.Kind, item.Title, subtitle, item.Status, item.UpdatedAt);
    }

    private static string ChessSubtitle(PlaySessionSnapshot item)
    {
        try
        {
            var state = JsonSerializer.Deserialize<ChessGameState>(item.PayloadJson);
            if (state is null) return "Saved chess game";
            return state.Result == "playing" ? (state.WhiteToMove ? "White to move" : "Black to move") : state.Result;
        }
        catch (JsonException) { return "Saved chess game"; }
    }

    private static string QuizSubtitle(PlaySessionSnapshot item)
    {
        try
        {
            var state = JsonSerializer.Deserialize<QuizGameState>(item.PayloadJson);
            if (state is null) return "Saved quiz";
            return state.Completed ? $"Finished · {state.Score}/{state.Questions.Count}"
                : $"Question {state.QuestionIndex + 1} of {state.Questions.Count} · {state.Score} correct";
        }
        catch (JsonException) { return "Saved quiz"; }
    }

    private static ChessGameState NewChessState() =>
        new("rnbqkbnrpppppppp................................PPPPPPPPRNBQKBNR", true, "playing", DateTimeOffset.UtcNow);

    private static class ChessRules
    {
        public readonly record struct Move(int From, int To);

        public static IReadOnlyList<int> GetLegalMoves(ChessGameState state, int from)
        {
            if (from is < 0 or > 63) return [];
            var board = Board(state);
            var piece = board[from];
            if (piece == '.' || IsWhite(piece) != state.WhiteToMove) return [];
            var moves = PseudoMoves(board, from).ToList();
            if (char.ToLowerInvariant(piece) == 'p' && state.EnPassantTarget is int enPassant &&
                PawnCanCaptureEnPassant(board, from, enPassant, IsWhite(piece)))
                moves.Add(enPassant);
            if (char.ToLowerInvariant(piece) == 'k')
                moves.AddRange(CastleMoves(state, board, from));
            return moves.Distinct()
                .Where(to => char.ToLowerInvariant(board[to]) != 'k')
                .Where(to => !LeavesKingInCheck(state, board, from, to))
                .ToArray();
        }

        public static IReadOnlyList<Move> AllLegalMoves(ChessGameState state)
        {
            var moves = new List<Move>();
            for (var from = 0; from < 64; from++)
                foreach (var to in GetLegalMoves(state, from)) moves.Add(new Move(from, to));
            return moves;
        }

        public static int CaptureValue(ChessGameState state, int from, int to)
        {
            var board = Board(state);
            var piece = board[to];
            if (piece == '.' && state.EnPassantTarget == to && char.ToLowerInvariant(board[from]) == 'p') return 1;
            return char.ToLowerInvariant(piece) switch { 'q' => 9, 'r' => 5, 'b' or 'n' => 3, 'p' => 1, 'k' => 100, _ => 0 };
        }

        public static ChessGameState ApplyMove(ChessGameState state, int from, int to)
        {
            if (!GetLegalMoves(state, from).Contains(to)) throw new InvalidOperationException("That is not a legal chess move.");
            var source = Board(state);
            var piece = source[from];
            var captured = source[to];
            var board = SimulateMove(state, source, from, to);
            if (char.ToLowerInvariant(piece) == 'p' && (Row(to) == 0 || Row(to) == 7)) board[to] = IsWhite(piece) ? 'Q' : 'q';

            var whiteKingSide = state.WhiteKingSideCastle;
            var whiteQueenSide = state.WhiteQueenSideCastle;
            var blackKingSide = state.BlackKingSideCastle;
            var blackQueenSide = state.BlackQueenSideCastle;
            if (piece == 'K') { whiteKingSide = false; whiteQueenSide = false; }
            if (piece == 'k') { blackKingSide = false; blackQueenSide = false; }
            if (piece == 'R' && from == 63) whiteKingSide = false;
            if (piece == 'R' && from == 56) whiteQueenSide = false;
            if (piece == 'r' && from == 7) blackKingSide = false;
            if (piece == 'r' && from == 0) blackQueenSide = false;
            if (captured == 'R' && to == 63) whiteKingSide = false;
            if (captured == 'R' && to == 56) whiteQueenSide = false;
            if (captured == 'r' && to == 7) blackKingSide = false;
            if (captured == 'r' && to == 0) blackQueenSide = false;

            var enPassant = char.ToLowerInvariant(piece) == 'p' && Math.Abs(Row(to) - Row(from)) == 2
                ? Index((Row(from) + Row(to)) / 2, Col(from))
                : (int?)null;
            var next = state with
            {
                Board = new string(board),
                WhiteToMove = !state.WhiteToMove,
                Result = "playing",
                UpdatedAt = DateTimeOffset.UtcNow,
                WhiteKingSideCastle = whiteKingSide,
                WhiteQueenSideCastle = whiteQueenSide,
                BlackKingSideCastle = blackKingSide,
                BlackQueenSideCastle = blackQueenSide,
                EnPassantTarget = enPassant
            };
            var replies = AllLegalMoves(next);
            if (replies.Count == 0)
            {
                var checkedKing = IsKingInCheck(Board(next), next.WhiteToMove);
                next = next with
                {
                    Result = checkedKing
                        ? (next.WhiteToMove ? "Black wins by checkmate" : "White wins by checkmate")
                        : "Draw by stalemate"
                };
            }
            return next;
        }

        private static IEnumerable<int> PseudoMoves(char[] board, int from)
        {
            var piece = board[from];
            var white = IsWhite(piece);
            return char.ToLowerInvariant(piece) switch
            {
                'p' => PawnMoves(board, from, white),
                'n' => JumpMoves(board, from, white, [(2,1),(2,-1),(-2,1),(-2,-1),(1,2),(1,-2),(-1,2),(-1,-2)]),
                'b' => SlideMoves(board, from, white, [(1,1),(1,-1),(-1,1),(-1,-1)]),
                'r' => SlideMoves(board, from, white, [(1,0),(-1,0),(0,1),(0,-1)]),
                'q' => SlideMoves(board, from, white, [(1,1),(1,-1),(-1,1),(-1,-1),(1,0),(-1,0),(0,1),(0,-1)]),
                'k' => JumpMoves(board, from, white, [(1,1),(1,0),(1,-1),(0,1),(0,-1),(-1,1),(-1,0),(-1,-1)]),
                _ => []
            };
        }

        private static IEnumerable<int> PawnMoves(char[] board, int from, bool white)
        {
            var result = new List<int>();
            var direction = white ? -1 : 1;
            var startRow = white ? 6 : 1;
            var row = Row(from); var col = Col(from);
            var oneRow = row + direction;
            if (!Inside(oneRow, col)) return result;
            var one = Index(oneRow, col);
            if (board[one] == '.')
            {
                result.Add(one);
                var twoRow = row + direction * 2;
                if (row == startRow && board[Index(twoRow, col)] == '.') result.Add(Index(twoRow, col));
            }
            foreach (var dc in new[] { -1, 1 })
            {
                var targetCol = col + dc;
                if (!Inside(oneRow, targetCol)) continue;
                var target = Index(oneRow, targetCol);
                if (board[target] != '.' && IsWhite(board[target]) != white) result.Add(target);
            }
            return result;
        }

        private static IEnumerable<int> JumpMoves(char[] board, int from, bool white, IReadOnlyList<(int dr,int dc)> offsets)
        {
            var result = new List<int>();
            foreach (var (dr, dc) in offsets)
            {
                var row = Row(from) + dr; var col = Col(from) + dc;
                if (!Inside(row, col)) continue;
                var target = Index(row, col);
                if (board[target] == '.' || IsWhite(board[target]) != white) result.Add(target);
            }
            return result;
        }

        private static IEnumerable<int> SlideMoves(char[] board, int from, bool white, IReadOnlyList<(int dr,int dc)> directions)
        {
            var result = new List<int>();
            foreach (var (dr, dc) in directions)
            {
                var row = Row(from) + dr; var col = Col(from) + dc;
                while (Inside(row, col))
                {
                    var target = Index(row, col);
                    if (board[target] == '.') result.Add(target);
                    else { if (IsWhite(board[target]) != white) result.Add(target); break; }
                    row += dr; col += dc;
                }
            }
            return result;
        }

        private static bool LeavesKingInCheck(ChessGameState state, char[] source, int from, int to)
        {
            var piece = source[from];
            var board = SimulateMove(state, source, from, to);
            return IsKingInCheck(board, IsWhite(piece));
        }

        private static char[] SimulateMove(ChessGameState state, char[] source, int from, int to)
        {
            var board = (char[])source.Clone();
            var piece = board[from];
            if (char.ToLowerInvariant(piece) == 'p' && state.EnPassantTarget == to && board[to] == '.' && Col(from) != Col(to))
                board[Index(Row(from), Col(to))] = '.';

            board[from] = '.';
            board[to] = piece;
            if (char.ToLowerInvariant(piece) == 'k' && Math.Abs(Col(to) - Col(from)) == 2)
            {
                var row = Row(from);
                if (Col(to) == 6)
                {
                    board[Index(row, 5)] = board[Index(row, 7)];
                    board[Index(row, 7)] = '.';
                }
                else if (Col(to) == 2)
                {
                    board[Index(row, 3)] = board[Index(row, 0)];
                    board[Index(row, 0)] = '.';
                }
            }
            return board;
        }

        private static bool PawnCanCaptureEnPassant(char[] board, int from, int target, bool white)
        {
            if (target is < 0 or > 63 || board[target] != '.') return false;
            var direction = white ? -1 : 1;
            if (Row(target) != Row(from) + direction || Math.Abs(Col(target) - Col(from)) != 1) return false;
            var captured = board[Index(Row(from), Col(target))];
            return captured == (white ? 'p' : 'P');
        }

        private static IEnumerable<int> CastleMoves(ChessGameState state, char[] board, int from)
        {
            var white = state.WhiteToMove;
            var homeRow = white ? 7 : 0;
            var kingHome = Index(homeRow, 4);
            if (from != kingHome || board[from] != (white ? 'K' : 'k') || IsSquareAttacked(board, kingHome, !white)) yield break;

            var kingSide = white ? state.WhiteKingSideCastle : state.BlackKingSideCastle;
            if (kingSide && board[Index(homeRow, 7)] == (white ? 'R' : 'r') &&
                board[Index(homeRow, 5)] == '.' && board[Index(homeRow, 6)] == '.' &&
                !IsSquareAttacked(board, Index(homeRow, 5), !white) && !IsSquareAttacked(board, Index(homeRow, 6), !white))
                yield return Index(homeRow, 6);

            var queenSide = white ? state.WhiteQueenSideCastle : state.BlackQueenSideCastle;
            if (queenSide && board[Index(homeRow, 0)] == (white ? 'R' : 'r') &&
                board[Index(homeRow, 1)] == '.' && board[Index(homeRow, 2)] == '.' && board[Index(homeRow, 3)] == '.' &&
                !IsSquareAttacked(board, Index(homeRow, 3), !white) && !IsSquareAttacked(board, Index(homeRow, 2), !white))
                yield return Index(homeRow, 2);
        }

        private static bool IsSquareAttacked(char[] board, int square, bool byWhite)
        {
            for (var from = 0; from < 64; from++)
            {
                var piece = board[from];
                if (piece == '.' || IsWhite(piece) != byWhite) continue;
                var lower = char.ToLowerInvariant(piece);
                if (lower == 'p')
                {
                    var direction = byWhite ? -1 : 1;
                    if (Row(square) == Row(from) + direction && Math.Abs(Col(square) - Col(from)) == 1) return true;
                    continue;
                }
                if (lower == 'k')
                {
                    if (Math.Max(Math.Abs(Row(square) - Row(from)), Math.Abs(Col(square) - Col(from))) == 1) return true;
                    continue;
                }
                if (PseudoMoves(board, from).Contains(square)) return true;
            }
            return false;
        }

        private static bool IsKingInCheck(char[] board, bool white)
        {
            var king = Array.IndexOf(board, white ? 'K' : 'k');
            return king < 0 || IsSquareAttacked(board, king, !white);
        }

        private static char[] Board(ChessGameState state)
        {
            if (state.Board.Length != 64) throw new InvalidOperationException("Chess board state must contain exactly 64 squares.");
            return state.Board.ToCharArray();
        }
        private static bool IsWhite(char piece) => char.IsUpper(piece);
        private static int Row(int square) => square / 8;
        private static int Col(int square) => square % 8;
        private static int Index(int row, int col) => row * 8 + col;
        private static bool Inside(int row, int col) => row is >= 0 and < 8 && col is >= 0 and < 8;
    }
}

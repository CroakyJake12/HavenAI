using System.Text.Json;
using Haven.Application;
using Haven.Application.Play;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class PlaySessionServiceTests
{
    [Fact]
    public async Task ChessUsesLegalLocalMovesAndAlternatesTurnsThroughSemanticRoute()
    {
        var settings = new MemorySettingsStore();
        var service = CreateService(settings, out var router);
        var session = await service.StartChessAsync(CancellationToken.None);
        var state = service.ReadChess(session);

        Assert.Contains(44, service.GetLegalChessMoves(state, 52));
        Assert.Contains(36, service.GetLegalChessMoves(state, 52));

        var result = await RouteAsync(router, session.Id, "play.chess.move", new { sessionId = session.Id, from = 52, to = 36 }, GenUiEventSource.User);
        Assert.Equal(GenUiActionStatus.Completed, result.Status);

        session = Assert.IsType<PlaySessionSnapshot>(await service.GetAsync(session.Id, CancellationToken.None));
        state = service.ReadChess(session);
        Assert.False(state.WhiteToMove);

        var opponent = service.ChooseLocalChessOpponentMove(state);
        Assert.NotNull(opponent);
        Assert.Contains(opponent.Value.To, service.GetLegalChessMoves(state, opponent.Value.From));

        result = await RouteAsync(router, session.Id, "play.chess.move",
            new { sessionId = session.Id, from = opponent.Value.From, to = opponent.Value.To }, GenUiEventSource.Agent);
        Assert.Equal(GenUiActionStatus.Completed, result.Status);

        session = Assert.IsType<PlaySessionSnapshot>(await service.GetAsync(session.Id, CancellationToken.None));
        Assert.True(service.ReadChess(session).WhiteToMove);
    }

    [Fact]
    public async Task QuizScoresCompletesAndReloadsFromVersionedSettings()
    {
        var settings = new MemorySettingsStore();
        var service = CreateService(settings, out var router);
        var session = await service.StartQuizAsync(CancellationToken.None);

        for (var index = 0; index < service.Questions.Count; index++)
        {
            var question = service.Questions[index];
            var result = await RouteAsync(router, session.Id, "play.quiz.answer",
                new { sessionId = session.Id, answerIndex = question.CorrectIndex }, GenUiEventSource.User);
            Assert.Equal(GenUiActionStatus.Completed, result.Status);
            session = Assert.IsType<PlaySessionSnapshot>(await service.GetAsync(session.Id, CancellationToken.None));
        }

        var quiz = service.ReadQuiz(session);
        Assert.True(quiz.Completed);
        Assert.Equal(service.Questions.Count, quiz.Score);
        Assert.Equal(PlaySessionStatus.Completed, session.Status);

        var reloaded = CreateService(settings, out _);
        var recent = await reloaded.GetRecentAsync(CancellationToken.None);
        var summary = Assert.Single(recent);
        Assert.Equal(session.Id, summary.Id);
        Assert.Equal(PlaySessionStatus.Completed, summary.Status);
    }

    [Fact]
    public async Task ChessSupportsKingSideCastlingAfterPathClears()
    {
        var settings = new MemorySettingsStore();
        var service = CreateService(settings, out var router);
        var session = await service.StartChessAsync(CancellationToken.None);

        foreach (var move in new[] { (52, 36), (8, 16), (62, 45), (16, 24), (61, 52), (9, 17) })
        {
            var result = await RouteAsync(router, session.Id, "play.chess.move",
                new { sessionId = session.Id, from = move.Item1, to = move.Item2 }, GenUiEventSource.User);
            Assert.Equal(GenUiActionStatus.Completed, result.Status);
        }

        session = Assert.IsType<PlaySessionSnapshot>(await service.GetAsync(session.Id, CancellationToken.None));
        var state = service.ReadChess(session);
        Assert.Contains(62, service.GetLegalChessMoves(state, 60));

        var castle = await RouteAsync(router, session.Id, "play.chess.move",
            new { sessionId = session.Id, from = 60, to = 62 }, GenUiEventSource.User);
        Assert.Equal(GenUiActionStatus.Completed, castle.Status);

        session = Assert.IsType<PlaySessionSnapshot>(await service.GetAsync(session.Id, CancellationToken.None));
        state = service.ReadChess(session);
        Assert.Equal('K', state.Board[62]);
        Assert.Equal('R', state.Board[61]);
        Assert.Equal('.', state.Board[60]);
        Assert.Equal('.', state.Board[63]);
        Assert.False(state.WhiteKingSideCastle);
        Assert.False(state.WhiteQueenSideCastle);
    }

    [Fact]
    public async Task ChessSupportsImmediateEnPassantCapture()
    {
        var settings = new MemorySettingsStore();
        var service = CreateService(settings, out var router);
        var session = await service.StartChessAsync(CancellationToken.None);

        foreach (var move in new[] { (52, 36), (8, 16), (36, 28), (11, 27) })
        {
            var result = await RouteAsync(router, session.Id, "play.chess.move",
                new { sessionId = session.Id, from = move.Item1, to = move.Item2 }, GenUiEventSource.User);
            Assert.Equal(GenUiActionStatus.Completed, result.Status);
        }

        session = Assert.IsType<PlaySessionSnapshot>(await service.GetAsync(session.Id, CancellationToken.None));
        var state = service.ReadChess(session);
        Assert.Equal(19, state.EnPassantTarget);
        Assert.Contains(19, service.GetLegalChessMoves(state, 28));

        var capture = await RouteAsync(router, session.Id, "play.chess.move",
            new { sessionId = session.Id, from = 28, to = 19 }, GenUiEventSource.User);
        Assert.Equal(GenUiActionStatus.Completed, capture.Status);

        session = Assert.IsType<PlaySessionSnapshot>(await service.GetAsync(session.Id, CancellationToken.None));
        state = service.ReadChess(session);
        Assert.Equal('P', state.Board[19]);
        Assert.Equal('.', state.Board[27]);
        Assert.Null(state.EnPassantTarget);
    }

    [Fact]
    public async Task GeneratedQuizPersistsItsOwnQuestionsAndRestartKeepsThem()
    {
        var settings = new MemorySettingsStore();
        var service = CreateService(settings, out var router);
        QuizQuestion[] generated =
        [
            new("  First prompt?  ", [" A ", " B "], 1, "  Because B.  "),
            new("Second prompt?", ["Yes", "No", "Maybe"], 0, "Because yes.")
        ];

        var session = await service.StartGeneratedQuizAsync("  Generated round  ", generated, CancellationToken.None);
        var state = service.ReadQuiz(session);
        Assert.Equal("Generated round", session.Title);
        Assert.Equal(2, state.Questions.Count);
        Assert.Equal("First prompt?", state.Questions[0].Prompt);
        Assert.Equal("B", state.Questions[0].Options[1]);

        var result = await RouteAsync(router, session.Id, "play.quiz.answer",
            new { sessionId = session.Id, answerIndex = state.Questions[0].CorrectIndex }, GenUiEventSource.User);
        Assert.Equal(GenUiActionStatus.Completed, result.Status);

        var reloaded = CreateService(settings, out _);
        session = Assert.IsType<PlaySessionSnapshot>(await reloaded.GetAsync(session.Id, CancellationToken.None));
        state = reloaded.ReadQuiz(session);
        Assert.Equal(2, state.Questions.Count);
        Assert.Equal(1, state.QuestionIndex);
        Assert.Equal(1, state.Score);

        session = await reloaded.RestartAsync(session.Id, CancellationToken.None);
        state = reloaded.ReadQuiz(session);
        Assert.Equal(2, state.Questions.Count);
        Assert.Equal(0, state.QuestionIndex);
        Assert.Equal(0, state.Score);
        Assert.False(state.Completed);
        Assert.Equal("Second prompt?", state.Questions[1].Prompt);
    }

    private static PlaySessionService CreateService(MemorySettingsStore settings, out GenerativeUiEventRouter router)
    {
        var appHandler = new GenUiAppEventHandler();
        var service = new PlaySessionService(settings, appHandler);
        router = new GenerativeUiEventRouter(
            [appHandler],
            new BoundedGenUiEventAuditSink(),
            new GenUiInstanceStore());
        return service;
    }

    private static Task<GenUiActionResult> RouteAsync(
        GenerativeUiEventRouter router,
        Guid sessionId,
        string actionId,
        object payload,
        GenUiEventSource source)
    {
        var origin = new GenUiOrigin(Guid.Empty, PlaySessionService.AppTargetKey, null, sessionId);
        var semanticEvent = new GenUiEvent(
            Guid.NewGuid(),
            GenUiEventType.ActionInvoked,
            DateTimeOffset.UtcNow,
            origin,
            "play-test",
            actionId,
            sessionId.ToString("N"),
            null,
            null,
            JsonSerializer.SerializeToElement(payload),
            source,
            "Play regression test.");
        return router.RouteAsync(
            semanticEvent,
            new GenUiActionBinding(actionId, GenUiRouteKind.App, PlaySessionService.AppTargetKey, CapabilityRiskClass.Low, false),
            CancellationToken.None);
    }

    private sealed class MemorySettingsStore : IVersionedSettingsStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_values.TryGetValue(key, out var json) ? JsonSerializer.Deserialize<T>(json) : null);
        }

        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            _values[key] = JsonSerializer.Serialize(value);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _values.Remove(key);
            return Task.CompletedTask;
        }

        public Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SettingsExportManifest { Settings = new Dictionary<string, string>(_values) });

        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken cancellationToken)
        {
            foreach (var item in manifest.Settings) _values[item.Key] = item.Value;
            return Task.FromResult(new SettingsImportResult(true, new Dictionary<string, string>(_values), "Imported"));
        }
    }
}

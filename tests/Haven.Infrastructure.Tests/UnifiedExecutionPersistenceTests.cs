using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class UnifiedExecutionPersistenceTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public async Task Migration_23_round_trips_graph_feedback_session_notifications_and_sources()
    {
        var database = new SqliteDatabase(_paths);
        await database.InitializeAsync(CancellationToken.None);
        Assert.Equal(23, Migrations.LatestVersion);
        await using (var connection = await database.OpenAsync(CancellationToken.None))
        {
            await using var version = connection.CreateCommand();
            version.CommandText = "SELECT MAX(version) FROM schema_migrations;";
            Assert.Equal((long)Migrations.LatestVersion, (long)(await version.ExecuteScalarAsync(CancellationToken.None))!);

            // Migration 23 adds the optional canonical Space association.
            var spaceColumn = connection.CreateCommand();
            spaceColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('conversations') WHERE name='space_id';";
            Assert.Equal(1L, (long)(await spaceColumn.ExecuteScalarAsync(CancellationToken.None))!);

            await using var tables = connection.CreateCommand();
            tables.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('execution_events','action_feedback','remediations','external_agent_tasks','haven_notifications','workspace_session','extension_sources','extension_packages');";
            Assert.Equal(8L, (long)(await tables.ExecuteScalarAsync(CancellationToken.None))!);
        }
        var events = new ExecutionEventRepository(database);
        var feedback = new ActionFeedbackRepository(database);
        var sessions = new WorkspaceSessionRepository(database);
        var notifications = new HavenNotificationRepository(database);
        var extensions = new ExtensionRepository(database);
        var executionId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await events.AppendAsync([
            new ExecutionEvent(Guid.NewGuid(), executionId, actionId, null, ExecutionOrigin.Haven, ExecutionActionType.ToolCall,
                ExecutionActionStatus.Failed, "Build", "Run the relevant build", "Compiler failed", "dotnet", now, now, now + TimeSpan.FromMilliseconds(25),
                Failure: new ExecutionFailure("CS0103", "Name does not exist", "Generated code referenced a missing name.")),
            new ExecutionEvent(Guid.NewGuid(), executionId, Guid.NewGuid(), actionId, ExecutionOrigin.Haven, ExecutionActionType.AutomaticRepair,
                ExecutionActionStatus.Completed, "Repair code", "Repair Haven's edit", "Corrected the generated name", "haven", now + TimeSpan.FromMilliseconds(30),
                RecoveryOfActionId: actionId)
        ], CancellationToken.None);
        var trace = await events.GetExecutionAsync(executionId, CancellationToken.None);
        Assert.Equal(2, trace.Count);
        Assert.Equal(ExecutionActionStatus.Failed, trace[0].Status);
        Assert.Equal(actionId, trace[1].RecoveryOfActionId);

        var feedbackValue = new ActionFeedback(Guid.NewGuid(), executionId, actionId, ActionFeedbackRating.Negative, "Use deterministic lookup first", "ToolCall", "dotnet", "Build repair", now, now);
        await feedback.UpsertAsync(feedbackValue, CancellationToken.None);
        Assert.Equal(feedbackValue.Comment, (await feedback.GetAsync(executionId, actionId, CancellationToken.None))?.Comment);

        var tabId = Guid.NewGuid();
        var boundsJson = """{"X":120,"Y":80,"Width":1024,"Height":768}""";
        var session = new WorkspaceSessionSnapshot(WorkspaceSessionSnapshot.CurrentSchemaVersion,
            [new TabSessionSnapshot(tabId, "chat", "Chat", "{}", null, null, null, false, false, now, now)], [],
            [new WorkspaceWindowSnapshot(Guid.NewGuid(), WorkspaceWindowKind.Main,
                new WorkspaceLayoutSnapshot(Guid.NewGuid(), WorkspaceLayoutKind.Single, SplitOrientation.Horizontal, .5,
                    [new WorkspacePaneSnapshot(Guid.NewGuid(), tabId, 0)]), [tabId], tabId, boundsJson, now)], now);
        await sessions.SaveAsync(session, CancellationToken.None);
        var restoredSession = (await sessions.LoadAsync(CancellationToken.None))!;
        Assert.Equal(tabId, restoredSession.Tabs[0].Id);
        Assert.Equal(boundsJson, Assert.Single(restoredSession.Windows).BoundsJson);

        var target = new HavenNavigationTarget(ActionId: actionId, ExecutionId: executionId);
        var notification = new HavenNotification(Guid.NewGuid(), HavenNotificationKind.Failure, HavenNotificationPriority.Error, "haven", "Haven", "Build failed", "Review the failure", false, false, false, false, null, target, [], now, now, now);
        await notifications.UpsertAsync(notification, CancellationToken.None);
        Assert.Equal(executionId, Assert.Single(await notifications.GetRecentAsync(10, false, CancellationToken.None)).Target.ExecutionId);

        var source = new ExtensionSource(Guid.NewGuid(), ExtensionSourceType.GitHubRepository, "Personal", "https://github.com/example/haven-plugins", null, false, null, ExtensionUpdateMode.Notify, true, null, null);
        await extensions.UpsertSourceAsync(source, CancellationToken.None);
        Assert.Equal(source.RepositoryUri, Assert.Single(await extensions.GetSourcesAsync(CancellationToken.None)).RepositoryUri);
    }

    [Fact]
    public async Task External_task_claim_is_exclusive_and_idempotent_completion_is_persisted()
    {
        var database = new SqliteDatabase(_paths);
        await database.InitializeAsync(CancellationToken.None);
        var repository = new ExternalAgentTaskRepository(database);
        var sink = new RecordingSink();
        var service = new ExternalAgentTaskService(repository, sink);
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var principal = new ExternalAgentPrincipal(userId, new HashSet<Guid> { workspaceId }, new HashSet<Guid> { projectId }, "personal.chatgpt");
        var task = await service.CreateAsync(principal, "personal.chatgpt", "Run tests", "Run authorised tests", "{\"projectRef\":\"current\",\"apiKey\":\"context-secret\"}", "Test result", workspaceId, projectId, null, null, null, CancellationToken.None);
        Assert.DoesNotContain("context-secret", task.ContextReferenceJson, StringComparison.Ordinal);
        Assert.Contains("<redacted>", task.ContextReferenceJson, StringComparison.Ordinal);

        var claim = await service.ClaimAsync(task.Locator, principal, "conversation-1", CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ClaimAsync(task.Locator, principal, "conversation-2", CancellationToken.None));
        await service.UpdateAsync(task.Id, claim.LeaseToken, HavenTaskStatus.Completed, "Done", "All tests passed", null, "complete-1", CancellationToken.None);
        await service.UpdateAsync(task.Id, claim.LeaseToken, HavenTaskStatus.Completed, "Done", "All tests passed", null, "complete-1", CancellationToken.None);
        Assert.Equal(HavenTaskStatus.Completed, (await repository.GetByIdAsync(task.Id, CancellationToken.None))!.Status);
        Assert.Contains(sink.Events, item => item.ActionType == ExecutionActionType.ExternalAgent && item.Status == ExecutionActionStatus.Completed);

        var other = new ExternalAgentPrincipal(Guid.NewGuid(), new HashSet<Guid> { workspaceId }, new HashSet<Guid> { projectId }, "personal.chatgpt");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAuthorisedAsync(task.Locator, other, CancellationToken.None));
        Assert.Empty(await service.GetRecentAuthorisedAsync(other, 20, CancellationToken.None));

        var cancellable = await service.CreateAsync(principal, "personal.chatgpt", "Inspect", "Inspect authorised files", "{}", "Summary",
            workspaceId, projectId, null, null, null, CancellationToken.None);
        await service.CancelAsync(cancellable.Id, principal, CancellationToken.None);
        Assert.Equal(HavenTaskStatus.Cancelled, (await repository.GetByIdAsync(cancellable.Id, CancellationToken.None))!.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CancelAsync(task.Id, principal, CancellationToken.None));
    }

    [Fact]
    public void Project_preview_provider_detects_real_supported_web_entry_points_without_starting_them()
    {
        var provider = new WebProjectPreviewProvider(new RecordingSink());
        var npmRoot = Path.Combine(_paths.DataDirectory, "npm-preview");
        Directory.CreateDirectory(npmRoot);
        File.WriteAllText(Path.Combine(npmRoot, "package.json"), "{\"scripts\":{\"dev\":\"vite\"}}");

        Assert.True(provider.CanPreview(npmRoot));
        Assert.Equal(ProjectPreviewKind.Website, provider.Describe(npmRoot).Kind);
        Assert.Contains("dev script", provider.Describe(npmRoot).EntryDescription, StringComparison.OrdinalIgnoreCase);

        var dotnetRoot = Path.Combine(_paths.DataDirectory, "dotnet-preview");
        Directory.CreateDirectory(dotnetRoot);
        File.WriteAllText(Path.Combine(dotnetRoot, "Web.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk.Web\" />");
        Assert.True(provider.CanPreview(dotnetRoot));

        var unsupported = Path.Combine(_paths.DataDirectory, "unsupported-preview");
        Directory.CreateDirectory(unsupported);
        File.WriteAllText(Path.Combine(unsupported, "package.json"), "{\"scripts\":{\"test\":\"echo test\"}}");
        Assert.False(provider.CanPreview(unsupported));
    }

    [Fact]
    public async Task Project_preview_provider_starts_serves_and_stops_a_real_loopback_site()
    {
        var sink = new RecordingSink();
        var provider = new WebProjectPreviewProvider(sink);
        var root = Path.Combine(_paths.DataDirectory, "running-preview");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "RunningPreview.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(root, "Program.cs"),
            "var app = WebApplication.CreateBuilder(args).Build(); app.MapGet(\"/\", () => \"haven-preview-ok\"); app.Run();");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var session = await provider.StartAsync(root, timeout.Token);
        var uri = session.PreviewUri;
        try
        {
            Assert.True(uri.IsLoopback);
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            Assert.Equal("haven-preview-ok", await client.GetStringAsync(uri, timeout.Token));
            Assert.Contains(sink.Events, item => item.ActionType == ExecutionActionType.Preview && item.Status == ExecutionActionStatus.Completed);
        }
        finally
        {
            await session.DisposeAsync();
        }

        var stopped = false;
        using (var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) })
        {
            for (var attempt = 0; attempt < 10 && !stopped; attempt++)
            {
                try
                {
                    using var response = await client.GetAsync(uri);
                    await Task.Delay(100);
                }
                catch (HttpRequestException) { stopped = true; }
                catch (TaskCanceledException) { stopped = true; }
            }
        }
        Assert.True(stopped);
    }

    public void Dispose() => _paths.Dispose();

    private sealed class RecordingSink : IExecutionEventSink
    {
        public List<ExecutionEvent> Events { get; } = [];
        public bool TryPublish(ExecutionEvent executionEvent) { Events.Add(executionEvent); return true; }
    }

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-unified-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
            DatabasePath = Path.Combine(DataDirectory, "test.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "missing.json");
        }
        public string DataDirectory { get; }
        public string DatabasePath { get; }
        public string BrowserProfileDirectory { get; }
        public string AttachmentsDirectory { get; }
        public string LogsDirectory { get; }
        public string LegacyStatePath { get; }
        public void Dispose() { try { if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, true); } catch { } }
    }
}

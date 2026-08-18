using Haven.Core;
using Haven.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Protects representative persisted records from every historical base schema
/// while the Generative UI release adds forward-only migrations.
/// </summary>
public sealed class HistoricalSchemaUpgradeTests : IDisposable
{
    private const string ConversationId = "11111111-1111-4111-8111-111111111111";
    private const string ContainerId = "22222222-2222-4222-8222-222222222222";
    private readonly TestPaths _paths = new();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public async Task EveryHistoricalBaseSchemaUpgradesWithoutLosingRepresentativeData(int sourceVersion)
    {
        await CreateHistoricalDatabaseAsync(sourceVersion);

        var database = new SqliteDatabase(_paths);
        await new ConversationProductionDatabase(database).InitializeAsync(CancellationToken.None);
        var retrieval = new RetrievalIndexService(database, new LocalHashEmbeddingService());
        await retrieval.IndexTextAsync(
            new RetrievalScope(RetrievalScopeKind.Conversation, Guid.Parse(ConversationId)),
            "migration-fixture",
            "fixture",
            "Fixture",
            "Preserved after schema upgrade",
            CancellationToken.None);

        await using var connection = await database.OpenAsync(CancellationToken.None);
        Assert.Equal(15L, await ScalarInt64Async(connection, "SELECT MAX(version) FROM schema_migrations;"));
        Assert.Equal("Preserved conversation", await ScalarStringAsync(connection, $"SELECT title FROM conversations WHERE id='{ConversationId}';"));
        Assert.Equal("Preserved message", await ScalarStringAsync(connection, "SELECT content FROM messages WHERE id='44444444-4444-4444-8444-444444444444';"));
        Assert.Equal("Preserved project", await ScalarStringAsync(connection, $"SELECT name FROM containers WHERE id='{ContainerId}';"));

        if (sourceVersion >= 2)
        {
            Assert.Equal("Preserved agent", await ScalarStringAsync(connection, "SELECT name FROM agents WHERE id='55555555-5555-4555-8555-555555555555';"));
            Assert.Equal("Preserved capability source", await ScalarStringAsync(connection, "SELECT name FROM capabilities WHERE id='66666666-6666-4666-8666-666666666666';"));
            Assert.Equal(0L, await ScalarInt64Async(connection, "SELECT is_enabled FROM capabilities WHERE id='66666666-6666-4666-8666-666666666666';"));
        }

        if (sourceVersion >= 3)
        {
            Assert.Equal("Preserved automation", await ScalarStringAsync(connection, "SELECT name FROM automations WHERE id='77777777-7777-4777-8777-777777777777';"));
            Assert.Equal("preserved-value", await ScalarStringAsync(connection, "SELECT value FROM settings WHERE key='fixture.setting';"));
        }

        if (sourceVersion >= 4)
        {
            Assert.Equal("Preserved instruction", await ScalarStringAsync(connection, "SELECT name FROM prompts WHERE id='99999999-9999-4999-8999-999999999999';"));
            Assert.Equal("Preserved workflow", await ScalarStringAsync(connection, "SELECT name FROM reusable_tasks WHERE id='aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';"));
        }

        if (sourceVersion >= 6)
            Assert.Equal("Preserved training prompt", await ScalarStringAsync(connection, "SELECT task_prompt FROM training_runs WHERE id='bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';"));

        if (sourceVersion >= 7)
        {
            Assert.Equal("reference.pdf", await ScalarStringAsync(connection, "SELECT name FROM container_resources WHERE id='dddddddd-dddd-4ddd-8ddd-dddddddddddd';"));
            Assert.Equal("Preserved planner task", await ScalarStringAsync(connection, "SELECT title FROM planner_tasks WHERE id='eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee';"));
        }

        if (sourceVersion >= 8)
        {
            Assert.Equal("fixture-app", await ScalarStringAsync(connection, "SELECT key FROM mode_definitions WHERE id='12121212-1212-4212-8212-121212121212';"));
            Assert.Equal(3L, await ScalarInt64Async(connection, "SELECT turn_count FROM mode_usage WHERE id='14141414-1414-4414-8414-141414141414';"));
        }
    }

    private async Task CreateHistoricalDatabaseAsync(int sourceVersion)
    {
        _ = new SqliteDatabase(_paths); // Initializes the production winsqlite3 provider.
        await using var connection = new SqliteConnection($"Data Source={_paths.DatabasePath}");
        await connection.OpenAsync();
        await ExecuteAsync(connection, "PRAGMA foreign_keys=ON; CREATE TABLE schema_migrations(version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL);");

        foreach (var migration in Migrations.All.Where(item => item.Version <= sourceVersion).OrderBy(item => item.Version))
        {
            await ExecuteAsync(connection, migration.Sql);
            await ExecuteAsync(connection, $"INSERT INTO schema_migrations(version,applied_at) VALUES({migration.Version},'2026-01-01T00:00:00.0000000+00:00');");
        }

        await SeedBaseDataAsync(connection, sourceVersion);
    }

    private static async Task SeedBaseDataAsync(SqliteConnection connection, int sourceVersion)
    {
        const string now = "2026-01-01T00:00:00.0000000+00:00";
        await ExecuteAsync(connection, $$"""
            INSERT INTO containers(id,mode,name,root_path,context,instructions,created_at,updated_at)
            VALUES('{{ContainerId}}',3,'Preserved project','C:\fixture','context','instructions','{{now}}','{{now}}');
            INSERT INTO lessons(id,subject_id,topic_group,name,structure_json,sort_order,created_at,updated_at)
            VALUES('33333333-3333-4333-8333-333333333333','{{ContainerId}}','General','Preserved lesson','{}',0,'{{now}}','{{now}}');
            INSERT INTO conversations(id,mode,kind,title,container_id,lesson_id,is_pinned,is_temporary,created_at,updated_at)
            VALUES('{{ConversationId}}',3,3,'Preserved conversation','{{ContainerId}}',NULL,1,0,'{{now}}','{{now}}');
            INSERT INTO messages(id,conversation_id,role,content,agent_name,model_name,metadata_json,created_at)
            VALUES('44444444-4444-4444-8444-444444444444','{{ConversationId}}',0,'Preserved message',NULL,'fixture-model','{}','{{now}}');
            """);

        if (sourceVersion >= 2)
        {
            await ExecuteAsync(connection, $$"""
                INSERT INTO agents(id,name,description,instructions,icon_key,preferred_model,fallback_model,detection_rules,permissions_json,is_built_in,is_enabled,updated_at)
                VALUES('55555555-5555-4555-8555-555555555555','Preserved agent','description','instructions','agent','fixture-model',NULL,'','{}',0,1,'{{now}}');
                INSERT INTO plugins(id,name,description,icon_key,instructions,capabilities_json,conflicts_json,persists,is_built_in,is_enabled,updated_at)
                VALUES('66666666-6666-4666-8666-666666666666','Preserved capability source','description','tool','instructions','[]','[]',1,0,1,'{{now}}');
                """);
        }

        if (sourceVersion >= 3)
        {
            await ExecuteAsync(connection, $$"""
                INSERT INTO automations(id,name,mode,instruction,schedule_kind,schedule_json,next_run_at,container_id,is_enabled,created_at,updated_at,lease_token,lease_until)
                VALUES('77777777-7777-4777-8777-777777777777','Preserved automation',2,'Run fixture',0,'{}',NULL,'{{ContainerId}}',1,'{{now}}','{{now}}',NULL,NULL);
                INSERT INTO automation_runs(id,automation_id,status,scheduled_for,started_at,completed_at,result,error,lease_token)
                VALUES('88888888-8888-4888-8888-888888888888','77777777-7777-4777-8777-777777777777',2,'{{now}}','{{now}}','{{now}}','Preserved result',NULL,NULL);
                INSERT INTO settings(key,value,updated_at) VALUES('fixture.setting','preserved-value','{{now}}');
                INSERT INTO migration_log(key,completed_at,note) VALUES('fixture-log','{{now}}','Preserved log');
                """);
        }

        if (sourceVersion >= 4)
        {
            await ExecuteAsync(connection, $$"""
                INSERT INTO prompts(id,name,description,icon_key,instructions,persists,is_built_in,is_enabled,updated_at,is_agentic,allowed_modes_json)
                VALUES('99999999-9999-4999-8999-999999999999','Preserved instruction','description','prompt','instructions',1,0,1,'{{now}}',0,'[]');
                INSERT INTO macros(id,name,description,instruction,container_id,is_enabled,created_at,updated_at)
                VALUES('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa','Preserved workflow','description','instruction','{{ContainerId}}',1,'{{now}}','{{now}}');
                INSERT INTO workspace_versions(id,conversation_id,container_id,workspace_root,relative_path,kind,before_content,after_content,summary,lines_added,lines_removed,created_at)
                VALUES('abababab-abab-4bab-8bab-abababababab','{{ConversationId}}','{{ContainerId}}','C:\fixture','file.txt',0,'before','after','Preserved version',1,1,'{{now}}');
                INSERT INTO decisions(id,container_id,title,decision_text,alternatives,reasoning,evidence,consequences,created_at,updated_at)
                VALUES('acacacac-acac-4cac-8cac-acacacacacac','{{ContainerId}}','Preserved decision','Keep data','None','Safety','Fixture','Compatibility','{{now}}','{{now}}');
                """);
        }

        if (sourceVersion >= 6)
        {
            await ExecuteAsync(connection, $$"""
                INSERT INTO training_runs(id,task_prompt,workspace_path,snapshot_path,model_name,max_attempts,duration_minutes,file_permission,command_permission,browser_permission,allow_desktop_tools,allow_file_system_writes,created_at,completed_at)
                VALUES('bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb','Preserved training prompt','C:\fixture','C:\snapshot','fixture-model',5,10,1,1,0,0,1,'{{now}}',NULL);
                INSERT INTO training_attempts(id,training_run_id,attempt_number,report_markdown,feedback,action_log,succeeded,duration_ms,created_at)
                VALUES('cccccccc-cccc-4ccc-8ccc-cccccccccccc','bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',1,'Preserved report',NULL,'[]',1,100,'{{now}}');
                """);
        }

        if (sourceVersion >= 7)
        {
            await ExecuteAsync(connection, $$"""
                INSERT INTO container_resources(id,container_id,name,stored_name,media_type,kind,size_bytes,sha256,created_at)
                VALUES('dddddddd-dddd-4ddd-8ddd-dddddddddddd','{{ContainerId}}','reference.pdf','reference-stored.pdf','application/pdf',0,42,'fixture-sha','{{now}}');
                INSERT INTO call_sessions(id,conversation_id,model_name,input_device_id,output_device_id,voice_name,input_mode,used_screen_share,status,started_at,ended_at,error)
                VALUES('dededede-dede-4ede-8ede-dededededede','{{ConversationId}}','fixture-model','mic','speaker','voice',0,0,2,'{{now}}','{{now}}',NULL);
                INSERT INTO planner_tasks(id,collection_id,parent_task_id,title,notes,priority,status,tags_json,estimated_minutes,starts_at,due_at,recurrence_rule,reminder_at,completed_at,sort_order,time_zone_id,created_at,updated_at)
                VALUES('eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee','8f51f72f-3c1f-4a5f-a101-010000000001',NULL,'Preserved planner task','notes',1,0,'[]',30,NULL,NULL,NULL,NULL,NULL,0,'Europe/London','{{now}}','{{now}}');
                INSERT INTO planner_events(id,calendar_id,title,notes,location,starts_at,ends_at,is_all_day,recurrence_rule,reminder_at,is_read_only,provider_event_id,provider_etag,time_zone_id,created_at,updated_at,deleted_at)
                VALUES('efefefef-efef-4fef-8fef-efefefefefef','8f51f72f-3c1f-4a5f-a101-020000000001','Preserved event','','','2026-01-01T10:00:00+00:00','2026-01-01T11:00:00+00:00',0,NULL,NULL,0,NULL,NULL,'Europe/London','{{now}}','{{now}}',NULL);
                """);
        }

        if (sourceVersion >= 8)
        {
            await ExecuteAsync(connection, $$"""
                INSERT INTO mode_definitions(id,key,name,description,icon_key,base_mode,surfaces_json,tool_allowlist_json,tool_denylist_json,plugins_json,system_prompt_suffix,source,install_state,author,version,tags_json,created_at,updated_at,is_enabled)
                VALUES('12121212-1212-4212-8212-121212121212','fixture-app','Fixture App','description','app',0,'["Chat"]','[]','[]','[]','',2,3,'User','1.0.0','[]','{{now}}','{{now}}',1);
                INSERT INTO mode_versions(id,mode_id,major,minor,patch,manifest_json,changelog,published_at)
                VALUES('13131313-1313-4313-8313-131313131313','12121212-1212-4212-8212-121212121212',1,0,0,'{}','Initial','{{now}}');
                INSERT INTO mode_permission_grants(id,mode_id,file_permission,command_permission,browser_permission,allow_desktop_tools,allow_file_system_writes,granted_at)
                VALUES('15151515-1515-4515-8515-151515151515','12121212-1212-4212-8212-121212121212',1,1,0,0,1,'{{now}}');
                INSERT INTO mode_pins(id,mode_id,sort_order,pinned_at)
                VALUES('16161616-1616-4616-8616-161616161616','12121212-1212-4212-8212-121212121212',0,'{{now}}');
                INSERT INTO mode_usage(id,mode_id,date,turn_count,completion_count,total_duration_ms)
                VALUES('14141414-1414-4414-8414-141414141414','12121212-1212-4212-8212-121212121212','2026-01-01',3,2,1000);
                INSERT INTO surface_runs(id,conversation_id,surface,surface_key,target_mode_key,started_at,completed_at,succeeded)
                VALUES('17171717-1717-4717-8717-171717171717','{{ConversationId}}',1,'fixture','fixture-app','{{now}}','{{now}}',1);
                INSERT INTO activity_events(id,kind,conversation_id,mode_id,summary,detail_json,timestamp)
                VALUES('18181818-1818-4818-8818-181818181818',1,'{{ConversationId}}','12121212-1212-4212-8212-121212121212','Preserved activity','{}','{{now}}');
                INSERT INTO conversation_moves(id,conversation_id,from_mode_id,to_mode_id,from_placement,to_placement,reason,moved_at)
                VALUES('19191919-1919-4919-8919-191919191919','{{ConversationId}}','12121212-1212-4212-8212-121212121212','12121212-1212-4212-8212-121212121212',0,1,'Preserved move','{{now}}');
                """);
        }
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarInt64Async(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<string> ScalarStringAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    public void Dispose() => _paths.Dispose();

    private sealed class TestPaths : Haven.Application.IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-historical-schema-tests-" + Guid.NewGuid().ToString("N"));
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

        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, true); }
            catch (IOException) { }
        }
    }
}

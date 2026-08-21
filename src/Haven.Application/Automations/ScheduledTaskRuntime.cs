using System.Globalization;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application.Automations;

/// <summary>Editable schedule fields used by Haven Automations and Plan.</summary>
public sealed record ScheduledTaskScheduleDraft(
    DateTimeOffset OnceAt,
    TimeOnly Time,
    DayOfWeek DayOfWeek,
    int IntervalHours,
    int ConditionIntervalMinutes);

/// <summary>Composes and parses the durable schedule JSON used by scheduled Tasks.</summary>
public static class ScheduledTaskScheduleComposer
{
    public static string Compose(AutomationScheduleKind kind, ScheduledTaskScheduleDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var value = kind switch
        {
            AutomationScheduleKind.Once => new Dictionary<string, object?>
            {
                ["at"] = draft.OnceAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
            },
            AutomationScheduleKind.Hourly => new Dictionary<string, object?>
            {
                ["intervalHours"] = Math.Clamp(draft.IntervalHours, 1, 168)
            },
            AutomationScheduleKind.Daily => new Dictionary<string, object?> { ["time"] = FormatTime(draft.Time) },
            AutomationScheduleKind.Weekly => new Dictionary<string, object?>
            {
                ["dayOfWeek"] = draft.DayOfWeek.ToString(),
                ["time"] = FormatTime(draft.Time)
            },
            AutomationScheduleKind.ConditionWatch => new Dictionary<string, object?>
            {
                ["intervalMinutes"] = Math.Clamp(draft.ConditionIntervalMinutes, 60, 10_080)
            },
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported scheduled Task kind.")
        };
        return JsonSerializer.Serialize(value);
    }

    public static ScheduledTaskScheduleDraft Parse(
        AutomationScheduleKind kind,
        string? scheduleJson,
        DateTimeOffset now)
    {
        var fallback = new ScheduledTaskScheduleDraft(now.AddHours(1), new TimeOnly(8, 0), DayOfWeek.Monday, 1, 60);
        if (string.IsNullOrWhiteSpace(scheduleJson)) return fallback;
        try
        {
            using var document = JsonDocument.Parse(scheduleJson);
            var root = document.RootElement;
            var once = root.TryGetProperty("at", out var at)
                       && DateTimeOffset.TryParse(at.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedAt)
                ? parsedAt.ToLocalTime()
                : fallback.OnceAt;
            var time = root.TryGetProperty("time", out var timeValue)
                       && TimeOnly.TryParse(timeValue.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTime)
                ? parsedTime
                : fallback.Time;
            var day = root.TryGetProperty("dayOfWeek", out var dayValue)
                      && Enum.TryParse<DayOfWeek>(dayValue.GetString(), true, out var parsedDay)
                ? parsedDay
                : fallback.DayOfWeek;
            var hours = root.TryGetProperty("intervalHours", out var hourValue) && hourValue.TryGetInt32(out var parsedHours)
                ? Math.Clamp(parsedHours, 1, 168)
                : fallback.IntervalHours;
            var minutes = root.TryGetProperty("intervalMinutes", out var minuteValue) && minuteValue.TryGetInt32(out var parsedMinutes)
                ? Math.Clamp(parsedMinutes, 60, 10_080)
                : fallback.ConditionIntervalMinutes;
            return new ScheduledTaskScheduleDraft(once, time, day, hours, minutes);
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    public static string Describe(AutomationScheduleKind kind, ScheduledTaskScheduleDraft draft) => kind switch
    {
        AutomationScheduleKind.Once => $"Once at {draft.OnceAt.ToLocalTime():g}",
        AutomationScheduleKind.Hourly => draft.IntervalHours == 1 ? "Every hour" : $"Every {draft.IntervalHours} hours",
        AutomationScheduleKind.Daily => $"Daily at {FormatTime(draft.Time)}",
        AutomationScheduleKind.Weekly => $"Every {draft.DayOfWeek} at {FormatTime(draft.Time)}",
        AutomationScheduleKind.ConditionWatch => draft.ConditionIntervalMinutes == 60
            ? "Check the condition hourly"
            : $"Check the condition every {draft.ConditionIntervalMinutes} minutes",
        _ => kind.ToString()
    };

    private static string FormatTime(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);
}

/// <summary>Calculates next-run timestamps without owning a scheduler loop.</summary>
public sealed class ScheduledTaskScheduleCalculator(TimeZoneInfo? timeZone = null)
{
    private readonly TimeZoneInfo _timeZone = timeZone ?? TimeZoneInfo.Local;

    public DateTimeOffset? GetNextRun(AutomationDefinition task, DateTimeOffset after)
    {
        if (!task.IsEnabled) return null;
        using var document = Parse(task.ScheduleJson);
        var root = document.RootElement;
        return task.ScheduleKind switch
        {
            AutomationScheduleKind.Once => ReadDate(root, "at") is { } once && once > after ? once.ToUniversalTime() : null,
            AutomationScheduleKind.Hourly => after.ToUniversalTime().AddHours(Math.Max(1, ReadInt(root, "intervalHours", 1))),
            AutomationScheduleKind.Daily => NextDaily(after, ReadTime(root, "time", new TimeOnly(8, 0))),
            AutomationScheduleKind.Weekly => NextWeekly(after, ReadDay(root, "dayOfWeek", DayOfWeek.Monday), ReadTime(root, "time", new TimeOnly(8, 0))),
            AutomationScheduleKind.ConditionWatch => after.ToUniversalTime().AddMinutes(Math.Max(60, ReadInt(root, "intervalMinutes", 60))),
            _ => null
        };
    }

    public DateTimeOffset GetInitialRun(AutomationScheduleKind kind, string scheduleJson, DateTimeOffset now)
    {
        var placeholder = new AutomationDefinition(Guid.Empty, string.Empty, HavenMode.Chat, string.Empty, kind, scheduleJson, null, null, true, now, now);
        return GetNextRun(placeholder, now.AddTicks(-1)) ?? now.ToUniversalTime();
    }

    private DateTimeOffset NextDaily(DateTimeOffset after, TimeOnly time)
    {
        var localAfter = TimeZoneInfo.ConvertTime(after, _timeZone);
        var candidate = CreateLocal(localAfter.Date, time);
        if (candidate <= localAfter) candidate = CreateLocal(localAfter.Date.AddDays(1), time);
        return candidate.ToUniversalTime();
    }

    private DateTimeOffset NextWeekly(DateTimeOffset after, DayOfWeek day, TimeOnly time)
    {
        var localAfter = TimeZoneInfo.ConvertTime(after, _timeZone);
        var delta = ((int)day - (int)localAfter.DayOfWeek + 7) % 7;
        var candidate = CreateLocal(localAfter.Date.AddDays(delta), time);
        if (candidate <= localAfter) candidate = CreateLocal(localAfter.Date.AddDays(delta + 7), time);
        return candidate.ToUniversalTime();
    }

    private DateTimeOffset CreateLocal(DateTime date, TimeOnly time)
    {
        var unspecified = DateTime.SpecifyKind(date.Add(time.ToTimeSpan()), DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, _timeZone.GetUtcOffset(unspecified));
    }

    private static JsonDocument Parse(string json)
    {
        try { return JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json); }
        catch (JsonException exception) { throw new FormatException("The scheduled Task definition contains invalid schedule JSON.", exception); }
    }

    private static int ReadInt(JsonElement root, string property, int fallback) =>
        root.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) ? number : fallback;

    private static DateTimeOffset? ReadDate(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value)
        && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result)
            ? result
            : null;

    private static TimeOnly ReadTime(JsonElement root, string property, TimeOnly fallback) =>
        root.TryGetProperty(property, out var value)
        && TimeOnly.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var result)
            ? result
            : fallback;

    private static DayOfWeek ReadDay(JsonElement root, string property, DayOfWeek fallback) =>
        root.TryGetProperty(property, out var value) && Enum.TryParse<DayOfWeek>(value.GetString(), true, out var result)
            ? result
            : fallback;
}

public sealed record ScheduledTaskBatchResult(int Due, int Started, int Succeeded, int Failed, int Skipped);
public sealed record ScheduledTaskConditionResult(bool ConditionMet, string Report);

/// <summary>
/// Executes persisted scheduled Tasks on demand. It deliberately owns no timer,
/// OS scheduler registration, worker process, or hidden background loop.
/// </summary>
public sealed class ScheduledTaskRunner(
    IAutomationRepository repository,
    IOllamaClient ollama,
    ScheduledTaskScheduleCalculator schedules,
    DeviceAutomationNodeExecutor? deviceExecutor = null,
    BuiltInAutomationActionNodeExecutor? builtInExecutor = null)
{
    private const int MaximumAttempts = 3;

    public async Task<ScheduledTaskBatchResult> RunDueAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var due = await repository.GetDueAsync(now, cancellationToken).ConfigureAwait(false);
        var started = 0;
        var succeeded = 0;
        var failed = 0;
        var skipped = 0;
        foreach (var task in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var leaseToken = Guid.NewGuid().ToString("N");
            if (!await repository.TryAcquireLeaseAsync(task.Id, leaseToken, now.AddMinutes(15), cancellationToken).ConfigureAwait(false))
            {
                skipped++;
                continue;
            }
            started++;
            var run = await CompleteLeasedRunAsync(task, leaseToken, task.NextRunAt ?? now, cancellationToken).ConfigureAwait(false);
            if (run.Status == AutomationRunStatus.Succeeded) succeeded++;
            else if (run.Status == AutomationRunStatus.Failed) failed++;
        }
        return new ScheduledTaskBatchResult(due.Count, started, succeeded, failed, skipped);
    }

    public async Task<AutomationRun> RunOneAsync(
        AutomationDefinition task,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);
        var leaseToken = Guid.NewGuid().ToString("N");
        if (!await repository.TryAcquireLeaseAsync(task.Id, leaseToken, requestedAt.AddMinutes(15), cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("This Task is already running in another Haven process.");
        return await CompleteLeasedRunAsync(task, leaseToken, requestedAt, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AutomationRun> CompleteLeasedRunAsync(
        AutomationDefinition task,
        string leaseToken,
        DateTimeOffset scheduledFor,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            var result = await ExecuteWithRetryAsync(task, cancellationToken).ConfigureAwait(false);
            var completedAt = DateTimeOffset.UtcNow;
            var run = new AutomationRun(Guid.NewGuid(), task.Id, AutomationRunStatus.Succeeded, scheduledFor, startedAt, completedAt, result, null, leaseToken);
            await repository.CompleteRunAsync(run, NextRun(task, completedAt), cancellationToken).ConfigureAwait(false);
            return run;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var completedAt = DateTimeOffset.UtcNow;
            var run = new AutomationRun(Guid.NewGuid(), task.Id, AutomationRunStatus.Cancelled, scheduledFor, startedAt, completedAt, null, "Cancelled.", leaseToken);
            await repository.CompleteRunAsync(run, NextRun(task, completedAt), CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            var completedAt = DateTimeOffset.UtcNow;
            var run = new AutomationRun(Guid.NewGuid(), task.Id, AutomationRunStatus.Failed, scheduledFor, startedAt, completedAt, null, exception.Message, leaseToken);
            await repository.CompleteRunAsync(run, NextRun(task, completedAt), CancellationToken.None).ConfigureAwait(false);
            return run;
        }
    }

    private DateTimeOffset? NextRun(AutomationDefinition task, DateTimeOffset completedAt) =>
        task.IsEnabled ? schedules.GetNextRun(task, completedAt) : null;

    private async Task<string> ExecuteWithRetryAsync(AutomationDefinition task, CancellationToken cancellationToken)
    {
        var maximumAttempts = ScheduledGraphAutomationPayloadCodec.IsPayload(task.Instruction) ? 1 : MaximumAttempts;
        Exception? lastError = null;
        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return await ExecuteOnceAsync(task, attempt, maximumAttempts, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                lastError = exception;
                if (attempt == maximumAttempts) break;
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken).ConfigureAwait(false);
            }
        }
        throw new InvalidOperationException($"Task failed after {maximumAttempts} attempt{(maximumAttempts == 1 ? string.Empty : "s")}: {lastError?.Message}", lastError);
    }

    private async Task<string> ExecuteOnceAsync(AutomationDefinition task, int attempt, int maximumAttempts, CancellationToken cancellationToken)
    {
        if (ScheduledGraphAutomationPayloadCodec.IsPayload(task.Instruction))
        {
            if (!ScheduledGraphAutomationPayloadCodec.TryDeserialize(task.Instruction, out var payload))
                throw new InvalidOperationException("The scheduled graph payload is invalid, so Haven did not fall back to an instruction run.");
            return await ExecuteGraphPayloadAsync(task, payload, attempt, maximumAttempts, cancellationToken).ConfigureAwait(false);
        }

        if (task.ScheduleKind == AutomationScheduleKind.ConditionWatch)
        {
            var result = await EvaluateConditionAsync(task.Instruction, task.Mode, attempt, maximumAttempts, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Serialize(new { conditionMet = result.ConditionMet, report = result.Report, evaluatedAt = DateTimeOffset.UtcNow });
        }

        var models = await ollama.GetModelsAsync(cancellationToken).ConfigureAwait(false);
        var model = models.FirstOrDefault(candidate => candidate.Supports(ToolCapability.Text))
                    ?? throw new InvalidOperationException("No text-capable model is installed.");
        var system = $"You are Haven Automations executing a user-approved scheduled instruction. Mode: {task.Mode}. Execute safely and return a concise run report. Never claim external actions unless confirmed. Attempt {attempt} of {maximumAttempts}.";
        return await ollama.CompleteAsync(new OllamaChatRequest(
            model.Name,
            [new OllamaMessage("user", task.Instruction)],
            EffortLevel.Medium,
            system), cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> ExecuteGraphPayloadAsync(AutomationDefinition task, ScheduledGraphAutomationPayload payload, int attempt, int maximumAttempts, CancellationToken cancellationToken)
    {
        if (!AutomationGraphCodec.TryDeserialize(payload.GraphJson, out var graph))
            throw new InvalidOperationException("The captured scheduled graph is unreadable.");
        if (!AutomationGraphTriggerScope.TrySelect(graph, payload.TriggerNodeId, out var scoped, out var scopeError))
            throw new InvalidOperationException(scopeError ?? "The scheduled graph trigger is invalid.");

        ScheduledTaskConditionResult? condition = null;
        if (task.ScheduleKind == AutomationScheduleKind.ConditionWatch)
        {
            if (string.IsNullOrWhiteSpace(payload.WatchCondition))
                throw new InvalidOperationException("The scheduled Condition Watch has no condition to evaluate.");
            condition = await EvaluateConditionAsync(payload.WatchCondition, task.Mode, attempt, maximumAttempts, cancellationToken).ConfigureAwait(false);
            if (!condition.ConditionMet)
                return JsonSerializer.Serialize(new { conditionMet = false, report = condition.Report, graphExecuted = false, evaluatedAt = DateTimeOffset.UtcNow });
        }

        var scopedJson = AutomationGraphCodec.Serialize(scoped);
        var workflow = new ReusableTaskDefinition(
            payload.WorkflowId,
            string.IsNullOrWhiteSpace(payload.WorkflowName) ? task.Name : payload.WorkflowName,
            "Scheduled graph snapshot",
            string.Empty,
            task.ContainerId,
            true,
            task.CreatedAt,
            DateTimeOffset.UtcNow,
            scopedJson);
        var graphRun = await new ReusableDeviceWorkflowRunner(deviceExecutor, builtInExecutor)
            .RunAsync(workflow, permissionGranted: false, cancellationToken)
            .ConfigureAwait(false);
        if (!graphRun.Handled || graphRun.GraphResult is null)
            throw new InvalidOperationException("The scheduled graph could not be executed, and Haven did not substitute an instruction run.");
        if (!graphRun.GraphResult.Succeeded)
            throw new InvalidOperationException(graphRun.GraphResult.FailureMessage ?? graphRun.Message);

        return JsonSerializer.Serialize(new
        {
            conditionMet = condition?.ConditionMet,
            conditionReport = condition?.Report,
            graphExecuted = true,
            graphSucceeded = true,
            tracedNodes = graphRun.GraphResult.Trace.Count,
            completedAt = graphRun.GraphResult.CompletedAt
        });
    }

    private async Task<ScheduledTaskConditionResult> EvaluateConditionAsync(string instruction, HavenMode mode, int attempt, int maximumAttempts, CancellationToken cancellationToken)
    {
        var models = await ollama.GetModelsAsync(cancellationToken).ConfigureAwait(false);
        var model = models.FirstOrDefault(candidate => candidate.Supports(ToolCapability.Text))
                    ?? throw new InvalidOperationException("No text-capable model is installed.");
        var system = $"You are Haven Automations evaluating a scheduled condition. Mode: {mode}. Return one JSON object and no markdown: {{\"conditionMet\":true|false,\"report\":\"concise evidence-based report\"}}. Fail closed when evidence is missing or ambiguous. Never claim an external action unless it was confirmed. Attempt {attempt} of {maximumAttempts}.";
        var raw = await ollama.CompleteAsync(new OllamaChatRequest(
            model.Name,
            [new OllamaMessage("user", instruction)],
            EffortLevel.Medium,
            system), cancellationToken).ConfigureAwait(false);
        return ScheduledTaskConditionParser.Parse(raw);
    }
}

public static class ScheduledTaskConditionParser
{
    public static ScheduledTaskConditionResult Parse(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return new(false, "The condition check returned no evidence and was treated as not met.");
        try
        {
            var start = response.IndexOf('{');
            var end = response.LastIndexOf('}');
            if (start < 0 || end <= start) throw new JsonException("No JSON object was returned.");
            using var document = JsonDocument.Parse(response[start..(end + 1)]);
            var root = document.RootElement;
            var met = root.TryGetProperty("conditionMet", out var condition)
                      && condition.ValueKind is JsonValueKind.True or JsonValueKind.False
                      && condition.GetBoolean();
            var report = root.TryGetProperty("report", out var reportElement) ? reportElement.GetString()?.Trim() : null;
            return new(met, string.IsNullOrWhiteSpace(report)
                ? met ? "The condition was reported as met without further detail." : "The condition was not met."
                : report);
        }
        catch (JsonException)
        {
            var bounded = response.Trim();
            if (bounded.Length > 1000) bounded = bounded[..1000] + "…";
            return new(false, "The condition check returned an unstructured response and was treated as not met. Response: " + bounded);
        }
    }
}

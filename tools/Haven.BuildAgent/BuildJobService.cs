using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Haven.BuildAgent;

public sealed class JobStore
{
    private readonly ConcurrentDictionary<Guid, JobState> _jobs = new();

    public JobSnapshot Create(string kind)
    {
        var state = new JobState
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            Status = "queued",
            CreatedAt = DateTimeOffset.UtcNow
        };

        if (!_jobs.TryAdd(state.Id, state))
        {
            throw new InvalidOperationException("Unable to allocate a build job identifier.");
        }

        return Snapshot(state);
    }

    public JobSnapshot Get(Guid id)
    {
        return Snapshot(GetState(id));
    }

    public void MarkRunning(Guid id)
    {
        JobState state = GetState(id);
        lock (state.SyncRoot)
        {
            state.Status = "running";
            state.StartedAt = DateTimeOffset.UtcNow;
        }
    }

    public void Complete(
        Guid id,
        int exitCode,
        IReadOnlyList<BuildDiagnostic> diagnostics,
        TestSummary? tests,
        string consoleLogUrl,
        string? msBuildLogUrl,
        string? binaryLogUrl,
        string? testResultsUrl)
    {
        JobState state = GetState(id);
        lock (state.SyncRoot)
        {
            state.Status = exitCode == 0 ? "succeeded" : "failed";
            state.CompletedAt = DateTimeOffset.UtcNow;
            state.ExitCode = exitCode;
            state.Diagnostics = diagnostics;
            state.Tests = tests;
            state.ConsoleLogUrl = consoleLogUrl;
            state.MsBuildLogUrl = msBuildLogUrl;
            state.BinaryLogUrl = binaryLogUrl;
            state.TestResultsUrl = testResultsUrl;
        }
    }

    public void Fail(Guid id, Exception exception, string? consoleLogUrl = null)
    {
        JobState state = GetState(id);
        lock (state.SyncRoot)
        {
            state.Status = "failed";
            state.CompletedAt = DateTimeOffset.UtcNow;
            state.ExitCode = -1;
            state.Failure = exception.Message;
            state.ConsoleLogUrl = consoleLogUrl;
        }
    }

    private JobState GetState(Guid id)
    {
        if (!_jobs.TryGetValue(id, out JobState? state))
        {
            throw new KeyNotFoundException($"Unknown job '{id}'.");
        }

        return state;
    }

    private static JobSnapshot Snapshot(JobState state)
    {
        lock (state.SyncRoot)
        {
            return new JobSnapshot(
                state.Id,
                state.Kind,
                state.Status,
                state.CreatedAt,
                state.StartedAt,
                state.CompletedAt,
                state.ExitCode,
                state.Diagnostics.ToArray(),
                state.Tests,
                state.ConsoleLogUrl,
                state.MsBuildLogUrl,
                state.BinaryLogUrl,
                state.TestResultsUrl,
                state.Failure);
        }
    }

    private sealed class JobState
    {
        public object SyncRoot { get; } = new();

        public Guid Id { get; init; }

        public string Kind { get; init; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset? StartedAt { get; set; }

        public DateTimeOffset? CompletedAt { get; set; }

        public int? ExitCode { get; set; }

        public IReadOnlyList<BuildDiagnostic> Diagnostics { get; set; } = [];

        public TestSummary? Tests { get; set; }

        public string? ConsoleLogUrl { get; set; }

        public string? MsBuildLogUrl { get; set; }

        public string? BinaryLogUrl { get; set; }

        public string? TestResultsUrl { get; set; }

        public string? Failure { get; set; }
    }
}

public sealed class BuildJobService
{
    private static readonly Regex DiagnosticLineRegex = new(
        @"^(?:(?<origin>.+?)\s*:\s*)?(?<severity>error|warning)\s+(?<code>[A-Za-z]+\d+):\s*(?<message>.*?)(?:\s+\[(?<project>[^\]]+)\])?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    private static readonly Regex FileLocationRegex = new(
        @"^(?<file>.*)\((?<line>\d+)(?:,(?<column>\d+))?\)$",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex TestSummaryRegex = new(
        @"(?:Passed|Failed)!\s*-\s*Failed:\s*(?<failed>\d+),\s*Passed:\s*(?<passed>\d+),\s*Skipped:\s*(?<skipped>\d+),\s*Total:\s*(?<total>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    private static readonly string[] AllowedConfigurations = ["Debug", "Release"];
    private static readonly string[] AllowedVerbosities = ["quiet", "minimal", "normal", "detailed", "diagnostic"];

    private readonly BuildAgentOptions _options;
    private readonly JobStore _jobs;
    private readonly ILogger<BuildJobService> _logger;

    public BuildJobService(
        IOptions<BuildAgentOptions> options,
        JobStore jobs,
        ILogger<BuildJobService> logger)
    {
        _options = options.Value;
        _jobs = jobs;
        _logger = logger;
    }

    public JobSnapshot StartBuild(BuildRequest request)
    {
        ValidateConfiguration(request.Configuration);
        ValidateVerbosity(request.Verbosity);
        _options.GetBuildProfile(request.Profile);

        JobSnapshot job = _jobs.Create("build");
        _ = Task.Run(() => ExecuteBuildAsync(job.Id, request));
        return job;
    }

    public JobSnapshot StartTest(TestRequest request)
    {
        ValidateConfiguration(request.Configuration);
        ValidateVerbosity(request.Verbosity);
        _options.GetBuildProfile(request.Profile);

        JobSnapshot job = _jobs.Create("test");
        _ = Task.Run(() => ExecuteTestAsync(job.Id, request));
        return job;
    }

    public JobSnapshot Get(Guid id) => _jobs.Get(id);

    private async Task ExecuteBuildAsync(Guid jobId, BuildRequest request)
    {
        string? consoleLogUrl = null;
        try
        {
            _jobs.MarkRunning(jobId);
            BuildProfile profile = _options.GetBuildProfile(request.Profile);
            string targetPath = _options.ResolveRepositoryPath(profile.Target);
            string artifactDirectory = _options.CreateArtifactDirectory("builds", jobId);
            string consoleLogPath = Path.Combine(artifactDirectory, "console.log");
            string msBuildLogPath = Path.Combine(artifactDirectory, "msbuild.log");
            string binaryLogPath = Path.Combine(artifactDirectory, "build.binlog");
            consoleLogUrl = _options.ToArtifactUrl(consoleLogPath);

            string[] arguments =
            [
                "build",
                targetPath,
                "--configuration",
                NormalizeConfiguration(request.Configuration),
                "--nologo",
                "--verbosity",
                request.Verbosity.ToLowerInvariant(),
                $"-bl:{binaryLogPath}",
                "-fl",
                $"-flp:logfile={msBuildLogPath};verbosity=normal"
            ];

            ProcessResult result = await RunDotnetAsync(arguments, consoleLogPath).ConfigureAwait(false);
            IReadOnlyList<BuildDiagnostic> diagnostics = ParseDiagnostics(result.CombinedOutput);

            _jobs.Complete(
                jobId,
                result.ExitCode,
                diagnostics,
                null,
                consoleLogUrl,
                File.Exists(msBuildLogPath) ? _options.ToArtifactUrl(msBuildLogPath) : null,
                File.Exists(binaryLogPath) ? _options.ToArtifactUrl(binaryLogPath) : null,
                null);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Build job {JobId} failed unexpectedly.", jobId);
            _jobs.Fail(jobId, exception, consoleLogUrl);
        }
    }

    private async Task ExecuteTestAsync(Guid jobId, TestRequest request)
    {
        string? consoleLogUrl = null;
        try
        {
            _jobs.MarkRunning(jobId);
            BuildProfile profile = _options.GetBuildProfile(request.Profile);
            string targetPath = _options.ResolveRepositoryPath(profile.Target);
            string artifactDirectory = _options.CreateArtifactDirectory("tests", jobId);
            string consoleLogPath = Path.Combine(artifactDirectory, "console.log");
            string binaryLogPath = Path.Combine(artifactDirectory, "test.binlog");
            string trxPath = Path.Combine(artifactDirectory, "results.trx");
            consoleLogUrl = _options.ToArtifactUrl(consoleLogPath);

            var arguments = new List<string>
            {
                "test",
                targetPath,
                "--configuration",
                NormalizeConfiguration(request.Configuration),
                "--nologo",
                "--verbosity",
                request.Verbosity.ToLowerInvariant(),
                "--logger",
                $"trx;LogFileName={Path.GetFileName(trxPath)}",
                "--results-directory",
                artifactDirectory,
                $"-bl:{binaryLogPath}"
            };

            if (request.NoBuild)
            {
                arguments.Add("--no-build");
            }

            ProcessResult result = await RunDotnetAsync(arguments, consoleLogPath).ConfigureAwait(false);
            IReadOnlyList<BuildDiagnostic> diagnostics = ParseDiagnostics(result.CombinedOutput);
            TestSummary? tests = ParseTestSummary(result.CombinedOutput);

            _jobs.Complete(
                jobId,
                result.ExitCode,
                diagnostics,
                tests,
                consoleLogUrl,
                null,
                File.Exists(binaryLogPath) ? _options.ToArtifactUrl(binaryLogPath) : null,
                File.Exists(trxPath) ? _options.ToArtifactUrl(trxPath) : null);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Test job {JobId} failed unexpectedly.", jobId);
            _jobs.Fail(jobId, exception, consoleLogUrl);
        }
    }

    private async Task<ProcessResult> RunDotnetAsync(IReadOnlyList<string> arguments, string consoleLogPath)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = _options.RepositoryRootPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.StartInfo.Environment["DOTNET_NOLOGO"] = "1";
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        _logger.LogInformation("Starting dotnet job: dotnet {Arguments}", string.Join(' ', arguments));
        if (!process.Start())
        {
            throw new InvalidOperationException("dotnet failed to start.");
        }

        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw new TimeoutException("The dotnet command exceeded the 20-minute safety timeout.");
        }

        string standardOutput = await standardOutputTask.ConfigureAwait(false);
        string standardError = await standardErrorTask.ConfigureAwait(false);
        string combinedOutput = string.Join(
            Environment.NewLine,
            new[] { standardOutput, standardError }.Where(value => !string.IsNullOrWhiteSpace(value)));

        await File.WriteAllTextAsync(consoleLogPath, combinedOutput).ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, combinedOutput);
    }

    private static IReadOnlyList<BuildDiagnostic> ParseDiagnostics(string output)
    {
        var diagnostics = new List<BuildDiagnostic>();
        foreach (string line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Match match = DiagnosticLineRegex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            string? origin = NullIfEmpty(match.Groups["origin"].Value);
            string? file = null;
            int? lineNumber = null;
            int? columnNumber = null;

            if (origin is not null)
            {
                Match location = FileLocationRegex.Match(origin);
                if (location.Success)
                {
                    file = NullIfEmpty(location.Groups["file"].Value);
                    lineNumber = ParseNullableInteger(location.Groups["line"].Value);
                    columnNumber = ParseNullableInteger(location.Groups["column"].Value);
                }
            }

            diagnostics.Add(new BuildDiagnostic(
                match.Groups["severity"].Value.ToLowerInvariant(),
                match.Groups["code"].Value,
                match.Groups["message"].Value.Trim(),
                file,
                lineNumber,
                columnNumber,
                NullIfEmpty(match.Groups["project"].Value),
                origin));
        }

        return diagnostics
            .DistinctBy(diagnostic => new
            {
                diagnostic.Severity,
                diagnostic.Code,
                diagnostic.Message,
                diagnostic.File,
                diagnostic.Line,
                diagnostic.Column,
                diagnostic.Project
            })
            .ToArray();
    }

    private static TestSummary? ParseTestSummary(string output)
    {
        MatchCollection matches = TestSummaryRegex.Matches(output);
        if (matches.Count == 0)
        {
            return null;
        }

        Match match = matches[^1];
        return new TestSummary(
            int.Parse(match.Groups["failed"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["passed"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["skipped"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["total"].Value, CultureInfo.InvariantCulture));
    }

    private static void ValidateConfiguration(string configuration)
    {
        if (!AllowedConfigurations.Contains(configuration, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Configuration must be Debug or Release.", nameof(configuration));
        }
    }

    private static string NormalizeConfiguration(string configuration)
    {
        return AllowedConfigurations.First(value => string.Equals(value, configuration, StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateVerbosity(string verbosity)
    {
        if (!AllowedVerbosities.Contains(verbosity, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Unsupported verbosity '{verbosity}'.", nameof(verbosity));
        }
    }

    private static int? ParseNullableInteger(string value)
    {
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ? parsed : null;
    }

    private static string? NullIfEmpty(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private sealed record ProcessResult(int ExitCode, string CombinedOutput);
}

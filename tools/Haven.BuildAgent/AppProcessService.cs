using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;

namespace Haven.BuildAgent;

public sealed class AppProcessService
{
    private static readonly string[] AllowedConfigurations = ["Debug", "Release"];

    private readonly BuildAgentOptions _options;
    private readonly ConcurrentDictionary<Guid, ManagedRun> _runs = new();
    private readonly ILogger<AppProcessService> _logger;

    public AppProcessService(IOptions<BuildAgentOptions> options, ILogger<AppProcessService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public RunSnapshot Start(StartRunRequest request)
    {
        string configuration = NormalizeConfiguration(request.Configuration);
        RunProfile profile = _options.GetRunProfile(request.Profile);
        Guid runId = Guid.NewGuid();
        string runDirectory = _options.CreateArtifactDirectory("runs", runId);
        string logPath = Path.Combine(runDirectory, "application.log");
        string executableRelativePath = profile.Executable.Replace("{Configuration}", configuration, StringComparison.OrdinalIgnoreCase);
        string executablePath = _options.ResolveRepositoryPath(executableRelativePath);
        string workingDirectory = _options.ResolveRepositoryPath(profile.WorkingDirectory);

        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                $"The configured executable does not exist. Build Haven in {configuration} first.",
                executablePath);
        }

        string? dataProfilePath = null;
        var startInfo = new ProcessStartInfo(executablePath)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = false
        };

        if (request.FreshDataProfile)
        {
            dataProfilePath = Path.Combine(runDirectory, "data-profile");
            Directory.CreateDirectory(dataProfilePath);
            startInfo.Environment["HAVEN_DATA_DIR"] = dataProfilePath;
        }

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        TextWriter logWriter = TextWriter.Synchronized(new StreamWriter(logPath, append: false, Encoding.UTF8)
        {
            AutoFlush = true
        });

        if (!process.Start())
        {
            logWriter.Dispose();
            process.Dispose();
            throw new InvalidOperationException("Haven failed to start.");
        }

        var managed = new ManagedRun(
            runId,
            request.Profile,
            configuration,
            process,
            logWriter,
            _options.ToArtifactUrl(logPath),
            dataProfilePath);

        if (!_runs.TryAdd(runId, managed))
        {
            process.Kill(entireProcessTree: true);
            logWriter.Dispose();
            process.Dispose();
            throw new InvalidOperationException("Unable to allocate a run identifier.");
        }

        process.OutputDataReceived += (_, eventArgs) => managed.AppendLog("stdout", eventArgs.Data);
        process.ErrorDataReceived += (_, eventArgs) => managed.AppendLog("stderr", eventArgs.Data);
        process.Exited += (_, _) => managed.MarkExited();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        _logger.LogInformation("Started Haven run {RunId} as process {ProcessId}.", runId, process.Id);
        return managed.Snapshot();
    }

    public IReadOnlyList<RunSnapshot> List()
    {
        return _runs.Values
            .Select(run => run.Snapshot())
            .OrderByDescending(run => run.StartedAt)
            .ToArray();
    }

    public RunSnapshot Get(Guid runId)
    {
        return GetManagedRun(runId).Snapshot();
    }

    public Process GetRunningProcess(Guid runId)
    {
        ManagedRun run = GetManagedRun(runId);
        Process process = run.Process;
        process.Refresh();
        if (process.HasExited)
        {
            run.MarkExited();
            throw new InvalidOperationException($"Run '{runId}' has already exited.");
        }

        return process;
    }

    public RunSnapshot Stop(Guid runId)
    {
        ManagedRun run = GetManagedRun(runId);
        try
        {
            if (!run.Process.HasExited)
            {
                run.Process.Kill(entireProcessTree: true);
                run.Process.WaitForExit(10_000);
            }
        }
        catch (InvalidOperationException)
        {
        }

        run.MarkExited();
        _logger.LogInformation("Stopped Haven run {RunId}.", runId);
        return run.Snapshot();
    }

    private ManagedRun GetManagedRun(Guid runId)
    {
        if (!_runs.TryGetValue(runId, out ManagedRun? run))
        {
            throw new KeyNotFoundException($"Unknown run '{runId}'.");
        }

        return run;
    }

    private static string NormalizeConfiguration(string configuration)
    {
        string? normalized = AllowedConfigurations.FirstOrDefault(
            value => string.Equals(value, configuration, StringComparison.OrdinalIgnoreCase));
        return normalized ?? throw new ArgumentException("Configuration must be Debug or Release.", nameof(configuration));
    }

    private sealed class ManagedRun
    {
        private readonly object _syncRoot = new();
        private readonly TextWriter _logWriter;
        private string _status = "running";
        private DateTimeOffset? _exitedAt;
        private int? _exitCode;
        private string? _failure;

        public ManagedRun(
            Guid id,
            string profile,
            string configuration,
            Process process,
            TextWriter logWriter,
            string logUrl,
            string? dataProfilePath)
        {
            Id = id;
            Profile = profile;
            Configuration = configuration;
            Process = process;
            _logWriter = logWriter;
            LogUrl = logUrl;
            DataProfilePath = dataProfilePath;
            StartedAt = DateTimeOffset.UtcNow;
        }

        public Guid Id { get; }

        public string Profile { get; }

        public string Configuration { get; }

        public Process Process { get; }

        public string LogUrl { get; }

        public string? DataProfilePath { get; }

        public DateTimeOffset StartedAt { get; }

        public void AppendLog(string stream, string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (_syncRoot)
            {
                try
                {
                    _logWriter.WriteLine("[{0:O}] [{1}] {2}", DateTimeOffset.UtcNow, stream, line);
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        public void MarkExited()
        {
            lock (_syncRoot)
            {
                if (_status == "exited")
                {
                    return;
                }

                _status = "exited";
                _exitedAt = DateTimeOffset.UtcNow;
                try
                {
                    _exitCode = Process.ExitCode;
                }
                catch (InvalidOperationException exception)
                {
                    _failure = exception.Message;
                }
            }
        }

        public RunSnapshot Snapshot()
        {
            lock (_syncRoot)
            {
                return new RunSnapshot(
                    Id,
                    Profile,
                    Configuration,
                    _status,
                    Process.Id,
                    StartedAt,
                    _exitedAt,
                    _exitCode,
                    LogUrl,
                    DataProfilePath,
                    _failure);
            }
        }
    }
}

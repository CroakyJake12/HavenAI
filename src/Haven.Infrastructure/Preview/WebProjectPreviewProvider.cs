using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>Starts supported web projects on loopback without a command shell.</summary>
public sealed class WebProjectPreviewProvider(IExecutionEventSink events) : IProjectPreviewProvider
{
    public string Id => "project.web";

    public bool CanPreview(string projectRoot) => DescribeLaunch(projectRoot) is not null;

    public ProjectPreviewDescriptor Describe(string projectRoot)
    {
        var launch = DescribeLaunch(projectRoot) ?? throw new NotSupportedException("This project does not expose a supported web preview.");
        return new ProjectPreviewDescriptor(Id, ProjectPreviewKind.Website, "Website preview", Path.GetFullPath(projectRoot), launch.Description);
    }

    public async Task<IProjectPreviewSession> StartAsync(string projectRoot, CancellationToken cancellationToken)
    {
        var descriptor = Describe(projectRoot);
        var launch = DescribeLaunch(projectRoot)!;
        var port = ReservePort();
        var uri = new Uri($"http://127.0.0.1:{port}/", UriKind.Absolute);
        var arguments = launch.Arguments(port);
        var start = new ProcessStartInfo(launch.Executable, arguments)
        {
            WorkingDirectory = descriptor.ProjectRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        CopySafeEnvironment(start);
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        if (!process.Start()) throw new InvalidOperationException("The preview process did not start.");
        var session = new WebPreviewSession(descriptor, uri, process);
        events.TryPublish(new ExecutionEvent(Guid.NewGuid(), session.ExecutionId, session.ActionId, null, ExecutionOrigin.Haven,
            ExecutionActionType.Preview, ExecutionActionStatus.Running, "Start Project preview", "Run the supported project preview on loopback.",
            descriptor.EntryDescription, Id, DateTimeOffset.UtcNow, StartedAt: DateTimeOffset.UtcNow));
        try
        {
            await WaitUntilReadyAsync(session, cancellationToken).ConfigureAwait(false);
            events.TryPublish(new ExecutionEvent(Guid.NewGuid(), session.ExecutionId, session.ActionId, null, ExecutionOrigin.Haven,
                ExecutionActionType.Preview, ExecutionActionStatus.Completed, "Project preview ready", null, uri.ToString(), Id,
                DateTimeOffset.UtcNow, session.StartedAt, DateTimeOffset.UtcNow));
            return session;
        }
        catch (Exception ex)
        {
            await session.DisposeAsync().ConfigureAwait(false);
            events.TryPublish(new ExecutionEvent(Guid.NewGuid(), session.ExecutionId, session.ActionId, null, ExecutionOrigin.Haven,
                ExecutionActionType.Preview, ExecutionActionStatus.Failed, "Project preview failed", null, session.SafeLogTail, Id,
                DateTimeOffset.UtcNow, session.StartedAt, DateTimeOffset.UtcNow,
                Failure: new ExecutionFailure("PREVIEW_START_FAILED", "Preview failed to start", SensitiveTextRedactor.Redact(ex.Message, 4_000), AffectedComponent: Id)));
            throw new InvalidOperationException("Project preview failed to start. " + session.SafeLogTail, ex);
        }
    }

    private static PreviewLaunch? DescribeLaunch(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return null;
        var package = Path.Combine(root, "package.json");
        if (File.Exists(package))
        {
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(package));
                if (json.RootElement.TryGetProperty("scripts", out var scripts) && scripts.TryGetProperty("dev", out _))
                    return new PreviewLaunch(OperatingSystem.IsWindows() ? "npm.cmd" : "npm", port => $"run dev -- --host 127.0.0.1 --port {port}", "package.json dev script");
                if (json.RootElement.TryGetProperty("scripts", out scripts) && scripts.TryGetProperty("start", out _))
                    return new PreviewLaunch(OperatingSystem.IsWindows() ? "npm.cmd" : "npm", port => $"run start -- --host 127.0.0.1 --port {port}", "package.json start script");
            }
            catch (JsonException) { return null; }
        }
        var webProject = Directory.EnumerateFiles(root, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault(path =>
        {
            try { return File.ReadAllText(path).Contains("Microsoft.NET.Sdk.Web", StringComparison.Ordinal); }
            catch { return false; }
        });
        return webProject is null ? null : new PreviewLaunch("dotnet", port => $"run --no-launch-profile --urls http://127.0.0.1:{port}", "ASP.NET Core web project");
    }

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task WaitUntilReadyAsync(WebPreviewSession session, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            if (session.HasExited) throw new InvalidOperationException("The preview process exited before becoming ready.");
            try
            {
                using var response = await client.GetAsync(session.PreviewUri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                if ((int)response.StatusCode < 500) return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!timeout.IsCancellationRequested) { }
            await Task.Delay(250, timeout.Token).ConfigureAwait(false);
        }
    }

    private static void CopySafeEnvironment(ProcessStartInfo start)
    {
        start.Environment.Clear();
        foreach (var name in new[] { "PATH", "Path", "SystemRoot", "ProgramFiles", "ProgramFiles(x86)", "DOTNET_ROOT", "APPDATA", "LOCALAPPDATA", "TEMP", "TMP" })
            if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } value) start.Environment[name] = value;
        start.Environment["BROWSER"] = "none";
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
    }

    private sealed record PreviewLaunch(string Executable, Func<int, string> Arguments, string Description);

    private sealed class WebPreviewSession : IProjectPreviewSession
    {
        private readonly Process _process;
        private readonly FileSystemWatcher _watcher;
        private readonly Queue<string> _log = new();
        private int _disposed;

        public WebPreviewSession(ProjectPreviewDescriptor descriptor, Uri uri, Process process)
        {
            Descriptor = descriptor;
            PreviewUri = uri;
            _process = process;
            StartedAt = DateTimeOffset.UtcNow;
            ExecutionId = Guid.NewGuid();
            ActionId = Guid.NewGuid();
            process.OutputDataReceived += OnOutput;
            process.ErrorDataReceived += OnOutput;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _watcher = new FileSystemWatcher(descriptor.ProjectRoot)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                EnableRaisingEvents = true
            };
            _watcher.Changed += OnChanged;
            _watcher.Created += OnChanged;
            _watcher.Deleted += OnChanged;
            _watcher.Renamed += OnChanged;
        }

        public ProjectPreviewDescriptor Descriptor { get; }
        public Uri PreviewUri { get; }
        public Guid ExecutionId { get; }
        public Guid ActionId { get; }
        public DateTimeOffset StartedAt { get; }
        public bool HasExited => _process.HasExited;
        public string SafeLogTail { get { lock (_log) return string.Join(Environment.NewLine, _log); } }
        public event EventHandler? SourceChanged;

        private void OnOutput(object? sender, DataReceivedEventArgs args)
        {
            if (string.IsNullOrWhiteSpace(args.Data)) return;
            var safe = SensitiveTextRedactor.Redact(args.Data, 1_000);
            lock (_log)
            {
                _log.Enqueue(safe);
                while (_log.Count > 30) _log.Dequeue();
            }
        }

        private void OnChanged(object? sender, FileSystemEventArgs args)
        {
            var relative = Path.GetRelativePath(Descriptor.ProjectRoot, args.FullPath);
            if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" or "node_modules" or ".git")) return;
            SourceChanged?.Invoke(this, EventArgs.Empty);
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return ValueTask.CompletedTask;
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnChanged;
            _watcher.Created -= OnChanged;
            _watcher.Deleted -= OnChanged;
            _watcher.Renamed -= OnChanged;
            _watcher.Dispose();
            _process.OutputDataReceived -= OnOutput;
            _process.ErrorDataReceived -= OnOutput;
            if (!_process.HasExited)
            {
                try { _process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }
            _process.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

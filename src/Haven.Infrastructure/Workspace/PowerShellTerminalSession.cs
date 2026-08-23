using System.Diagnostics;
using System.Globalization;
using System.Text;
using Haven.Application;

namespace Haven.Infrastructure;

public sealed class PowerShellTerminalSessionFactory : ITerminalSessionFactory
{
    public ITerminalSession Create(string initialDirectory, string? displayName = null) =>
        new PowerShellTerminalSession(initialDirectory, displayName);
}

public sealed class PowerShellTerminalSession : ITerminalSession
{
    private const string CompletionPrefix = "__HAVEN_DONE__|";
    private const string ExitFrame = "__HAVEN_EXIT__";
    private static readonly string HostScript = """
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'
while ($true) {
    $__haven_internal_frame = [Console]::In.ReadLine()
    if ($null -eq $__haven_internal_frame -or $__haven_internal_frame -eq '__HAVEN_EXIT__') { break }
    $__haven_internal_parts = $__haven_internal_frame.Split('|', 3)
    if ($__haven_internal_parts.Length -ne 3 -or $__haven_internal_parts[0] -ne 'CMD') { continue }
    $__haven_internal_id = $__haven_internal_parts[1]
    $__haven_internal_code = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($__haven_internal_parts[2]))
    $global:LASTEXITCODE = 0
    $__haven_internal_error = [ref]$false
    try {
        . ([ScriptBlock]::Create($__haven_internal_code)) *>&1 | ForEach-Object {
            if ($_ -is [System.Management.Automation.ErrorRecord]) {
                $__haven_internal_error.Value = $true
                [Console]::Error.WriteLine(($_ | Out-String).TrimEnd())
                [Console]::Error.Flush()
            } else {
                $__haven_internal_text = if ($_ -is [string]) { $_ } else { ($_ | Out-String).TrimEnd() }
                if ($null -ne $__haven_internal_text) { [Console]::Out.WriteLine($__haven_internal_text) }
                [Console]::Out.Flush()
            }
        }
    } catch {
        $__haven_internal_error.Value = $true
        [Console]::Error.WriteLine(($_ | Out-String).TrimEnd())
        [Console]::Error.Flush()
    }
    $__haven_internal_exit = if ($global:LASTEXITCODE -ne 0) { [int]$global:LASTEXITCODE } elseif (-not $__haven_internal_error.Value) { 0 } else { 1 }
    $__haven_internal_cwd = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes((Get-Location).Path))
    [Console]::Out.WriteLine("__HAVEN_DONE__|$__haven_internal_id|$__haven_internal_exit|$__haven_internal_cwd")
    [Console]::Out.Flush()
}
""";

    private readonly object _sync = new();
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly SemaphoreSlim _restartGate = new(1, 1);
    private Process? _process;
    private ActiveCommand? _active;
    private int _generation;
    private int _disposed;

    public PowerShellTerminalSession(string initialDirectory, string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(initialDirectory)) throw new ArgumentException("An initial directory is required.", nameof(initialDirectory));
        var root = Path.GetFullPath(initialDirectory);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var id = Guid.NewGuid();
        Metadata = new TerminalSessionMetadata(id, OperatingSystem.IsWindows() ? "Windows PowerShell" : "PowerShell",
            string.IsNullOrWhiteSpace(displayName) ? "Terminal" : displayName.Trim(), root, root,
            TerminalSessionLifecycleState.Starting, DateTimeOffset.UtcNow, 0, false);
        StartShell(root, stateWasReset: false);
    }

    public TerminalSessionMetadata Metadata { get; private set; }
    public int? ProcessId
    {
        get
        {
            lock (_sync)
            {
                try { return _process is { HasExited: false } process ? process.Id : null; }
                catch (InvalidOperationException) { return null; }
            }
        }
    }

    public event EventHandler<TerminalSessionOutput>? OutputReceived;
    public event EventHandler<TerminalSessionMetadata>? MetadataChanged;

    public async Task<TerminalSessionCommandResult> ExecuteAsync(Guid commandId, string command, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (commandId == Guid.Empty) throw new ArgumentException("A command ID is required.", nameof(commandId));
        if (string.IsNullOrWhiteSpace(command)) throw new ArgumentException("A command is required.", nameof(command));

        await _commandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        ActiveCommand? active = null;
        try
        {
            Process process;
            TerminalSessionMetadata changed;
            lock (_sync)
            {
                process = GetLiveProcessLocked();
                active = new ActiveCommand(commandId, Stopwatch.GetTimestamp());
                _active = active;
                changed = SetMetadataLocked(Metadata with { State = TerminalSessionLifecycleState.Running });
            }
            MetadataChanged?.Invoke(this, changed);

            var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(command));
            await process.StandardInput.WriteLineAsync($"CMD|{commandId:N}|{payload}").ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            using var registration = cancellationToken.Register(static state =>
            {
                var session = (PowerShellTerminalSession)state!;
                _ = session.InterruptAsync(CancellationToken.None);
            }, this);
            return await active.Completion.Task.ConfigureAwait(false);
        }
        finally
        {
            lock (_sync)
            {
                if (active is not null && ReferenceEquals(_active, active)) _active = null;
            }
            _commandGate.Release();
        }
    }

    public async Task SetWorkingDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException(fullPath);
        var escaped = fullPath.Replace("'", "''", StringComparison.Ordinal);
        var result = await ExecuteAsync(Guid.NewGuid(), $"Set-Location -LiteralPath '{escaped}'", cancellationToken).ConfigureAwait(false);
        if (result.Cancelled) throw new OperationCanceledException(cancellationToken);
        if (result.ExitCode is not 0) throw new InvalidOperationException("PowerShell could not change the Terminal working directory.");
    }

    public async Task InterruptAsync(CancellationToken cancellationToken)
    {
        await _restartGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ActiveCommand? active;
            Process? process;
            string restartDirectory;
            TerminalSessionMetadata? changed = null;
            lock (_sync)
            {
                if (Volatile.Read(ref _disposed) != 0) return;
                active = _active;
                process = _process;
                if (active is null || process is null || SafeHasExited(process)) return;
                active.CancelRequested = true;
                restartDirectory = Metadata.CurrentWorkingDirectory ?? Metadata.InitialWorkingDirectory;
                changed = SetMetadataLocked(Metadata with { State = TerminalSessionLifecycleState.Interrupting });
            }
            if (changed is not null) MetadataChanged?.Invoke(this, changed);

            TryKillTree(process);
            try { await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false); }
            catch (InvalidOperationException) { }
            DetachProcess(process);
            process.Dispose();

            if (Volatile.Read(ref _disposed) != 0) return;
            try
            {
                StartShell(restartDirectory, stateWasReset: true);
                PublishOutput(null, TerminalOutputStream.System, "Shell restarted after interrupt; cwd was restored, but shell variables/functions/environment from the ended process were reset.");
                active.Completion.TrySetResult(new TerminalSessionCommandResult(Metadata.SessionId, active.Id, null, true,
                    Stopwatch.GetElapsedTime(active.StartedTimestamp), Metadata.CurrentWorkingDirectory, true, true));
            }
            catch (Exception ex)
            {
                PublishOutput(null, TerminalOutputStream.System, $"Shell restart failed after interrupt: {ex.Message}");
                active.Completion.TrySetResult(new TerminalSessionCommandResult(Metadata.SessionId, active.Id, null, true,
                    Stopwatch.GetElapsedTime(active.StartedTimestamp), Metadata.CurrentWorkingDirectory, false, true));
            }
        }
        finally
        {
            _restartGate.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Process? process;
        ActiveCommand? active;
        TerminalSessionMetadata changed;
        lock (_sync)
        {
            process = _process;
            _process = null;
            active = _active;
            if (active is not null) active.CancelRequested = true;
            changed = SetMetadataLocked(Metadata with { State = TerminalSessionLifecycleState.Disposed });
        }
        MetadataChanged?.Invoke(this, changed);
        if (process is not null)
        {
            try { if (!SafeHasExited(process)) { process.StandardInput.WriteLine(ExitFrame); process.StandardInput.Flush(); process.StandardInput.Close(); } } catch { }
            TryKillTree(process);
            try { if (!SafeHasExited(process)) process.WaitForExit(3_000); } catch { }
            DetachProcess(process);
            process.Dispose();
        }
        active?.Completion.TrySetResult(new TerminalSessionCommandResult(Metadata.SessionId, active.Id, null, true,
            Stopwatch.GetElapsedTime(active.StartedTimestamp), Metadata.CurrentWorkingDirectory, false, Metadata.StateWasReset));
        // The gates are intentionally left for GC: an in-flight ExecuteAsync/InterruptAsync may still be unwinding
        // and must be able to release them after Dispose has completed the active command.
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private void StartShell(string workingDirectory, bool stateWasReset)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var shell = OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";
        var encodedHost = Convert.ToBase64String(Encoding.Unicode.GetBytes(HostScript));
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = shell,
                Arguments = $"-NoLogo -NoProfile -NonInteractive -InputFormat Text -OutputFormat Text -EncodedCommand {encodedHost}",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            },
            EnableRaisingEvents = true
        };
        process.OutputDataReceived += OnOutputDataReceived;
        process.ErrorDataReceived += OnErrorDataReceived;
        process.Exited += OnProcessExited;

        TerminalSessionMetadata changed;
        lock (_sync)
        {
            _process = process;
            _generation++;
            changed = SetMetadataLocked(Metadata with
            {
                State = TerminalSessionLifecycleState.Starting,
                CurrentWorkingDirectory = workingDirectory,
                Generation = _generation,
                StateWasReset = stateWasReset
            });
        }
        MetadataChanged?.Invoke(this, changed);
        try
        {
            if (!process.Start()) throw new InvalidOperationException($"Could not start {shell}.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            lock (_sync) changed = SetMetadataLocked(Metadata with { State = TerminalSessionLifecycleState.Ready });
            MetadataChanged?.Invoke(this, changed);
        }
        catch
        {
            lock (_sync)
            {
                if (ReferenceEquals(_process, process)) _process = null;
                changed = SetMetadataLocked(Metadata with { State = TerminalSessionLifecycleState.Faulted });
            }
            MetadataChanged?.Invoke(this, changed);
            DetachProcess(process);
            process.Dispose();
            throw;
        }
    }

    private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is null) return;
        if (TryCompleteCommand(e.Data)) return;
        Guid? commandId;
        lock (_sync) commandId = _active?.Id;
        PublishOutput(commandId, TerminalOutputStream.StandardOutput, e.Data);
    }

    private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is null) return;
        Guid? commandId;
        lock (_sync) commandId = _active?.Id;
        PublishOutput(commandId, TerminalOutputStream.StandardError, e.Data);
    }

    private bool TryCompleteCommand(string line)
    {
        if (!line.StartsWith(CompletionPrefix, StringComparison.Ordinal)) return false;
        var parts = line.Split('|', 4);
        if (parts.Length != 4 || !Guid.TryParseExact(parts[1], "N", out var id) ||
            !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var exitCode)) return false;
        string? cwd = null;
        try { cwd = Encoding.UTF8.GetString(Convert.FromBase64String(parts[3])); } catch (FormatException) { }

        ActiveCommand? active;
        TerminalSessionMetadata? changed = null;
        lock (_sync)
        {
            active = _active;
            if (active is null || active.Id != id) return true;
            changed = SetMetadataLocked(Metadata with
            {
                State = TerminalSessionLifecycleState.Ready,
                CurrentWorkingDirectory = string.IsNullOrWhiteSpace(cwd) ? Metadata.CurrentWorkingDirectory : cwd
            });
        }
        MetadataChanged?.Invoke(this, changed);
        active.Completion.TrySetResult(new TerminalSessionCommandResult(Metadata.SessionId, id, exitCode, false,
            Stopwatch.GetElapsedTime(active.StartedTimestamp), Metadata.CurrentWorkingDirectory, ProcessId is not null, Metadata.StateWasReset));
        return true;
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (sender is not Process process) return;
        ActiveCommand? active = null;
        TerminalSessionMetadata? changed = null;
        int? exitCode = null;
        try { exitCode = process.ExitCode; } catch { }
        lock (_sync)
        {
            if (!ReferenceEquals(_process, process)) return;
            _process = null;
            active = _active;
            changed = SetMetadataLocked(Metadata with { State = Volatile.Read(ref _disposed) != 0 ? TerminalSessionLifecycleState.Disposed : TerminalSessionLifecycleState.Ended });
        }
        MetadataChanged?.Invoke(this, changed);
        if (active is not null && !active.CancelRequested)
        {
            active.Completion.TrySetResult(new TerminalSessionCommandResult(Metadata.SessionId, active.Id, exitCode, false,
                Stopwatch.GetElapsedTime(active.StartedTimestamp), Metadata.CurrentWorkingDirectory, false, Metadata.StateWasReset));
        }
        DetachProcess(process);
    }

    private Process GetLiveProcessLocked()
    {
        if (_process is null || SafeHasExited(_process)) throw new InvalidOperationException("The Terminal shell session has ended. Start a new session to continue.");
        return _process;
    }

    private TerminalSessionMetadata SetMetadataLocked(TerminalSessionMetadata value)
    {
        Metadata = value;
        return value;
    }

    private void PublishOutput(Guid? commandId, TerminalOutputStream stream, string text)
    {
        OutputReceived?.Invoke(this, new TerminalSessionOutput(Metadata.SessionId, commandId, stream,
            SensitiveTextRedactor.Redact(text, 120_000), DateTimeOffset.UtcNow));
    }

    private void DetachProcess(Process process)
    {
        process.OutputDataReceived -= OnOutputDataReceived;
        process.ErrorDataReceived -= OnErrorDataReceived;
        process.Exited -= OnProcessExited;
    }

    private static bool SafeHasExited(Process process)
    {
        try { return process.HasExited; } catch { return true; }
    }

    private static void TryKillTree(Process process)
    {
        try { if (!SafeHasExited(process)) process.Kill(entireProcessTree: true); } catch { }
    }

    private sealed class ActiveCommand(Guid id, long startedTimestamp)
    {
        public Guid Id { get; } = id;
        public long StartedTimestamp { get; } = startedTimestamp;
        public TaskCompletionSource<TerminalSessionCommandResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CancelRequested { get; set; }
    }
}

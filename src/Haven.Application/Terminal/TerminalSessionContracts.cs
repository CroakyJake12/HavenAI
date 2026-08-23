using System;
using System.Threading;
using System.Threading.Tasks;

namespace Haven.Application;

public enum TerminalSessionLifecycleState
{
    Starting,
    Ready,
    Running,
    Interrupting,
    Ended,
    Faulted,
    Disposed
}

public enum TerminalOutputStream
{
    StandardOutput,
    StandardError,
    System
}

public sealed record TerminalSessionMetadata(
    Guid SessionId,
    string ShellRuntime,
    string DisplayName,
    string InitialWorkingDirectory,
    string? CurrentWorkingDirectory,
    TerminalSessionLifecycleState State,
    DateTimeOffset StartedAt,
    int Generation,
    bool StateWasReset);

public sealed record TerminalSessionOutput(
    Guid SessionId,
    Guid? CommandId,
    TerminalOutputStream Stream,
    string Text,
    DateTimeOffset Timestamp);

public sealed record TerminalSessionCommandResult(
    Guid SessionId,
    Guid CommandId,
    int? ExitCode,
    bool Cancelled,
    TimeSpan Duration,
    string? CurrentWorkingDirectory,
    bool ShellAlive,
    bool StateWasReset);

public interface ITerminalSession : IDisposable, IAsyncDisposable
{
    TerminalSessionMetadata Metadata { get; }
    int? ProcessId { get; }
    event EventHandler<TerminalSessionOutput>? OutputReceived;
    event EventHandler<TerminalSessionMetadata>? MetadataChanged;
    Task<TerminalSessionCommandResult> ExecuteAsync(Guid commandId, string command, CancellationToken cancellationToken);
    Task InterruptAsync(CancellationToken cancellationToken);
    Task SetWorkingDirectoryAsync(string path, CancellationToken cancellationToken);
}

public interface ITerminalSessionFactory
{
    ITerminalSession Create(string initialDirectory, string? displayName = null);
}

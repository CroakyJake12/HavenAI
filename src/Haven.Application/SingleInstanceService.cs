/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/SingleInstanceService.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns ISingleInstanceService, SingleInstanceService. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Diagnostics;
using System.IO.Pipes;

namespace Haven.Application;

/// <summary>
/// Defines the single instance service contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface ISingleInstanceService
{
    bool IsFirstInstance { get; }
    Task SendSignalAsync(string[] args);
    Task WaitForSignalAsync(Action<string[]> onSignal, CancellationToken cancellationToken);
}

/// <summary>
/// Represents single instance service and keeps its related state and behavior together.
/// </summary>
public sealed class SingleInstanceService : ISingleInstanceService, IDisposable
{
    /// <summary>
    /// Stores pipe name locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _pipeName;
    /// <summary>
    /// Stores mutex name locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _mutexName;
    /// <summary>
    /// Stores mutex locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private Mutex? _mutex;
    /// <summary>
    /// Stores cts locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private CancellationTokenSource? _cts;
    /// <summary>
    /// Stores is first instance locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isFirstInstance;

    public SingleInstanceService(IAppPaths paths)
    {
        var instanceId = Path.GetFileName(paths.DataDirectory).Replace("\\", "_").Replace("/", "_");
        _pipeName = $"Haven-{instanceId}";
        _mutexName = $"Global\\Haven-{instanceId}";
    }

    /// <summary>
    /// Reports whether first instance applies to the current state.
    /// </summary>
    public bool IsFirstInstance => _isFirstInstance;

    /// <summary>
    /// Attempts to acquire and reports the result without using failure for normal control flow.
    /// </summary>
    public bool TryAcquire()
    {
        try
        {
            _mutex = new Mutex(true, _mutexName, out var createdNew);
            _isFirstInstance = createdNew;
            return _isFirstInstance;
        }
        catch
        {
            _isFirstInstance = false;
            return false;
        }
    }

    /// <summary>
    /// Performs send signal asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task SendSignalAsync(string[] args)
    {
        try
        {
            await using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            await client.ConnectAsync(3000).ConfigureAwait(false);
            await using var writer = new StreamWriter(client);
            foreach (var arg in args)
                await writer.WriteLineAsync(arg).ConfigureAwait(false);
        }
        catch { }
    }

    /// <summary>
    /// Performs wait for signal asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task WaitForSignalAsync(Action<string[]> onSignal, CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In);
                await server.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);
                using var reader = new StreamReader(server);
                var lines = new List<string>();
                string? line;
                while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) is not null)
                    lines.Add(line);
                if (lines.Count > 0)
                    onSignal(lines.ToArray());
            }
            catch (OperationCanceledException) { break; }
            catch { break; }
        }
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _mutex?.Dispose();
    }
}

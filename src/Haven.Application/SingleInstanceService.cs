using System.Diagnostics;
using System.IO.Pipes;

namespace Haven.Application;

public interface ISingleInstanceService
{
    bool IsFirstInstance { get; }
    Task SendSignalAsync(string[] args);
    Task WaitForSignalAsync(Action<string[]> onSignal, CancellationToken cancellationToken);
}

public sealed class SingleInstanceService : ISingleInstanceService, IDisposable
{
    private readonly string _pipeName;
    private readonly string _mutexName;
    private Mutex? _mutex;
    private CancellationTokenSource? _cts;
    private bool _isFirstInstance;

    public SingleInstanceService(IAppPaths paths)
    {
        var instanceId = Path.GetFileName(paths.DataDirectory).Replace("\\", "_").Replace("/", "_");
        _pipeName = $"Haven-{instanceId}";
        _mutexName = $"Global\\Haven-{instanceId}";
    }

    public bool IsFirstInstance => _isFirstInstance;

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

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _mutex?.Dispose();
    }
}

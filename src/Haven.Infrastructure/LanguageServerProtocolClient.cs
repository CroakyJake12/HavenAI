using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class LanguageServerClientFactory : ILanguageServerClientFactory
{
    public async Task<ILanguageServerClient> StartAsync(
        LanguageServerDefinition definition,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        if (!definition.IsEnabled) throw new InvalidOperationException($"{definition.DisplayName} is disabled.");
        var root = Path.GetFullPath(workspaceRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);

        var startInfo = new ProcessStartInfo
        {
            FileName = definition.Command,
            Arguments = definition.Arguments,
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            if (!process.Start()) throw new InvalidOperationException($"Could not start {definition.DisplayName}.");
            var client = new StdioLanguageServerClient(definition, root, process);
            await client.InitializeAsync(cancellationToken).ConfigureAwait(false);
            return client;
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }
}

public sealed class LanguageServerRequestException(int code, string message, JsonElement? data = null) : Exception(message)
{
    public int Code { get; } = code;
    public JsonElement? Data { get; } = data;
}

internal sealed class StdioLanguageServerClient : ILanguageServerClient
{
    private readonly LanguageServerDefinition _definition;
    private readonly string _workspaceRoot;
    private readonly Process _process;
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<CodeDiagnostic>> _publishedDiagnostics = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IReadOnlyList<CodeDiagnostic>>> _diagnosticWaiters = new(StringComparer.OrdinalIgnoreCase);
    private readonly StringBuilder _stderr = new();
    private readonly Task _readerTask;
    private readonly Task _stderrTask;
    private long _requestId;
    private int _shutdown;

    public StdioLanguageServerClient(LanguageServerDefinition definition, string workspaceRoot, Process process)
    {
        _definition = definition;
        _workspaceRoot = workspaceRoot;
        _process = process;
        _input = process.StandardOutput.BaseStream;
        _output = process.StandardInput.BaseStream;
        _readerTask = Task.Run(() => ReadLoopAsync(_lifetime.Token));
        _stderrTask = Task.Run(() => ReadStderrAsync(_lifetime.Token));
        process.Exited += (_, _) => FailPending(new InvalidOperationException(BuildExitMessage()));
    }

    public string ServerId => _definition.Id;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        JsonElement initializationOptions;
        using (var document = JsonDocument.Parse(_definition.InitializationOptionsJson))
            initializationOptions = document.RootElement.Clone();
        var rootUri = ToDocumentUri(_workspaceRoot);
        var response = await RequestAsync("initialize", new
        {
            processId = Environment.ProcessId,
            clientInfo = new { name = "Haven", version = "1" },
            locale = CultureInfo.CurrentUICulture.Name,
            rootUri,
            workspaceFolders = new[] { new { uri = rootUri, name = Path.GetFileName(_workspaceRoot) } },
            capabilities = new
            {
                workspace = new
                {
                    workspaceFolders = true,
                    symbol = new { dynamicRegistration = false }
                },
                textDocument = new
                {
                    synchronization = new { dynamicRegistration = false, didSave = true },
                    publishDiagnostics = new { relatedInformation = true, versionSupport = true, codeDescriptionSupport = true, dataSupport = true },
                    diagnostic = new { dynamicRegistration = false, relatedDocumentSupport = true },
                    formatting = new { dynamicRegistration = false }
                },
                general = new { positionEncodings = new[] { "utf-16" } }
            },
            initializationOptions,
            trace = "off"
        }, cancellationToken).ConfigureAwait(false);
        if (response.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null))
            throw new InvalidOperationException($"{_definition.DisplayName} returned an invalid initialize response.");
        await NotifyAsync("initialized", new { }, cancellationToken).ConfigureAwait(false);
    }

    public async Task OpenDocumentAsync(
        string documentUri,
        string languageId,
        string text,
        int version,
        CancellationToken cancellationToken)
    {
        await NotifyAsync("textDocument/didOpen", new
        {
            textDocument = new { uri = documentUri, languageId, version, text }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CodeDiagnostic>> GetDiagnosticsAsync(
        string documentUri,
        string workspaceRoot,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await RequestAsync("textDocument/diagnostic", new
            {
                textDocument = new { uri = documentUri }
            }, cancellationToken, timeout).ConfigureAwait(false);
            if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                return ParseDiagnostics(items, documentUri, workspaceRoot);
        }
        catch (LanguageServerRequestException exception) when (exception.Code is -32601 or -32803 or -32001)
        {
            // Push-only servers report textDocument/publishDiagnostics instead.
        }

        if (_publishedDiagnostics.TryGetValue(documentUri, out var existing)) return existing;
        var waiter = _diagnosticWaiters.GetOrAdd(documentUri, static _ => new TaskCompletionSource<IReadOnlyList<CodeDiagnostic>>(TaskCreationOptions.RunContinuationsAsynchronously));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        linked.CancelAfter(timeout);
        try
        {
            return await waiter.Task.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !_lifetime.IsCancellationRequested)
        {
            return _publishedDiagnostics.TryGetValue(documentUri, out existing) ? existing : [];
        }
        finally
        {
            _diagnosticWaiters.TryRemove(documentUri, out _);
        }
    }

    public async Task<IReadOnlyList<CodeSymbol>> SearchWorkspaceSymbolsAsync(
        string query,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var result = await RequestAsync("workspace/symbol", new { query }, cancellationToken).ConfigureAwait(false);
        if (result.ValueKind != JsonValueKind.Array) return [];
        var symbols = new List<CodeSymbol>();
        foreach (var item in result.EnumerateArray())
        {
            if (!item.TryGetProperty("name", out var nameElement) || nameElement.GetString() is not { Length: > 0 } name) continue;
            var kind = item.TryGetProperty("kind", out var kindElement) && kindElement.TryGetInt32(out var kindValue)
                ? SymbolKindName(kindValue)
                : "Symbol";
            var container = item.TryGetProperty("containerName", out var containerElement) ? containerElement.GetString() : null;
            if (!TryReadLocation(item, out var uri, out var range)) continue;
            var relative = RelativePathFromUri(uri, workspaceRoot);
            if (relative is null) continue;
            symbols.Add(new CodeSymbol(name, kind, relative, range, container, _definition.DisplayName));
            if (symbols.Count >= 500) break;
        }
        return symbols;
    }

    public async Task<IReadOnlyList<LanguageServerTextEdit>> FormatDocumentAsync(
        string documentUri,
        int tabSize,
        bool insertSpaces,
        CancellationToken cancellationToken)
    {
        var result = await RequestAsync("textDocument/formatting", new
        {
            textDocument = new { uri = documentUri },
            options = new { tabSize = Math.Clamp(tabSize, 1, 16), insertSpaces, trimTrailingWhitespace = true, insertFinalNewline = true, trimFinalNewlines = true }
        }, cancellationToken).ConfigureAwait(false);
        if (result.ValueKind == JsonValueKind.Null) return [];
        if (result.ValueKind != JsonValueKind.Array) throw new InvalidOperationException($"{_definition.DisplayName} returned invalid formatting edits.");
        var edits = new List<LanguageServerTextEdit>();
        foreach (var item in result.EnumerateArray())
        {
            if (!item.TryGetProperty("range", out var rangeElement) || !TryReadRange(rangeElement, out var range)) continue;
            if (!item.TryGetProperty("newText", out var textElement)) continue;
            edits.Add(new LanguageServerTextEdit(range, textElement.GetString() ?? string.Empty));
        }
        return edits;
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _shutdown, 1) != 0) return;
        try
        {
            if (!_process.HasExited)
            {
                try { _ = await RequestAsync("shutdown", null, cancellationToken, TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
                catch (Exception exception) when (exception is IOException or InvalidOperationException or LanguageServerRequestException or OperationCanceledException) { }
                try { await NotifyAsync("exit", null, cancellationToken).ConfigureAwait(false); }
                catch (Exception exception) when (exception is IOException or InvalidOperationException or OperationCanceledException) { }
                try { await _process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false); }
                catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
                {
                    TryKill();
                }
            }
        }
        finally
        {
            _lifetime.Cancel();
            FailPending(new OperationCanceledException("The language-server session closed."));
        }
    }

    public async ValueTask DisposeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await ShutdownAsync(timeout.Token).ConfigureAwait(false);
        try { await Task.WhenAll(_readerTask, _stderrTask).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException) { }
        _input.Dispose();
        _output.Dispose();
        _process.Dispose();
        _writeGate.Dispose();
        _lifetime.Dispose();
    }

    private async Task<JsonElement> RequestAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken,
        TimeSpan? explicitTimeout = null)
    {
        if (_process.HasExited) throw new InvalidOperationException(BuildExitMessage());
        var id = Interlocked.Increment(ref _requestId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion)) throw new InvalidOperationException("Could not register the language-server request.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        linked.CancelAfter(explicitTimeout ?? TimeSpan.FromSeconds(_definition.RequestTimeoutSeconds));
        using var registration = linked.Token.Register(() =>
        {
            completion.TrySetCanceled(linked.Token);
            _ = NotifyCancellationAsync(id);
        });
        try
        {
            await WriteMessageAsync(new { jsonrpc = "2.0", id, method, @params = parameters }, linked.Token).ConfigureAwait(false);
            return await completion.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !_lifetime.IsCancellationRequested)
        {
            throw new TimeoutException($"{_definition.DisplayName} did not answer '{method}' within the configured timeout.");
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private Task NotifyAsync(string method, object? parameters, CancellationToken cancellationToken) =>
        WriteMessageAsync(new { jsonrpc = "2.0", method, @params = parameters }, cancellationToken);

    private async Task NotifyCancellationAsync(long id)
    {
        try { await NotifyAsync("$/cancelRequest", new { id }, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ObjectDisposedException) { }
    }

    private async Task WriteMessageAsync(object payload, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(payload, ProviderHttp.Json);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _output.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await _output.WriteAsync(body, cancellationToken).ConfigureAwait(false);
            await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await LanguageServerProtocolCodec.ReadMessageAsync(_input, cancellationToken).ConfigureAwait(false);
                if (message is null) break;
                Dispatch(message.Value);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
        {
            FailPending(exception);
        }
    }

    private async Task ReadStderrAsync(CancellationToken cancellationToken)
    {
        try
        {
            var buffer = new char[2_048];
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await _process.StandardError.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                lock (_stderr)
                {
                    var remaining = 32_000 - _stderr.Length;
                    if (remaining > 0) _stderr.Append(buffer, 0, Math.Min(read, remaining));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (IOException) { }
    }

    private void Dispatch(JsonElement message)
    {
        if (message.TryGetProperty("id", out var idElement) && idElement.ValueKind is JsonValueKind.Number or JsonValueKind.String)
        {
            if (!TryReadRequestId(idElement, out var id) || !_pending.TryGetValue(id, out var completion)) return;
            if (message.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                var code = error.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var value) ? value : -32000;
                var text = error.TryGetProperty("message", out var messageElement) ? messageElement.GetString() ?? "Language-server request failed." : "Language-server request failed.";
                completion.TrySetException(new LanguageServerRequestException(code, text, error.TryGetProperty("data", out var data) ? data.Clone() : null));
            }
            else
            {
                completion.TrySetResult(message.TryGetProperty("result", out var result) ? result.Clone() : JsonSerializer.SerializeToElement<object?>(null));
            }
            return;
        }

        if (!message.TryGetProperty("method", out var methodElement) || methodElement.GetString() is not { } method) return;
        if (method == "textDocument/publishDiagnostics" && message.TryGetProperty("params", out var parameters))
            HandlePublishedDiagnostics(parameters);
    }

    private void HandlePublishedDiagnostics(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("uri", out var uriElement) || uriElement.GetString() is not { Length: > 0 } uri) return;
        var diagnostics = parameters.TryGetProperty("diagnostics", out var values) && values.ValueKind == JsonValueKind.Array
            ? ParseDiagnostics(values, uri, _workspaceRoot)
            : [];
        _publishedDiagnostics[uri] = diagnostics;
        if (_diagnosticWaiters.TryGetValue(uri, out var waiter)) waiter.TrySetResult(diagnostics);
    }

    private void FailPending(Exception exception)
    {
        foreach (var pair in _pending) pair.Value.TrySetException(exception);
        foreach (var pair in _diagnosticWaiters) pair.Value.TrySetException(exception);
    }

    private string BuildExitMessage()
    {
        string error;
        lock (_stderr) error = _stderr.ToString().Trim();
        var suffix = error.Length == 0 ? string.Empty : " " + error;
        return $"{_definition.DisplayName} exited unexpectedly with code {(_process.HasExited ? _process.ExitCode : -1)}.{suffix}";
    }

    private void TryKill()
    {
        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
    }

    private static bool TryReadRequestId(JsonElement element, out long id)
    {
        if (element.ValueKind == JsonValueKind.Number) return element.TryGetInt64(out id);
        return long.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
    }

    private static IReadOnlyList<CodeDiagnostic> ParseDiagnostics(JsonElement values, string documentUri, string workspaceRoot)
    {
        var relative = RelativePathFromUri(documentUri, workspaceRoot) ?? documentUri;
        var result = new List<CodeDiagnostic>();
        foreach (var item in values.EnumerateArray())
        {
            if (!item.TryGetProperty("range", out var rangeElement) || !TryReadRange(rangeElement, out var range)) continue;
            var severity = item.TryGetProperty("severity", out var severityElement) && severityElement.TryGetInt32(out var severityValue) && Enum.IsDefined(typeof(CodeDiagnosticSeverity), severityValue)
                ? (CodeDiagnosticSeverity)severityValue
                : CodeDiagnosticSeverity.Information;
            var code = item.TryGetProperty("code", out var codeElement) ? codeElement.ToString() : null;
            var source = item.TryGetProperty("source", out var sourceElement) ? sourceElement.GetString() : null;
            var message = item.TryGetProperty("message", out var messageElement) ? messageElement.GetString() ?? "Diagnostic" : "Diagnostic";
            result.Add(new CodeDiagnostic(relative, range, severity, code, source, message));
        }
        return result;
    }

    private static bool TryReadLocation(JsonElement item, out string uri, out CodeRange range)
    {
        uri = string.Empty;
        range = default!;
        if (!item.TryGetProperty("location", out var location) || location.ValueKind != JsonValueKind.Object) return false;
        if (location.TryGetProperty("uri", out var uriElement))
        {
            uri = uriElement.GetString() ?? string.Empty;
            return location.TryGetProperty("range", out var rangeElement) && TryReadRange(rangeElement, out range);
        }
        return false;
    }

    internal static bool TryReadRange(JsonElement element, out CodeRange range)
    {
        range = default!;
        if (!element.TryGetProperty("start", out var start) || !element.TryGetProperty("end", out var end)) return false;
        if (!TryReadPosition(start, out var startPosition) || !TryReadPosition(end, out var endPosition)) return false;
        range = new CodeRange(startPosition, endPosition);
        return true;
    }

    private static bool TryReadPosition(JsonElement element, out CodePosition position)
    {
        position = default!;
        if (!element.TryGetProperty("line", out var lineElement) || !lineElement.TryGetInt32(out var line) || line < 0) return false;
        if (!element.TryGetProperty("character", out var characterElement) || !characterElement.TryGetInt32(out var character) || character < 0) return false;
        position = new CodePosition(line, character);
        return true;
    }

    internal static string ToDocumentUri(string path) => new Uri(Path.GetFullPath(path)).AbsoluteUri;

    internal static string? RelativePathFromUri(string uri, string workspaceRoot)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || !parsed.IsFile) return null;
        var path = Path.GetFullPath(parsed.LocalPath);
        var root = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.Equals(root, comparison) && !path.StartsWith(root + Path.DirectorySeparatorChar, comparison)) return null;
        return Path.GetRelativePath(root, path);
    }

    private static string SymbolKindName(int value) => value switch
    {
        2 => "Module", 3 => "Namespace", 4 => "Package", 5 => "Class", 6 => "Method", 7 => "Property", 8 => "Field",
        9 => "Constructor", 10 => "Enum", 11 => "Interface", 12 => "Function", 13 => "Variable", 14 => "Constant",
        15 => "String", 16 => "Number", 17 => "Boolean", 18 => "Array", 19 => "Object", 20 => "Key", 21 => "Null",
        22 => "Enum member", 23 => "Struct", 24 => "Event", 25 => "Operator", 26 => "Type parameter", _ => "Symbol"
    };
}

internal static class LanguageServerProtocolCodec
{
    public static async Task<JsonElement?> ReadMessageAsync(Stream stream, CancellationToken cancellationToken)
    {
        int? contentLength = null;
        while (true)
        {
            var line = await ReadAsciiLineAsync(stream, cancellationToken).ConfigureAwait(false);
            if (line is null) return null;
            if (line.Length == 0) break;
            var separator = line.IndexOf(':');
            if (separator <= 0) continue;
            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var length)
                && length >= 0)
                contentLength = length;
        }
        if (contentLength is null) throw new InvalidDataException("The language-server message did not contain Content-Length.");
        if (contentLength > 16 * 1024 * 1024) throw new InvalidDataException("The language-server message exceeded Haven's 16 MB safety limit.");
        var body = new byte[contentLength.Value];
        var offset = 0;
        while (offset < body.Length)
        {
            var read = await stream.ReadAsync(body.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException("The language-server message ended before its declared content length.");
            offset += read;
        }
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    public static async Task WriteMessageAsync(Stream stream, JsonElement message, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(message);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> ReadAsciiLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>(128);
        var single = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(single, cancellationToken).ConfigureAwait(false);
            if (read == 0) return bytes.Count == 0 ? null : Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r');
            if (single[0] == (byte)'\n') return Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r');
            bytes.Add(single[0]);
            if (bytes.Count > 8_192) throw new InvalidDataException("The language-server header line exceeded Haven's safety limit.");
        }
    }
}

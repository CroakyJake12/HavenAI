/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/WhisperModelManager.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns WhisperModelManager, ModelDefinition. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Downloads the multilingual whisper.cpp model files consumed by Whisper.net.
/// Downloads are cancellable and atomically promoted so a partial file is never
/// reported as installed.
/// </summary>
public sealed class WhisperModelManager(HttpClient httpClient, IAppPaths paths) : ISpeechModelManager
{
    /// <summary>
    /// Stores definitions locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly IReadOnlyDictionary<SpeechModelSize, ModelDefinition> Definitions =
        new Dictionary<SpeechModelSize, ModelDefinition>
        {
            [SpeechModelSize.Tiny] = new(
                "Tiny · fastest", "ggml-tiny.bin", 75_000_000,
                "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin"),
            [SpeechModelSize.Base] = new(
                "Base · recommended", "ggml-base.bin", 142_000_000,
                "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin"),
            [SpeechModelSize.Small] = new(
                "Small · more accurate", "ggml-small.bin", 466_000_000,
                "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin")
        };

    /// <summary>
    /// Stores models directory locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _modelsDirectory = Path.Combine(paths.DataDirectory, "SpeechModels");
    /// <summary>
    /// Stores download gate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly SemaphoreSlim _downloadGate = new(1, 1);

    /// <summary>
    /// Retrieves models async for the current operation.
    /// </summary>
    public Task<IReadOnlyList<SpeechModelInfo>> GetModelsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_modelsDirectory);
        IReadOnlyList<SpeechModelInfo> result = Definitions
            .Select(pair => CreateInfo(pair.Key, pair.Value))
            .ToArray();
        return Task.FromResult(result);
    }

    /// <summary>
    /// Performs download async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<SpeechModelInfo> DownloadAsync(
        SpeechModelSize size,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (!Definitions.TryGetValue(size, out var definition))
            throw new ArgumentOutOfRangeException(nameof(size));

        await _downloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_modelsDirectory);
            var destination = Path.Combine(_modelsDirectory, definition.FileName);
            if (File.Exists(destination)) return CreateInfo(size, definition);

            var partial = destination + ".download";
            try
            {
                if (File.Exists(partial)) File.Delete(partial);
                using var response = await httpClient.GetAsync(
                    definition.DownloadUri,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? definition.ApproximateSizeBytes;
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var target = new FileStream(
                    partial,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var buffer = new byte[128 * 1024];
                long written = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    written += read;
                    progress?.Report(total > 0 ? Math.Clamp((double)written / total, 0, 1) : 0);
                }
                await target.FlushAsync(cancellationToken).ConfigureAwait(false);
                target.Close();
                File.Move(partial, destination, overwrite: true);
                progress?.Report(1);
                return CreateInfo(size, definition);
            }
            catch
            {
                try { if (File.Exists(partial)) File.Delete(partial); }
                catch (IOException) { /* a later run will clean the stale partial */ }
                throw;
            }
        }
        finally
        {
            _downloadGate.Release();
        }
    }

    /// <summary>
    /// Performs delete async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task DeleteAsync(SpeechModelSize size, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Definitions.TryGetValue(size, out var definition))
            throw new ArgumentOutOfRangeException(nameof(size));
        var path = Path.Combine(_modelsDirectory, definition.FileName);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Creates info with the invariants required by its callers.
    /// </summary>
    private SpeechModelInfo CreateInfo(SpeechModelSize size, ModelDefinition definition)
    {
        var localPath = Path.Combine(_modelsDirectory, definition.FileName);
        return new SpeechModelInfo(
            size,
            definition.DisplayName,
            definition.FileName,
            definition.ApproximateSizeBytes,
            File.Exists(localPath),
            localPath);
    }

    /// <summary>
    /// Represents model definition and keeps its related state and behavior together.
    /// </summary>
    private sealed record ModelDefinition(
        string DisplayName,
        string FileName,
        long ApproximateSizeBytes,
        string DownloadUri);
}

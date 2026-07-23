using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Haven.Desktop;

/// <summary>
/// Utility for parallel processing operations that offload work from the UI thread.
/// Uses the configured thread pool (up to 96 cores) for CPU-bound operations.
/// </summary>
public static class ParallelHelper
{
    /// <summary>
    /// Processes items in parallel with bounded concurrency, returning results in order.
    /// Offloads CPU-bound work to the thread pool.
    /// </summary>
    public static async Task<IReadOnlyList<TResult>> WhenAll<TSource, TResult>(
        IReadOnlyList<TSource> sources,
        Func<TSource, Task<TResult>> body,
        int? maxConcurrency = null)
    {
        var concurrency = maxConcurrency ?? Environment.ProcessorCount;
        var results = new ConcurrentBag<(int Index, TResult Value)>();
        var semaphore = new System.Threading.SemaphoreSlim(concurrency);

        var tasks = sources.Select(async (source, index) =>
        {
            await semaphore.WaitAsync();
            try
            {
                var result = await body(source);
                results.Add((index, result));
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        return results
            .OrderBy(r => r.Index)
            .Select(r => r.Value)
            .ToArray();
    }

    /// <summary>
    /// Processes items in parallel with bounded concurrency (void return).
    /// Offloads CPU-bound work to the thread pool.
    /// </summary>
    public static async Task ForEach<TSource>(
        IEnumerable<TSource> sources,
        Func<TSource, Task> body,
        int? maxConcurrency = null)
    {
        var concurrency = maxConcurrency ?? Environment.ProcessorCount;
        var semaphore = new System.Threading.SemaphoreSlim(concurrency);

        var tasks = sources.Select(async source =>
        {
            await semaphore.WaitAsync();
            try
            {
                await body(source);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Runs a CPU-bound action on a background thread, then marshals the result back to the UI thread.
    /// </summary>
    public static async Task<T> RunOnBackground<T>(Func<T> func)
    {
        return await Task.Run(func);
    }

    /// <summary>
    /// Runs a CPU-bound action on a background thread.
    /// </summary>
    public static async Task RunOnBackground(Action action)
    {
        await Task.Run(action);
    }

    /// <summary>
    /// Batches items into groups for parallel processing.
    /// Useful for processing large collections in chunks.
    /// </summary>
    public static IEnumerable<IReadOnlyList<TSource>> Batch<TSource>(
        this IEnumerable<TSource> source, int batchSize)
    {
        var batch = new List<TSource>(batchSize);
        foreach (var item in source)
        {
            batch.Add(item);
            if (batch.Count >= batchSize)
            {
                yield return batch;
                batch = new List<TSource>(batchSize);
            }
        }
        if (batch.Count > 0)
            yield return batch;
    }

    /// <summary>
    /// Processes items in parallel batches.
    /// </summary>
    public static async Task BatchProcess<TSource>(
        IEnumerable<TSource> sources,
        Func<IReadOnlyList<TSource>, Task> batchProcessor,
        int batchSize = 100,
        int? maxConcurrency = null)
    {
        var batches = sources.Batch(batchSize);
        await ForEach(batches, batchProcessor, maxConcurrency);
    }
}

/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/SentenceChunker.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns SentenceChunker. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text;

namespace Haven.Application;

/// <summary>
/// Turns arbitrary streamed model deltas into natural speech-sized chunks while
/// retaining incomplete text for the next delta.
/// </summary>
public sealed class SentenceChunker
{
    /// <summary>
    /// Stores pending locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly StringBuilder _pending = new();
    /// <summary>
    /// Stores soft limit locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly int _softLimit;

    public SentenceChunker(int softLimit = 220)
    {
        if (softLimit < 32) throw new ArgumentOutOfRangeException(nameof(softLimit));
        _softLimit = softLimit;
    }

    /// <summary>
    /// Performs the append step owned by this component.
    /// </summary>
    public IReadOnlyList<string> Append(string delta)
    {
        if (string.IsNullOrEmpty(delta)) return [];
        _pending.Append(delta);
        var chunks = new List<string>();

        while (TryFindBoundary(_pending, _softLimit, out var boundary))
        {
            var chunk = _pending.ToString(0, boundary).Trim();
            _pending.Remove(0, boundary);
            if (chunk.Length > 0) chunks.Add(chunk);
        }

        return chunks;
    }

    /// <summary>
    /// Performs the flush step owned by this component.
    /// </summary>
    public string Flush()
    {
        var remainder = _pending.ToString().Trim();
        _pending.Clear();
        return remainder;
    }

    /// <summary>
    /// Attempts to find boundary and reports the result without using failure for normal control flow.
    /// </summary>
    private static bool TryFindBoundary(StringBuilder value, int softLimit, out int boundary)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            var nextIsBreak = index == value.Length - 1 || char.IsWhiteSpace(value[index + 1]);
            if (nextIsBreak && current is '.' or '!' or '?' or '\n')
            {
                boundary = index + 1;
                return true;
            }
        }

        if (value.Length >= softLimit)
        {
            for (var index = Math.Min(value.Length - 1, softLimit); index >= softLimit / 2; index--)
            {
                if (!char.IsWhiteSpace(value[index])) continue;
                boundary = index + 1;
                return true;
            }
        }

        boundary = 0;
        return false;
    }
}

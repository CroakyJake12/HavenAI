using System.Text;

namespace Haven.Application;

/// <summary>
/// Turns arbitrary streamed model deltas into natural speech-sized chunks while
/// retaining incomplete text for the next delta.
/// </summary>
public sealed class SentenceChunker
{
    private readonly StringBuilder _pending = new();
    private readonly int _softLimit;

    public SentenceChunker(int softLimit = 220)
    {
        if (softLimit < 32) throw new ArgumentOutOfRangeException(nameof(softLimit));
        _softLimit = softLimit;
    }

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

    public string Flush()
    {
        var remainder = _pending.ToString().Trim();
        _pending.Clear();
        return remainder;
    }

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

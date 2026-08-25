/*
 * FILE DOCUMENTATION
 * Where: src/Haven.OldHaven/Services/NotesReadAloudController.cs, in the Desktop services layer, adapting application behavior to Windows and Avalonia concerns.
 * What: This file owns NotesReadAloudStatus and NotesReadAloudController: a local speech reader that either speaks one selected
 *       passage (ReadAsync) or reads a long document continuously from a sentence-boundary chunk queue (SpeakLongFormAsync)
 *       with skip/pause/resume/stop controls and honest progress reporting.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 *      Long-form playback runs one owner loop per session; Skip/Pause interrupt only the current chunk by cancelling its linked
 *      token while the loop re-reads shared position state under _gate, so no utterance is awaited while the gate is held.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Services;

/// <summary>
/// Represents notes read aloud status and keeps its related state and behavior together.
/// </summary>
public sealed record NotesReadAloudStatus(
    bool IsActive,
    string Message,
    bool IsError = false);

/// <summary>
/// Reads selected text through the same local Windows speech-output singleton used by
/// Call. It never sends document content to a model or network.
/// </summary>
/// <remarks>
/// Speaking-rate control does not exist here: <see cref="ISpeechOutputService"/> has no rate
/// parameter, so none is exposed and none is simulated. Voice choice is a real pass-through of
/// the service's voiceName argument via <see cref="SetPreferredVoice(string?)"/>. Pause is
/// implemented honestly as stop-with-position-retained plus resume-by-re-speaking the current
/// chunk, because the TTS engine has no native pause.
/// </remarks>
public sealed class NotesReadAloudController(
    ISpeechOutputService speech,
    ICallCoordinator calls,
    IProductionDiagnostics diagnostics) : IAsyncDisposable
{
    /// <summary>
    /// Stores gate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);
    /// <summary>
    /// Stores active cancellation locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private CancellationTokenSource? _activeCancellation;
    /// <summary>
    /// Stores active locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _active;
    /// <summary>
    /// Marks whether a chunked long-form session currently owns playback.
    /// </summary>
    private int _reading;
    /// <summary>
    /// Marks whether the long-form session is paused with its position retained.
    /// </summary>
    private int _paused;
    /// <summary>
    /// Requests that the owner loop replay the current chunk instead of advancing past it.
    /// </summary>
    private bool _replayCurrentChunk;
    /// <summary>
    /// Queue of chunks for the active long-form session.
    /// </summary>
    private IReadOnlyList<string> _chunks = [];
    /// <summary>
    /// Index of the chunk currently being spoken (or next when paused).
    /// </summary>
    private int _chunkIndex;
    /// <summary>
    /// Voice resolved once per long-form session.
    /// </summary>
    private CallVoice? _sessionVoice;
    /// <summary>
    /// Output device resolved once per long-form session.
    /// </summary>
    private string? _sessionOutputDeviceId;
    /// <summary>
    /// Signals a paused owner loop to resume; null whenever nobody is waiting.
    /// </summary>
    private TaskCompletionSource? _resumeSignal;
    /// <summary>
    /// Cancellation for the utterance currently in flight inside the owner loop.
    /// </summary>
    private CancellationTokenSource? _chunkCancellation;
    /// <summary>
    /// Preferred speech voice name or identifier passed through to the speech service.
    /// </summary>
    private string? _preferredVoice;
    /// <summary>
    /// Stores disposed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Reports whether active applies to the current state.
    /// </summary>
    public bool IsActive => Volatile.Read(ref _active) == 1;
    /// <summary>
    /// Reports whether a chunked long-form reading session is active (possibly paused).
    /// </summary>
    public bool IsReading => Volatile.Read(ref _reading) == 1;
    /// <summary>
    /// Reports whether the long-form session is paused with its position retained.
    /// </summary>
    public bool IsPaused => Volatile.Read(ref _paused) == 1;
    /// <summary>
    /// Gets the zero-based index of the chunk being spoken (or queued next while paused).
    /// </summary>
    public int CurrentChunkIndex => Volatile.Read(ref _chunkIndex);
    /// <summary>
    /// Gets the number of chunks in the active long-form session; zero while idle.
    /// </summary>
    public int ChunkCount => _chunks.Count;
    /// <summary>
    /// Stores status changed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event EventHandler<NotesReadAloudStatus>? StatusChanged;
    /// <summary>
    /// Reports long-form completion progress as a fraction from 0 to 1.
    /// </summary>
    public event Action<double>? ProgressChanged;
    /// <summary>
    /// Reports when a chunked long-form reading session starts or stops.
    /// </summary>
    public event Action<bool>? IsReadingChanged;

    /// <summary>
    /// Splits text into readable chunks near the requested length, preferring sentence boundaries
    /// and falling back to word boundaries for oversize sentences. Whitespace is normalised so
    /// every chunk holds trimmed single-spaced text; joining the chunks with one space reproduces
    /// the normalised input exactly.
    /// </summary>
    public static IReadOnlyList<string> SplitIntoChunks(string text, int maximumChunkLength = 600)
    {
        if (maximumChunkLength < 1)
            throw new ArgumentOutOfRangeException(nameof(maximumChunkLength), maximumChunkLength, "The maximum chunk length must be positive.");
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var normalized = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var chunks = new List<string>();
        var current = new StringBuilder();
        foreach (var sentence in EnumerateSentences(normalized))
        {
            if (sentence.Length > maximumChunkLength)
            {
                if (current.Length > 0)
                {
                    chunks.Add(current.ToString());
                    current.Clear();
                }

                chunks.AddRange(SplitOversizeSentence(sentence, maximumChunkLength));
                continue;
            }

            if (current.Length == 0)
            {
                current.Append(sentence);
                continue;
            }

            if (current.Length + 1 + sentence.Length <= maximumChunkLength)
                current.Append(' ').Append(sentence);
            else
            {
                chunks.Add(current.ToString());
                current.Clear();
                current.Append(sentence);
            }
        }

        if (current.Length > 0)
            chunks.Add(current.ToString());
        return chunks;
    }

    /// <summary>
    /// Yields sentence-sized pieces that end in sentence punctuation followed by whitespace or the
    /// end of the text. Abbreviation-aware splitting is deliberately out of scope; an occasional
    /// early break is harmless for speech.
    /// </summary>
    private static IEnumerable<string> EnumerateSentences(string text)
    {
        var start = 0;
        var index = 0;
        while (index < text.Length)
        {
            if (text[index] is '.' or '!' or '?' or ';' or ':' or '…')
            {
                var end = index + 1;
                while (end < text.Length && IsSentenceClosing(text[end])) end++;
                if (end >= text.Length || char.IsWhiteSpace(text[end]))
                {
                    var sentence = text[start..end].Trim();
                    if (sentence.Length > 0) yield return sentence;
                    index = start = end;
                    continue;
                }
            }

            index++;
        }

        if (start < text.Length)
        {
            var tail = text[start..].Trim();
            if (tail.Length > 0) yield return tail;
        }
    }

    /// <summary>
    /// Determines whether the given character may close a sentence before its boundary.
    /// </summary>
    private static bool IsSentenceClosing(char value) =>
        value is '"' or '\'' or ')' or ']' or '}' or '»' or '\u2019' or '\u201D';

    /// <summary>
    /// Hard-splits one oversize sentence at word boundaries so no chunk exceeds the limit unless a
    /// single word alone is longer than the limit.
    /// </summary>
    private static IEnumerable<string> SplitOversizeSentence(string sentence, int maximumChunkLength)
    {
        var current = new StringBuilder();
        foreach (var word in sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.Length == 0)
            {
                current.Append(word);
                continue;
            }

            if (current.Length + 1 + word.Length <= maximumChunkLength)
                current.Append(' ').Append(word);
            else
            {
                yield return current.ToString();
                current.Clear();
                current.Append(word);
            }
        }

        if (current.Length > 0)
            yield return current.ToString();
    }

    /// <summary>
    /// Remembers the preferred speech voice by name or identifier. The value is passed straight
    /// through to <see cref="ISpeechOutputService.SpeakAsync"/> as its voiceName argument whenever
    /// a voice with that id or name is available; unknown values fall back to the normal
    /// language-based selection. Pass null to clear the preference.
    /// </summary>
    public void SetPreferredVoice(string? voiceName)
    {
        _preferredVoice = string.IsNullOrWhiteSpace(voiceName) ? null : voiceName.Trim();
    }

    /// <summary>
    /// Performs read asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task ReadAsync(
        string text,
        string? language,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("The selected Notes block has no readable text.", nameof(text));

        CancellationTokenSource linked;
        CallVoice? voice;
        string? outputDeviceId;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (RuntimeSafetyState.IsSafeMode)
                throw new InvalidOperationException("Notes read aloud is disabled while Haven recovery safe mode is active.");
            if (calls.IsActive)
                throw new InvalidOperationException("End the active Haven Call before starting Notes read aloud.");
            if (!speech.IsAvailable)
                throw new InvalidOperationException(speech.UnavailableReason ?? "Local speech output is unavailable.");
            if (IsActive)
                throw new InvalidOperationException("Notes read aloud is already active. Stop it before starting another passage.");

            voice = ResolveVoiceLocked(speech.Voices, language);
            var output = speech.Devices.FirstOrDefault(value => value.IsDefault)
                         ?? speech.Devices.FirstOrDefault();
            outputDeviceId = output?.Id;
            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeCancellation = linked;
            Volatile.Write(ref _active, 1);
            RaiseStatus("Reading the selected block aloud locally…");
        }
        finally
        {
            _gate.Release();
        }

        try
        {
            await speech.SpeakAsync(
                text.Trim(),
                voice?.Id,
                outputDeviceId,
                linked.Token).ConfigureAwait(false);
            if (!linked.IsCancellationRequested)
            {
                await diagnostics.WriteAsync(
                    ReliabilitySeverity.Information,
                    "notes",
                    "read-aloud-completed",
                    "A selected Notes block was read aloud through local speech synthesis.",
                    new Dictionary<string, string>
                    {
                        ["characters"] = text.Trim().Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["voice"] = voice?.Name ?? "system default",
                        ["networkContentSent"] = bool.FalseString
                    },
                    cancellationToken: CancellationToken.None).ConfigureAwait(false);
                RaiseStatus("Read aloud completed locally.");
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            // StopAsync or the caller intentionally interrupted this local playback.
        }
        finally
        {
            await CompleteReadAsync(linked).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Splits the text at sentence boundaries and reads it continuously chunk by chunk.
    /// </summary>
    public Task SpeakLongFormAsync(string text, string? language, CancellationToken cancellationToken) =>
        SpeakLongFormAsync(SplitIntoChunks(text), language, cancellationToken);

    /// <summary>
    /// Reads the supplied chunks continuously in order through local speech output. Skips move
    /// within the queue, pause retains the position, resume re-speaks the current chunk from its
    /// beginning, and stop cancels the whole session. All state changes raise honest status,
    /// progress (0..1) and reading events.
    /// </summary>
    public async Task SpeakLongFormAsync(
        IReadOnlyList<string> chunks,
        string? language,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(chunks);
        var prepared = chunks
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToList();
        if (prepared.Count == 0)
            throw new ArgumentException("There is no readable text to speak aloud.", nameof(chunks));

        CancellationTokenSource linked;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (RuntimeSafetyState.IsSafeMode)
                throw new InvalidOperationException("Read aloud is disabled while Haven recovery safe mode is active.");
            if (calls.IsActive)
                throw new InvalidOperationException("End the active Haven Call before starting read aloud.");
            if (!speech.IsAvailable)
                throw new InvalidOperationException(speech.UnavailableReason ?? "Local speech output is unavailable.");
            if (IsActive)
                throw new InvalidOperationException("Read aloud is already active. Stop it before starting another passage.");

            _sessionVoice = ResolveVoiceLocked(speech.Voices, language);
            var output = speech.Devices.FirstOrDefault(value => value.IsDefault)
                         ?? speech.Devices.FirstOrDefault();
            _sessionOutputDeviceId = output?.Id;
            _chunks = prepared;
            Volatile.Write(ref _chunkIndex, 0);
            _replayCurrentChunk = false;
            Volatile.Write(ref _paused, 0);
            _resumeSignal = null;
            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeCancellation = linked;
            Volatile.Write(ref _active, 1);
            Volatile.Write(ref _reading, 1);
            RaiseIsReadingChanged(true);
            RaiseProgressLocked();
            RaiseStatus($"Reading {prepared.Count} section{(prepared.Count == 1 ? string.Empty : "s")} aloud locally…");
        }
        finally
        {
            _gate.Release();
        }

        try
        {
            var finished = await RunChunksAsync(linked).ConfigureAwait(false);
            if (finished && !linked.IsCancellationRequested)
            {
                await diagnostics.WriteAsync(
                    ReliabilitySeverity.Information,
                    "notes",
                    "read-aloud-completed",
                    "A long document was read aloud section by section through local speech synthesis.",
                    new Dictionary<string, string>
                    {
                        ["sections"] = prepared.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["characters"] = prepared.Sum(value => value.Length).ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["voice"] = _sessionVoice?.Name ?? "system default",
                        ["networkContentSent"] = bool.FalseString
                    },
                    cancellationToken: CancellationToken.None).ConfigureAwait(false);
                RaiseStatus("Read aloud completed locally.");
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            // StopAsync or the caller intentionally interrupted this local playback.
        }
        finally
        {
            await CompleteReadAsync(linked).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Owns playback for one long-form session: speaks each chunk in order, waits while paused
    /// with the position retained, and reacts to skip interruptions by re-reading shared state.
    /// Returns true only when every chunk finished naturally.
    /// </summary>
    private async Task<bool> RunChunksAsync(CancellationTokenSource session)
    {
        while (true)
        {
            int index = -1;
            string chunk = string.Empty;
            CancellationTokenSource? chunkCts = null;
            Task? resumeSignal = null;
            await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (!ReferenceEquals(_activeCancellation, session)) return false;
                if (Volatile.Read(ref _paused) == 1)
                {
                    if (_resumeSignal is null || _resumeSignal.Task.IsCompleted)
                        _resumeSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    resumeSignal = _resumeSignal.Task;
                }
                else
                {
                    index = Volatile.Read(ref _chunkIndex);
                    if (index >= _chunks.Count) return true;
                    chunk = _chunks[index];
                    chunkCts = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
                    _chunkCancellation = chunkCts;
                }
            }
            finally
            {
                _gate.Release();
            }

            if (resumeSignal is not null)
            {
                // Wait outside the gate; session cancellation surfaces here and ends the loop.
                await resumeSignal.WaitAsync(session.Token).ConfigureAwait(false);
                continue;
            }

            var utterance = chunkCts!;
            try
            {
                await speech.SpeakAsync(chunk, _sessionVoice?.Id, _sessionOutputDeviceId, utterance.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!session.IsCancellationRequested && utterance.IsCancellationRequested)
            {
                // Skip or pause interrupted this chunk on purpose; shared state already moved.
            }
            finally
            {
                await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    if (ReferenceEquals(_chunkCancellation, utterance))
                        _chunkCancellation = null;
                }
                finally
                {
                    _gate.Release();
                }
                utterance.Dispose();
            }

            await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (!ReferenceEquals(_activeCancellation, session)) return false;
                if (Volatile.Read(ref _paused) == 1) continue;
                if (_chunkIndex == index && !_replayCurrentChunk)
                {
                    Volatile.Write(ref _chunkIndex, index + 1);
                    RaiseProgressLocked();
                }
                _replayCurrentChunk = false;
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    /// <summary>
    /// Moves one chunk forward and restarts speech there; at the final section it honestly reports
    /// that no further section exists instead of replaying it.
    /// </summary>
    public Task SkipForwardAsync(CancellationToken cancellationToken) =>
        SkipAsync(+1, cancellationToken);

    /// <summary>
    /// Moves one chunk back and restarts speech there; at the first section it replays that
    /// section from its beginning.
    /// </summary>
    public Task SkipBackwardAsync(CancellationToken cancellationToken) =>
        SkipAsync(-1, cancellationToken);

    /// <summary>
    /// Applies a skip while active or while paused with position retained, cancelling exactly the
    /// utterance currently in flight so the owner loop restarts at the target chunk.
    /// </summary>
    private async Task SkipAsync(int offset, CancellationToken cancellationToken)
    {
        CancellationTokenSource? interrupted;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _reading) != 1) return;
            var count = Math.Max(1, _chunks.Count);
            var current = Volatile.Read(ref _chunkIndex);
            var target = Math.Clamp(current + offset, 0, count - 1);
            if (Volatile.Read(ref _paused) == 1)
            {
                if (target == current)
                {
                    RaiseStatus($"Paused at section {current + 1} of {_chunks.Count}. No further section exists in that direction.");
                    return;
                }

                Volatile.Write(ref _chunkIndex, target);
                RaiseProgressLocked();
                RaiseStatus($"Paused at section {target + 1} of {_chunks.Count}.");
                return;
            }

            if (offset > 0 && target == current)
            {
                RaiseStatus($"Section {current + 1} of {_chunks.Count} is the final section to read.");
                return;
            }

            if (target != current)
                Volatile.Write(ref _chunkIndex, target);
            else
                _replayCurrentChunk = true;
            interrupted = TakeChunkCancellationLocked();
            RaiseProgressLocked();
            RaiseStatus($"Reading section {target + 1} of {_chunks.Count}…");
        }
        finally
        {
            _gate.Release();
        }

        CancelInterrupted(interrupted);
    }

    /// <summary>
    /// Stops the current utterance while keeping the queue position so resume can continue there.
    /// This is honest stop-with-position-retained because the TTS engine offers no native pause.
    /// </summary>
    public async Task PauseAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? interrupted;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _reading) != 1 || Volatile.Read(ref _paused) == 1) return;
            Volatile.Write(ref _paused, 1);
            interrupted = TakeChunkCancellationLocked();
            var index = Math.Min(Volatile.Read(ref _chunkIndex), Math.Max(0, _chunks.Count - 1));
            RaiseStatus($"Read aloud paused at section {index + 1} of {_chunks.Count}. Resume re-speaks this section.");
        }
        finally
        {
            _gate.Release();
        }

        CancelInterrupted(interrupted);
    }

    /// <summary>
    /// Resumes a paused session by re-speaking the retained current chunk from its beginning.
    /// </summary>
    public async Task ResumeAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource? signal;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _paused) != 1) return;
            Volatile.Write(ref _paused, 0);
            signal = _resumeSignal;
            _resumeSignal = null;
            var index = Math.Min(Volatile.Read(ref _chunkIndex), Math.Max(0, _chunks.Count - 1));
            RaiseStatus($"Resuming read aloud at section {index + 1} of {_chunks.Count}.");
        }
        finally
        {
            _gate.Release();
        }

        signal?.TrySetResult();
    }

    /// <summary>
    /// Performs stop asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? interrupted;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cancellation = Interlocked.Exchange(ref _activeCancellation, null);
            var wasActive = Interlocked.Exchange(ref _active, 0) == 1;
            interrupted = FinalizeLongFormStateLocked();
            cancellation?.Cancel();
            try
            {
                // ISpeechOutputService is shared with Call. Never interrupt it unless
                // this controller actually owns an active Notes utterance.
                if (wasActive)
                    await speech.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                cancellation?.Dispose();
                if (wasActive) RaiseStatus("Read aloud stopped.");
            }
        }
        finally
        {
            _gate.Release();
        }

        CancelInterrupted(interrupted);
    }

    /// <summary>
    /// Takes ownership of the in-flight chunk cancellation under the gate so the caller may
    /// cancel it after releasing the gate.
    /// </summary>
    private CancellationTokenSource? TakeChunkCancellationLocked()
    {
        var chunk = _chunkCancellation;
        _chunkCancellation = null;
        return chunk;
    }

    /// <summary>
    /// Cancels and disposes an interrupted chunk cancellation outside the gate. Only the owner
    /// loop disposes its own cancellation otherwise, so double disposal cannot occur.
    /// </summary>
    private void CancelInterrupted(CancellationTokenSource? interrupted)
    {
        if (interrupted is null) return;
        interrupted.Cancel();
        interrupted.Dispose();
    }

    /// <summary>
    /// Clears all long-form session state once per session and reports the end of reading.
    /// Must run under the gate; returns any chunk cancellation the caller should cancel.
    /// </summary>
    private CancellationTokenSource? FinalizeLongFormStateLocked()
    {
        if (Interlocked.Exchange(ref _reading, 0) != 1) return null;
        var interrupted = TakeChunkCancellationLocked();
        _chunks = [];
        Volatile.Write(ref _chunkIndex, 0);
        Volatile.Write(ref _paused, 0);
        _replayCurrentChunk = false;
        _sessionVoice = null;
        _sessionOutputDeviceId = null;
        _resumeSignal?.TrySetResult();
        _resumeSignal = null;
        RaiseIsReadingChanged(false);
        return interrupted;
    }

    /// <summary>
    /// Performs complete read asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task CompleteReadAsync(CancellationTokenSource owner)
    {
        CancellationTokenSource? interrupted;
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(_activeCancellation, owner)) return;
            _activeCancellation = null;
            Volatile.Write(ref _active, 0);
            interrupted = FinalizeLongFormStateLocked();
            owner.Dispose();
            RaiseStatus("Read aloud is idle.");
        }
        finally
        {
            _gate.Release();
        }

        CancelInterrupted(interrupted);
    }

    /// <summary>
    /// Resolves the voice for a request, honouring the preferred pass-through voice first and
    /// then the existing language-based selection.
    /// </summary>
    private CallVoice? ResolveVoiceLocked(IReadOnlyList<CallVoice> voices, string? language)
    {
        if (!string.IsNullOrWhiteSpace(_preferredVoice))
        {
            var preferred = voices.FirstOrDefault(value =>
                string.Equals(value.Id, _preferredVoice, StringComparison.OrdinalIgnoreCase)
                || string.Equals(value.Name, _preferredVoice, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null) return preferred;
        }

        return SelectVoice(voices, language);
    }

    /// <summary>
    /// Performs the select voice step owned by this component.
    /// </summary>
    private static CallVoice? SelectVoice(
        IReadOnlyList<CallVoice> voices,
        string? language)
    {
        if (voices.Count == 0) return null;
        if (!string.IsNullOrWhiteSpace(language))
        {
            var exact = voices.FirstOrDefault(value =>
                string.Equals(value.Culture, language, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact;
            var prefix = language.Split('-', StringSplitOptions.RemoveEmptyEntries)[0];
            var matching = voices.FirstOrDefault(value =>
                value.Culture?.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase) == true
                || string.Equals(value.Culture, prefix, StringComparison.OrdinalIgnoreCase));
            if (matching is not null) return matching;
        }
        return voices.FirstOrDefault(value => value.IsDefault) ?? voices[0];
    }

    /// <summary>
    /// Reports progress for the shared queue position; call only while the gate is held or during
    /// single-threaded session setup.
    /// </summary>
    private void RaiseProgressLocked() =>
        ProgressChanged?.Invoke(_chunks.Count == 0 ? 1d : Math.Min(1d, Volatile.Read(ref _chunkIndex) / (double)_chunks.Count));

    /// <summary>
    /// Performs the raise status step owned by this component.
    /// </summary>
    private void RaiseStatus(string message, bool isError = false) =>
        StatusChanged?.Invoke(this, new NotesReadAloudStatus(IsActive, message, isError));

    /// <summary>
    /// Raises the reading-state change event for long-form sessions.
    /// </summary>
    private void RaiseIsReadingChanged(bool isReading) =>
        IsReadingChanged?.Invoke(isReading);

    /// <summary>
    /// Performs dispose asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }
}

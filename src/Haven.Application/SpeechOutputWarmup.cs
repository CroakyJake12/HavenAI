/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/SpeechOutputWarmup.cs, in the Application layer.
 * What: Defines optional preparation for speech engines that have expensive local model startup.
 * Why: Live call audio should not pay neural TTS loading cost after the language model has already replied.
 */

namespace Haven.Application;

/// <summary>Allows Call to prepare the selected local voice without producing audio.</summary>
public interface ISpeechOutputWarmup
{
    Task WarmAsync(string? voiceName, CancellationToken cancellationToken);
}

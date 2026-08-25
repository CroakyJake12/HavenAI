/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Services/DualModelChatController.cs, in the Desktop services layer.
 * What: Owns the session-only state machine for side-by-side dual-model chat: active flag, second model
 *       key, busy flag, and the single RunAsync entry over DualModelService.
 * How: NewChatPage resolves this controller's dependencies from App.Services (DualModelService is an
 *      App-level singleton) because the page's own constructor signature is fixed by MainView. The
 *      controller performs no UI work and no persistence; the page renders both sides and owns status.
 * Why: Keeping toggle/second-model/run state in one testable service keeps NewChatPage wiring minimal
 *      while DualModelService stays a pure Application-layer runtime.
 * Maintenance: This is intentionally non-streaming (CompleteAsync based) and session-only — dual results
 *              are never persisted and never streamed token-by-token. Preserve those limitations unless
 *              ChatSessionService gains an official dual path; also keep per-side failures honest.
 */

using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Services;

/// <summary>
/// Session-only controller behind the Chat surface's "Dual" affordance. When active, the page routes the
/// composer submit through <see cref="RunAsync"/> instead of the normal persisted chat pipeline.
/// </summary>
internal sealed class DualModelChatController(DualModelService dualModels)
{
    /// <summary>Gets whether dual mode currently intercepts chat sends.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gets the chosen second model key, or null until the user picks one.</summary>
    public string? SecondModelKey { get; private set; }

    /// <summary>Gets whether a dual run is currently executing.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Gets whether a submit can be routed through a dual comparison right now.</summary>
    public bool CanRun => IsActive && !IsRunning && SecondModelKey is { Length: > 0 };

    /// <summary>Toggles dual mode; deactivating keeps the chosen second model for later.</summary>
    public void SetActive(bool active) => IsActive = active;

    /// <summary>Sets the second model key used as Model B.</summary>
    public void SetSecondModel(string modelKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelKey);
        SecondModelKey = modelKey;
    }

    /// <summary>
    /// Runs both models over the prompt. Returns null instead of running when no usable second model is
    /// configured so the page can show an honest setup message.
    /// </summary>
    public async Task<DualModelRun?> RunAsync(
        string prompt,
        string firstModelKey,
        EffortLevel effort,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(firstModelKey);
        if (!IsActive || IsRunning || SecondModelKey is not { Length: > 0 } second) return null;
        IsRunning = true;
        try
        {
            return await dualModels.RunAsync(prompt, firstModelKey, second, effort, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            IsRunning = false;
        }
    }
}

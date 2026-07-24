namespace Haven.Core;

/// <summary>
/// A user-selectable problem and the recovery behaviour Haven should apply.
/// </summary>
public sealed record ResolveProblemDefinition(
    string Key,
    string Title,
    string Description,
    ResolveProblemAction Action,
    bool IsAlwaysVisible);

public enum ResolveProblemAction
{
    StopAndRegenerate,
    VerifyClaimsAndRegenerate,
    ReapplyInstructionsAndRegenerate,
    RefocusOnQuestion,
    ReselectCapability,
    RetryFailedOperation,
    RemoveFailedAttachment,
    SelectCompatibleModel,
    RequestPermission,
    UserDescribedCorrection
}

/// <summary>
/// Conversation signals used to add only relevant adaptive problems.
/// </summary>
public sealed record ResolveProblemSignals(
    bool ModelFailed,
    bool ToolFailed,
    bool PluginUnavailable,
    bool AttachmentFailed,
    bool ContextLimitReached,
    bool ModelUnavailable,
    bool PermissionRequired,
    bool ResponseStopped,
    bool RepetitionDetected);

/// <summary>
/// Builds the Resolve Problems menu. Common quality problems are always present;
/// runtime failures are adaptive.
/// </summary>
public static class ResolveProblemCatalog
{
    public static IReadOnlyList<ResolveProblemDefinition> AlwaysVisible { get; } =
    [
        new("looping", "Looping or repeating", "Stop the repetition and retry from the last stable turn.", ResolveProblemAction.StopAndRegenerate, true),
        new("hallucinating", "Hallucinating or making things up", "Verify the claims, separate evidence from assumptions, and retry.", ResolveProblemAction.VerifyClaimsAndRegenerate, true),
        new("ignored_instructions", "Ignoring my instructions", "Reapply the active instructions and retry the last response.", ResolveProblemAction.ReapplyInstructionsAndRegenerate, true),
        new("not_answering", "Not answering my question", "Refocus the response on the user's actual question.", ResolveProblemAction.RefocusOnQuestion, true),
        new("wrong_capability", "Used the wrong tool or mode", "Remove the incorrect capability and let the user select the right one for the retry.", ResolveProblemAction.ReselectCapability, true),
        new("poor_quality", "Response quality is poor", "Retry with the user's correction applied to this turn.", ResolveProblemAction.UserDescribedCorrection, true),
        new("other", "Other problem", "Apply a short user-described correction to the retry.", ResolveProblemAction.UserDescribedCorrection, true)
    ];

    public static IReadOnlyList<ResolveProblemDefinition> Build(ResolveProblemSignals signals)
    {
        ArgumentNullException.ThrowIfNull(signals);

        var result = AlwaysVisible.ToList();

        void AddIf(bool condition, ResolveProblemDefinition definition)
        {
            if (condition)
            {
                result.Add(definition);
            }
        }

        AddIf(signals.ModelFailed, new("model_failed", "Model request failed", "Retry the model request.", ResolveProblemAction.RetryFailedOperation, false));
        AddIf(signals.ToolFailed, new("tool_failed", "Tool action failed", "Retry or reselect the capability.", ResolveProblemAction.RetryFailedOperation, false));
        AddIf(signals.PluginUnavailable, new("plugin_unavailable", "Plugin unavailable", "Disable the plugin or install it.", ResolveProblemAction.ReselectCapability, false));
        AddIf(signals.AttachmentFailed, new("attachment_failed", "Attachment could not be processed", "Remove the failed attachment and retry.", ResolveProblemAction.RemoveFailedAttachment, false));
        AddIf(signals.ContextLimitReached, new("high_context", "Conversation context is full", "Compact older turns before retrying.", ResolveProblemAction.RetryFailedOperation, false));
        AddIf(signals.ModelUnavailable, new("model_unavailable", "Selected model is unavailable", "Select a compatible installed model.", ResolveProblemAction.SelectCompatibleModel, false));
        AddIf(signals.PermissionRequired, new("permission_required", "Permission is required", "Review and approve the requested capability.", ResolveProblemAction.RequestPermission, false));
        AddIf(signals.ResponseStopped, new("response_stopped", "Response stopped early", "Retry the last turn.", ResolveProblemAction.RetryFailedOperation, false));
        AddIf(signals.RepetitionDetected, new("repetition_detected", "Repetition detected in this response", "Retry from the last stable turn.", ResolveProblemAction.StopAndRegenerate, false));

        return result;
    }
}

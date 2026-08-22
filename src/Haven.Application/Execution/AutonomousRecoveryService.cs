using System.Collections.Concurrent;
using Haven.Core;

namespace Haven.Application;

/// <summary>Central bounded risk classifier used by active execution and historical Fix with AI.</summary>
public sealed class AutonomousRecoveryService
{
    private readonly ConcurrentDictionary<(Guid ExecutionId, string Signature), int> _failureCounts = new();

    public RecoveryAttempt Plan(
        Guid executionId,
        Guid failedActionId,
        string failureSignature,
        RecoveryRiskAssessment risk,
        string safeDiagnosis,
        string? safeRepair = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureSignature);
        var signature = SensitiveTextRedactor.Redact(failureSignature, 512);
        var attempt = _failureCounts.AddOrUpdate((executionId, signature), 1, static (_, count) => count + 1);
        var classification = Classify(risk);
        var stage = SelectStage(classification, attempt, safeRepair);
        return new RecoveryAttempt(
            Guid.NewGuid(), executionId, failedActionId, classification, stage, signature,
            attempt, RecoveryPolicyDefaults.MaximumAutomaticAttempts, risk,
            SensitiveTextRedactor.Redact(safeDiagnosis, 2_000),
            SensitiveTextRedactor.Redact(safeRepair, 2_000), DateTimeOffset.UtcNow);
    }

    public void ClearExecution(Guid executionId)
    {
        foreach (var key in _failureCounts.Keys.Where(key => key.ExecutionId == executionId))
            _failureCounts.TryRemove(key, out _);
    }

    private static RecoveryClass Classify(RecoveryRiskAssessment risk)
    {
        if (risk.Destructive || risk.AltersUserData || risk.HasExternalImpact || risk.ExpandsPermissions)
            return RecoveryClass.RiskyOrDestructive;
        if (risk.RequiresUnknownCredential || !risk.InsideAuthorisedScope || risk.Confidence < 0.75)
            return RecoveryClass.UserInformationOrPermission;
        return risk.MayRecoverAutonomously ? RecoveryClass.Autonomous : RecoveryClass.UserInformationOrPermission;
    }

    private static RecoveryStage SelectStage(RecoveryClass classification, int attempt, string? repair) => classification switch
    {
        RecoveryClass.RiskyOrDestructive => RecoveryStage.RequestRiskyApproval,
        RecoveryClass.UserInformationOrPermission => RecoveryStage.AskUserForRequiredInformation,
        _ when attempt > RecoveryPolicyDefaults.MaximumEquivalentFailures => RecoveryStage.Exhausted,
        _ when attempt == 1 => RecoveryStage.SafeAutomaticRetry,
        _ when !string.IsNullOrWhiteSpace(repair) => RecoveryStage.SafeAutonomousRepair,
        _ => RecoveryStage.SafeAlternative
    };
}

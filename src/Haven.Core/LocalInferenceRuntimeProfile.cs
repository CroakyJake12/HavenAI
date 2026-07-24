namespace Haven.Core;

/// <summary>
/// Accuracy-preserving local inference settings derived from the four-step reasoning scale.
/// These settings alter residency and context allocation only; they never change model weights,
/// precision, quantisation, pruning, or architecture.
/// </summary>
public sealed record LocalInferenceRuntimeProfile(
    string Name,
    int ContextLimit,
    string KeepAlive,
    bool PreloadModel,
    bool PreferFullGpuResidency,
    bool AllowCpuOffload,
    bool AllowMemoryMappedFallback,
    bool PreserveExactWeights)
{
    public static LocalInferenceRuntimeProfile Create(
        EffortLevel effort,
        int requestedContextLimit)
    {
        var requested = Math.Clamp(requestedContextLimit, 2048, 262_144);

        return effort switch
        {
            EffortLevel.Low => new(
                "Fast",
                Math.Min(requested, 8_192),
                "2m",
                false,
                true,
                true,
                true,
                true),

            EffortLevel.Medium => new(
                "Balanced",
                Math.Min(requested, 16_384),
                "5m",
                false,
                true,
                true,
                true,
                true),

            EffortLevel.High => new(
                "Deep",
                Math.Min(requested, 32_768),
                "10m",
                false,
                true,
                true,
                true,
                true),

            EffortLevel.Max => new(
                "Maximum",
                requested,
                "45m",
                true,
                true,
                true,
                true,
                true),

            _ => throw new ArgumentOutOfRangeException(nameof(effort), effort, null)
        };
    }
}

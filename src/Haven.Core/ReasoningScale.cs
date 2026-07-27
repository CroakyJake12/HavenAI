namespace Haven.Core;

/// <summary>
/// Keeps the user-facing reasoning percentages and the provider effort enum in sync.
/// The current product scale has exactly four steps: 25%, 50%, 75%, and 100%.
/// </summary>
public static class ReasoningScale
{
    public const int MinimumPercentage = 25;
    public const int MaximumPercentage = 100;
    public const int StepSize = 25;

    public static IReadOnlyList<int> Percentages { get; } =
        [MinimumPercentage, 50, 75, MaximumPercentage];

    public static int ToPercentage(EffortLevel effort) => effort switch
    {
        EffortLevel.Low => 25,
        EffortLevel.Medium => 50,
        EffortLevel.High => 75,
        EffortLevel.Max => 100,
        _ => throw new ArgumentOutOfRangeException(nameof(effort), effort, null)
    };

    public static EffortLevel FromPercentage(double percentage) =>
        SnapPercentage(percentage) switch
        {
            25 => EffortLevel.Low,
            50 => EffortLevel.Medium,
            75 => EffortLevel.High,
            100 => EffortLevel.Max,
            _ => throw new InvalidOperationException("Reasoning percentage snapping returned an unsupported value.")
        };

    public static int SnapPercentage(double percentage)
    {
        if (double.IsNaN(percentage) || double.IsInfinity(percentage))
        {
            throw new ArgumentOutOfRangeException(nameof(percentage));
        }

        var clamped = Math.Clamp(percentage, MinimumPercentage, MaximumPercentage);
        var step = (int)Math.Round(clamped / StepSize, MidpointRounding.AwayFromZero);
        return Math.Clamp(step * StepSize, MinimumPercentage, MaximumPercentage);
    }

    /// <summary>
    /// The large-model, accuracy-preserving runtime profile is reserved for the top step.
    /// This is the discrete equivalent of the previous 80-100% threshold.
    /// </summary>
    public static bool UsesHighReasoningRuntime(EffortLevel effort) =>
        effort == EffortLevel.Max;
}

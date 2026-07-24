using Haven.Core;

namespace Haven.Core.Tests;

public sealed class ReasoningScaleTests
{
    [Theory]
    [InlineData(EffortLevel.Low, 25)]
    [InlineData(EffortLevel.Medium, 50)]
    [InlineData(EffortLevel.High, 75)]
    [InlineData(EffortLevel.Max, 100)]
    public void EffortMapsToExpectedPercentage(EffortLevel effort, int percentage)
    {
        Assert.Equal(percentage, ReasoningScale.ToPercentage(effort));
        Assert.Equal(effort, ReasoningScale.FromPercentage(percentage));
    }

    [Theory]
    [InlineData(1, 25)]
    [InlineData(37, 25)]
    [InlineData(38, 50)]
    [InlineData(62, 50)]
    [InlineData(63, 75)]
    [InlineData(87, 75)]
    [InlineData(88, 100)]
    [InlineData(140, 100)]
    public void SliderSnapsToFourSteps(double value, int expected)
    {
        Assert.Equal(expected, ReasoningScale.SnapPercentage(value));
    }

    [Fact]
    public void MaximumProfilePreservesRequestedContextAndPreloads()
    {
        var profile = LocalInferenceRuntimeProfile.Create(EffortLevel.Max, 131_072);

        Assert.Equal(131_072, profile.ContextLimit);
        Assert.True(profile.PreloadModel);
        Assert.Equal("45m", profile.KeepAlive);
        Assert.True(profile.PreserveExactWeights);
    }

    [Theory]
    [InlineData(EffortLevel.Low, 8_192)]
    [InlineData(EffortLevel.Medium, 16_384)]
    [InlineData(EffortLevel.High, 32_768)]
    public void LowerProfilesRemainLatencyBounded(EffortLevel effort, int expectedLimit)
    {
        var profile = LocalInferenceRuntimeProfile.Create(effort, 131_072);

        Assert.Equal(expectedLimit, profile.ContextLimit);
        Assert.False(profile.PreloadModel);
        Assert.True(profile.PreserveExactWeights);
    }
}

using Haven.Core;

namespace Haven.Core.Tests;

public sealed class DeterministicCalculatorTests
{
    [Theory]
    [InlineData("2 + 3 * 4", 14)]
    [InlineData("(2 + 3) * 4", 20)]
    [InlineData("2^3^2", 512)]
    [InlineData("sqrt(81) + abs(-3)", 12)]
    [InlineData("max(4, min(9, 6))", 6)]
    public void Evaluates_supported_math_without_script_execution(string expression, double expected) =>
        Assert.Equal(expected, DeterministicCalculator.Evaluate(expression), precision: 10);

    [Theory]
    [InlineData("1 / 0")]
    [InlineData("sqrt(-1)")]
    [InlineData("System.IO.File.Delete(1)")]
    [InlineData("2 +")]
    public void Rejects_invalid_unsafe_or_non_finite_expressions(string expression) =>
        Assert.Throws<InvalidOperationException>(() => DeterministicCalculator.Evaluate(expression));
}

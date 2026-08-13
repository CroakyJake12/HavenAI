namespace Haven.UI;

public enum HavenPlatform
{
    Unknown,
    Windows,
    Android,
    IOS,
    MacOS,
    Linux
}

public sealed record HavenRenderContext(HavenPlatform Platform, HavenSize Viewport);

public interface IHavenRenderCondition
{
    bool Matches(HavenRenderContext context);
}

public sealed class HavenPlatformCondition : IHavenRenderCondition
{
    private readonly HashSet<HavenPlatform> _platforms;

    public HavenPlatformCondition(string platforms)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platforms);
        _platforms = platforms.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => Enum.TryParse<HavenPlatform>(value, true, out var parsed)
                ? parsed
                : throw new FormatException($"Unknown Haven platform '{value}'."))
            .ToHashSet();
    }

    public bool Matches(HavenRenderContext context) => _platforms.Contains(context.Platform);
}

public enum HavenScreenAxis { Width, Height }

public sealed class HavenScreenRangeCondition(
    HavenScreenAxis axis,
    HavenLength? minimum = null,
    HavenLength? maximum = null) : IHavenRenderCondition
{
    public HavenScreenAxis Axis { get; } = axis;
    public HavenLength? Minimum { get; } = minimum;
    public HavenLength? Maximum { get; } = maximum;

    public bool Matches(HavenRenderContext context)
    {
        var extent = Axis == HavenScreenAxis.Width ? context.Viewport.Width : context.Viewport.Height;
        var minimum = Minimum is { } min ? Resolve(min, context, extent) : double.NegativeInfinity;
        var maximum = Maximum is { } max ? Resolve(max, context, extent) : double.PositiveInfinity;
        return extent >= minimum && extent <= maximum;
    }

    private static double Resolve(HavenLength length, HavenRenderContext context, double extent)
    {
        var value = length.Resolve(extent, context.Viewport);
        if (double.IsNaN(value)) throw new InvalidOperationException("Auto/fr cannot be used as screen-condition bounds.");
        return value;
    }
}

public sealed class HavenScreenSizeCondition(
    HavenLength? minWidth = null,
    HavenLength? maxWidth = null,
    HavenLength? minHeight = null,
    HavenLength? maxHeight = null) : IHavenRenderCondition
{
    private readonly HavenScreenRangeCondition _width = new(HavenScreenAxis.Width, minWidth, maxWidth);
    private readonly HavenScreenRangeCondition _height = new(HavenScreenAxis.Height, minHeight, maxHeight);

    public bool Matches(HavenRenderContext context) => _width.Matches(context) && _height.Matches(context);
}

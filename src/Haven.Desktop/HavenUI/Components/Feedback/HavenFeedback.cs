using Avalonia.Controls;

namespace Haven.Desktop.HavenUI.Components;

/// <summary>Canonical transient or persistent notification surface.</summary>
public sealed class HavenNotification : ContentControl
{
    public HavenNotification() => Classes.Add("havenNotification");
}

/// <summary>Compact count or state badge.</summary>
public sealed class HavenBadge : ContentControl
{
    public HavenBadge() => Classes.Add("havenBadge");
}

/// <summary>Semantic status pill.</summary>
public sealed class HavenStatusChip : ContentControl
{
    public HavenStatusChip() => Classes.Add("havenStatusChip");
}

/// <summary>Canonical loading-state host.</summary>
public sealed class HavenLoadingState : ContentControl
{
    public HavenLoadingState() => Classes.Add("havenLoadingState");
}

/// <summary>Canonical error-state host.</summary>
public sealed class HavenErrorState : ContentControl
{
    public HavenErrorState() => Classes.Add("havenErrorState");
}

/// <summary>Accent-aware indeterminate or determinate progress ring.</summary>
public sealed class HavenProgressRing : ContentControl
{
    public HavenProgressRing() => Classes.Add("havenProgressRing");
}

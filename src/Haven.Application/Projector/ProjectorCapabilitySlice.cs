namespace Haven.Application;

/// <summary>
/// Opt-in display gate for the smallest Projector slice. Unknown capabilities fail closed.
/// </summary>
public static class ProjectorCapabilitySlice
{
    public const string Key = "projector-display";
    public const bool IsEnabledByDefault = false;

    public static bool CanRender(ProjectorDisplay? display, bool enabled = IsEnabledByDefault)
    {
        return enabled
            && display is not null
            && display.Capabilities.PresentationDisplay == ProjectorCapabilityState.Available
            && display.Capabilities.RenderHavenSurface == ProjectorCapabilityState.Available;
    }
}

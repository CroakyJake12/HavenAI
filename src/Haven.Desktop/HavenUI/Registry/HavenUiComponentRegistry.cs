using Avalonia.Controls;
using Haven.Desktop.HavenUI.Components;

namespace Haven.Desktop.HavenUI.Registry;

public enum HavenUiComponentCategory
{
    Action,
    Input,
    Navigation,
    Surface,
    Overlay,
    Feedback,
    Responsive
}

public sealed record HavenUiComponentDescriptor(
    string ComponentType,
    Type ControlType,
    HavenUiComponentCategory Category,
    string AccentTier,
    bool SupportsCompactPresentation,
    bool SupportsTouchContextActions,
    string Purpose);

/// <summary>
/// Trusted component vocabulary for Generative UI. Renderers resolve semantic
/// component names here instead of accepting arbitrary styled XAML.
/// </summary>
public static class HavenUiComponentRegistry
{
    private static readonly HavenUiComponentDescriptor[] ComponentList = Build().ToArray();
    private static readonly IReadOnlyDictionary<string, HavenUiComponentDescriptor> Components =
        ComponentList.ToDictionary(item => item.ComponentType, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<HavenUiComponentDescriptor> All => ComponentList;

    public static HavenUiComponentDescriptor Resolve(string componentType) =>
        Components.TryGetValue(componentType, out var descriptor)
            ? descriptor
            : throw new KeyNotFoundException($"HavenUI has no trusted component named '{componentType}'.");

    public static bool TryResolve(string componentType, out HavenUiComponentDescriptor descriptor) =>
        Components.TryGetValue(componentType, out descriptor!);

    public static Control Create(string componentType)
    {
        var descriptor = Resolve(componentType);
        return (Control)(Activator.CreateInstance(descriptor.ControlType)
            ?? throw new InvalidOperationException($"HavenUI component '{componentType}' could not be created."));
    }

    private static IEnumerable<HavenUiComponentDescriptor> Build()
    {
        yield return Action<HavenButton>("HavenButton", "Adaptive", "Compatibility action mapped to canonical semantic roles");
        yield return Action<HavenPrimaryButton>("HavenPrimaryButton", "Primary", "Dominant action");
        yield return Action<HavenSecondaryButton>("HavenSecondaryButton", "Secondary", "Secondary filled action");
        yield return Action<HavenTertiaryButton>("HavenTertiaryButton", "Tertiary", "Subdued action");
        yield return Action<HavenNegativeButton>("HavenNegativeButton", "SemanticNegative", "Immediate destructive action");
        yield return Action<HavenTextButton>("HavenTextButton", "Secondary", "Borderless text action");
        yield return Action<HavenIconButton>("HavenIconButton", "Primary", "Icon-only action");
        yield return Action<HavenChipButton>("HavenChipButton", "Tertiary", "Compact semantic action");
        yield return Item<HavenToggleButton>("HavenToggleButton", HavenUiComponentCategory.Action, "Tertiary", true, false, "Stateful reveal or selection action");
        yield return Action<HavenModelPickerButton>("HavenModelPicker", "ReasoningOverride", "Reasoning-aware model selector");

        yield return Input<HavenTextInput>("HavenTextInput", "Single-line text input");
        yield return Input<HavenSearchInput>("HavenSearchInput", "Search field");
        yield return Input<HavenMultilineInput>("HavenMultilineInput", "Multi-line editor");
        yield return Input<HavenSelect>("HavenSelect", "Haven-owned selection field and dropdown");
        yield return Input<HavenSwitch>("HavenToggle", "Binary switch");
        yield return Input<HavenSlider>("HavenSlider", "Interactive range input");
        yield return Input<HavenProgressBar>("HavenProgress", "Progress indicator");
        yield return Input<HavenCheckBox>("HavenCheckBox", "Binary checkbox");
        yield return Input<HavenRadioButton>("HavenRadioButton", "Exclusive selection");
        yield return Input<HavenCalendarPicker>("HavenCalendarPicker", "Calendar date field");

        yield return Item<HavenTabView>("HavenTabs", HavenUiComponentCategory.Navigation, "Primary", true, false, "Content tab system");
        yield return Item<HavenListBox>("HavenListBox", HavenUiComponentCategory.Navigation, "Tertiary", true, false, "Selectable collection");
        yield return Item<HavenExpander>("HavenExpander", HavenUiComponentCategory.Navigation, "Tertiary", true, false, "Advanced-content disclosure");
        yield return Item<HavenNavigationButton>("HavenNavigationButton", HavenUiComponentCategory.Navigation, "Tertiary", true, true, "Navigation row");
        yield return Item<HavenAdaptiveSurface>("HavenAdaptiveSurface", HavenUiComponentCategory.Surface, "Adaptive", true, false, "Compatibility surface mapped to canonical semantic roles");
        yield return Item<HavenCard>("HavenCard", HavenUiComponentCategory.Surface, "Tertiary", true, false, "Content card");
        yield return Item<HavenDragHandle>("HavenDragHandle", HavenUiComponentCategory.Navigation, "Secondary", true, true, "Mobile sheet swipe affordance");
        yield return Item<HavenSelectionIndicator>("HavenSelectionIndicator", HavenUiComponentCategory.Navigation, "Primary", true, false, "Selected tab marker");
        yield return Item<HavenToolbar>("HavenToolbar", HavenUiComponentCategory.Surface, "Tertiary", true, false, "Command toolbar");
        yield return Item<HavenComposerShell>("HavenComposer", HavenUiComponentCategory.Surface, "Tertiary", true, false, "Message/task composer");
        yield return Item<HavenPopupCard>("HavenPopup", HavenUiComponentCategory.Overlay, "Primary", true, false, "Desktop popup surface");
        yield return Item<HavenMobileSheet>("HavenMobileSheet", HavenUiComponentCategory.Overlay, "Primary", true, true, "Compact bottom sheet");
        yield return Item<HavenNotification>("HavenNotification", HavenUiComponentCategory.Feedback, "Tertiary", true, false, "Notification surface");
        yield return Item<HavenStatusChip>("HavenStatus", HavenUiComponentCategory.Feedback, "Tertiary", true, false, "Semantic status");
        yield return Item<HavenLoadingState>("HavenLoadingState", HavenUiComponentCategory.Feedback, "Primary", true, false, "Loading state");
        yield return Item<HavenErrorState>("HavenErrorState", HavenUiComponentCategory.Feedback, "SemanticNegative", true, false, "Error state");
    }

    private static HavenUiComponentDescriptor Action<T>(string name, string tier, string purpose) where T : Control, new() =>
        Item<T>(name, HavenUiComponentCategory.Action, tier, true, true, purpose);

    private static HavenUiComponentDescriptor Input<T>(string name, string purpose) where T : Control, new() =>
        Item<T>(name, HavenUiComponentCategory.Input, "Primary", true, false, purpose);

    private static HavenUiComponentDescriptor Item<T>(
        string name,
        HavenUiComponentCategory category,
        string tier,
        bool compact,
        bool touchContext,
        string purpose) where T : Control, new() =>
        new(name, typeof(T), category, tier, compact, touchContext, purpose);
}

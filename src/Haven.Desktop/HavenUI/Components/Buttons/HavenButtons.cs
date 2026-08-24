using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Haven.Desktop.HavenUI.Tokens;

namespace Haven.Desktop.HavenUI.Components;

/// <summary>
/// Shared behavioural base for every HavenUI button. The deck-defined press
/// compression and release bounce live here so individual pages cannot invent
/// different motion.
/// </summary>
public abstract class HavenButtonBase : Button
{
    private readonly DispatcherTimer _bounceTimer;
    private readonly DispatcherTimer _longPressTimer;
    private Point _touchOrigin;
    private bool _suppressNextClick;

    protected HavenButtonBase(string visualClass)
    {
        Theme = HavenControlThemeResolver.For(typeof(Button));
        Classes.Add("havenButton");
        Classes.Add(visualClass);
        _bounceTimer = new DispatcherTimer { Interval = HavenUiMotion.ButtonBounce };
        _bounceTimer.Tick += (_, _) =>
        {
            _bounceTimer.Stop();
            Classes.Set("releaseBounce", false);
        };
        _longPressTimer = new DispatcherTimer { Interval = HavenUiMotion.TouchLongPress };
        _longPressTimer.Tick += (_, _) =>
        {
            _longPressTimer.Stop();
            if (ContextMenu is null) return;
            _suppressNextClick = true;
            ContextMenu.Open(this);
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _bounceTimer.Stop();
            _longPressTimer.Stop();
            Classes.Set("releaseBounce", false);
        };
    }

    protected override void OnClick()
    {
        if (_suppressNextClick)
        {
            _suppressNextClick = false;
            return;
        }

        Classes.Set("releaseBounce", false);
        Classes.Set("releaseBounce", true);
        _bounceTimer.Stop();
        _bounceTimer.Start();
        base.OnClick();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (e.Pointer.Type == PointerType.Touch && ContextMenu is not null)
        {
            _touchOrigin = e.GetPosition(this);
            _suppressNextClick = false;
            _longPressTimer.Stop();
            _longPressTimer.Start();
        }

        base.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_longPressTimer.IsEnabled)
        {
            var current = e.GetPosition(this);
            if (Math.Abs(current.X - _touchOrigin.X) > 10 || Math.Abs(current.Y - _touchOrigin.Y) > 10)
                _longPressTimer.Stop();
        }

        base.OnPointerMoved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _longPressTimer.Stop();
        base.OnPointerReleased(e);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        _longPressTimer.Stop();
        base.OnPointerCaptureLost(e);
    }
}

/// <summary>
/// Canonical adaptive action used while older composed screens are migrated.
/// Semantic classes such as primary, accent, danger, icon, chip, sidebar and
/// ghost map to the same central HavenUI roles; pages never own their visuals.
/// New screen code should prefer the more specific button classes below.
/// </summary>
public sealed class HavenButton : HavenButtonBase
{
    public HavenButton() : base("havenAdaptive") { }
}

/// <summary>The dominant filled action from slide 3.</summary>
public sealed class HavenPrimaryButton : HavenButtonBase
{
    public HavenPrimaryButton() : base("havenPrimary") { }
}

/// <summary>The warm filled secondary action from slide 3.</summary>
public sealed class HavenSecondaryButton : HavenButtonBase
{
    public HavenSecondaryButton() : base("havenSecondary") { }
}

/// <summary>The subdued action that morphs into the bright hover fill.</summary>
public sealed class HavenTertiaryButton : HavenButtonBase
{
    public HavenTertiaryButton() : base("havenTertiary") { }
}

/// <summary>The immediate destructive action from slide 3.</summary>
public sealed class HavenNegativeButton : HavenButtonBase
{
    public HavenNegativeButton() : base("havenNegative") { }
}

/// <summary>The borderless text action from slide 3.</summary>
public sealed class HavenTextButton : HavenButtonBase
{
    public HavenTextButton() : base("havenText") { }
}

/// <summary>A circular icon-only action with the same glow and bounce contract.</summary>
public class HavenIconButton : HavenButtonBase
{
    public HavenIconButton() : base("havenIcon") { }
}

/// <summary>The compact circular action used in the floating top rail.</summary>
public sealed class HavenHeaderIconButton : HavenButtonBase
{
    public HavenHeaderIconButton() : base("havenHeaderIcon") { }
}

/// <summary>The image-bearing Haven product mark action in the top rail.</summary>
public sealed class HavenLogoButton : HavenButtonBase
{
    public HavenLogoButton() : base("havenLogo") { }
}

/// <summary>The compact labelled action used in the floating top rail.</summary>
public sealed class HavenHeaderPillButton : HavenButtonBase
{
    public HavenHeaderPillButton() : base("havenHeaderPill") { }
}

/// <summary>The orange model selector used in the mockup header.</summary>
public sealed class HavenModelPickerButton : HavenButtonBase
{
    public static readonly StyledProperty<int> EffortPercentageProperty =
        AvaloniaProperty.Register<HavenModelPickerButton, int>(nameof(EffortPercentage), 60);

    public HavenModelPickerButton() : base("havenModelPicker") => ApplyEffort(EffortPercentage);

    public int EffortPercentage
    {
        get => GetValue(EffortPercentageProperty);
        set => SetValue(EffortPercentageProperty, Math.Clamp(value, 0, 100));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EffortPercentageProperty)
            ApplyEffort(change.GetNewValue<int>());
    }

    private void ApplyEffort(int effort)
    {
        Classes.Set("reasoningLow", effort < 35);
        Classes.Set("reasoningBalanced", effort is >= 35 and < 70);
        Classes.Set("reasoningHigh", effort is >= 70 and < 95);
        Classes.Set("reasoningMax", effort >= 95);
    }
}

/// <summary>A full-width navigation row for sidebars and compact menus.</summary>
public sealed class HavenNavigationButton : HavenButtonBase
{
    public HavenNavigationButton() : base("havenNavigation") { }
}

/// <summary>A pill-shaped suggestion/action card used on Go and empty states.</summary>
public sealed class HavenSuggestionButton : HavenButtonBase
{
    public HavenSuggestionButton() : base("havenSuggestion") { }
}

/// <summary>A small semantic badge that is still keyboard actionable.</summary>
public sealed class HavenChipButton : HavenButtonBase
{
    public HavenChipButton() : base("havenChip") { }
}

/// <summary>Touch-sized compact labelled action for responsive/mobile compositions.</summary>
public sealed class HavenMobileActionButton : HavenButtonBase
{
    public HavenMobileActionButton() : base("havenMobileAction") { }
}

/// <summary>The selected/unselected text tab shown on slides 4 and 18.</summary>
public sealed class HavenTabButton : HavenButtonBase
{
    public static readonly Avalonia.StyledProperty<bool> IsSelectedProperty =
        Avalonia.AvaloniaProperty.Register<HavenTabButton, bool>(nameof(IsSelected));

    public HavenTabButton() : base("havenTab")
    {
        Classes.Set("selected", IsSelected);
    }

    public bool IsSelected
    {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsSelectedProperty)
            Classes.Set("selected", change.GetNewValue<bool>());
    }
}

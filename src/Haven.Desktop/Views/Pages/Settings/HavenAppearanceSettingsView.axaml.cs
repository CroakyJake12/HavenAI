using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Haven.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Pages.Settings;

/// <summary>
/// The production HavenUI appearance control. It deliberately owns only a
/// discrete colour choice; no layout or component geometry is theme-editable.
/// </summary>
public sealed partial class HavenAppearanceSettingsView : UserControl
{
    private readonly UserPreferencesService? _preferences;
    private bool _updating;
    private bool _subscribed;

    public HavenAppearanceSettingsView()
        : this(App.Services?.GetService<UserPreferencesService>())
    {
    }

    internal HavenAppearanceSettingsView(UserPreferencesService? preferences)
    {
        _preferences = preferences;
        InitializeComponent();

        AppearanceSlider.ValueChanged += OnSliderValueChanged;
        SuperBrightButton.Click += (_, _) => Select(HavenUiAppearance.SuperBright);
        BrightButton.Click += (_, _) => Select(HavenUiAppearance.Bright);
        DarkButton.Click += (_, _) => Select(HavenUiAppearance.Dark);
        SuperDarkButton.Click += (_, _) => Select(HavenUiAppearance.SuperDark);
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;

        Subscribe();
        Refresh(_preferences?.Appearance ?? HavenUiAppearance.Bright);
    }

    private void OnSliderValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_updating) return;
        Select(FromSlider(e.NewValue));
    }

    private void Select(HavenUiAppearance appearance)
    {
        _preferences?.ApplyAppearance(appearance);
        Refresh(appearance);
    }

    private void OnAppearanceChanged(object? sender, EventArgs e) =>
        Refresh(_preferences?.Appearance ?? HavenUiAppearance.Bright);

    private void Refresh(HavenUiAppearance appearance)
    {
        _updating = true;
        try
        {
            AppearanceSlider.Value = (int)appearance;
            AppearanceStatusText.Text = $"Current: {DisplayName(appearance)}";
            SetSelected(SuperBrightButton, appearance == HavenUiAppearance.SuperBright);
            SetSelected(BrightButton, appearance == HavenUiAppearance.Bright);
            SetSelected(DarkButton, appearance == HavenUiAppearance.Dark);
            SetSelected(SuperDarkButton, appearance == HavenUiAppearance.SuperDark);
        }
        finally
        {
            _updating = false;
        }
    }

    private static HavenUiAppearance FromSlider(double value) =>
        (HavenUiAppearance)Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 0, 3);

    private static string DisplayName(HavenUiAppearance appearance) => appearance switch
    {
        HavenUiAppearance.SuperBright => "Super Bright",
        HavenUiAppearance.Bright => "Bright",
        HavenUiAppearance.Dark => "Dark",
        HavenUiAppearance.SuperDark => "Super Dark",
        _ => throw new ArgumentOutOfRangeException(nameof(appearance), appearance, null)
    };

    private static void SetSelected(Button button, bool selected)
    {
        button.Classes.Set("accent", selected);
        button.IsDefault = selected;
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_preferences is null || !_subscribed) return;
        _preferences.AppearanceChanged -= OnAppearanceChanged;
        _subscribed = false;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e) => Subscribe();

    private void Subscribe()
    {
        if (_preferences is null || _subscribed) return;
        _preferences.AppearanceChanged += OnAppearanceChanged;
        _subscribed = true;
    }
}

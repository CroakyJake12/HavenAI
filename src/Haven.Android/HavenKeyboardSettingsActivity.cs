// Where:    src/Haven.Android/HavenKeyboardSettingsActivity.cs
// What:     Standalone preferences screen for the Haven Keyboard IME.
// How:      Fully programmatic Android widgets (Switch/SeekBar/RadioGroup) that
//           write straight into the same "haven_keyboard" SharedPreferences the
//           IME reads live, so changes apply on the next field focus without any
//           restart or signalling channel between the two components.
// Why:      Resources/xml/method.xml declares this activity as the keyboard's
//           settingsActivity; the system input-method picker launches it directly.
//
// PRIVACY RULE: this screen shows only preference state; it never displays,
// stores or logs any typed content.

using Android.App;
using Android.Content.PM;
using Android.Content.Res;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;

// Pin Orientation to widgets so UiMode resolution above stays unambiguous.
using Orientation = Android.Widget.Orientation;

namespace Haven.Android;

/// <summary>
/// Preferences UI for the keyboard: AI actions, feedback, layout density,
/// one-handed mode, theme and long-press delay.
/// </summary>
[Activity(
    Label = "Haven Keyboard Settings",
    Exported = true,
    ConfigurationChanges =
        ConfigChanges.Orientation
        | ConfigChanges.ScreenSize
        | ConfigChanges.SmallestScreenSize
        | ConfigChanges.ScreenLayout
        | ConfigChanges.UiMode)]
public sealed class HavenKeyboardSettingsActivity : Activity
{
    private HavenKeyboardSettings? _settings;
    private KeyboardPalette _palette = KeyboardTheme.Resolve(KeyboardThemeMode.FollowSystem, false);
    private readonly List<TextView> _textViewsNeedingColour = [];
    private readonly List<Switch> _switches = [];

    private HavenKeyboardSettings Settings => _settings ??= new HavenKeyboardSettings(this);

    /// <inheritdoc/>
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        _palette = KeyboardTheme.Resolve(Settings.ThemeMode, IsSystemNightMode());

        var scroll = new ScrollView(this);
        var root = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
        };
        root.SetPadding(Dp(20), Dp(24), Dp(20), Dp(24));
        scroll.AddView(root);

        AddHeading(root, "Haven Keyboard");
        AddSummary(root, "Offline-first keyboard. Suggestions come from a small built-in word list on this device. Nothing you type is stored or sent anywhere by the keyboard.");

        AddSection(root, "AI actions");
        AddToggle(
            root,
            "AI text actions",
            "Rewrite, fix grammar, shorten and change tone in supported fields.",
            Settings.AiEnabled,
            value => Settings.AiEnabled = value);
        AddToggle(
            root,
            "Allow cloud AI",
            "Consent flag for future cloud-backed executors. Unused while AI actions are off.",
            Settings.CloudAiAllowed,
            value => Settings.CloudAiAllowed = value);
        AddNote(root, "AI never runs in password fields, even when enabled.");

        AddSection(root, "Feedback");
        AddToggle(
            root,
            "Haptic feedback",
            "Short vibration on key presses.",
            Settings.HapticsEnabled,
            value => Settings.HapticsEnabled = value);
        AddToggle(
            root,
            "Key sounds",
            "Click sound on key presses.",
            Settings.SoundEnabled,
            value => Settings.SoundEnabled = value);

        AddSection(root, "Layout");
        AddToggle(
            root,
            "Always show number row",
            "Adds a digit row above the letters.",
            Settings.NumberRowAlways,
            value => Settings.NumberRowAlways = value);
        AddSlider(
            root,
            "Keyboard height",
            (int)(HavenKeyboardSettings.HeightScaleMinimum * 100),
            (int)(HavenKeyboardSettings.HeightScaleMaximum * 100),
            step: 1,
            current: (int)(Settings.HeightScale * 100),
            format: value => $"{value}%",
            onChange: value => Settings.HeightScale = value / 100f);
        AddRadioChoice(
            root,
            "One-handed mode",
            new[] { "Off", "Left", "Right" },
            (int)Settings.OneHandedMode,
            index => Settings.OneHandedMode = (KeyboardOneHandedMode)index);
        AddRadioChoice(
            root,
            "Theme",
            new[] { "Follow system", "Light", "Dark" },
            (int)Settings.ThemeMode,
            index => Settings.ThemeMode = (KeyboardThemeMode)index);

        AddSection(root, "Typing");
        AddSlider(
            root,
            "Long-press delay",
            HavenKeyboardSettings.LongPressDelayMinimum,
            HavenKeyboardSettings.LongPressDelayMaximum,
            step: 20,
            current: Settings.LongPressDelayMs,
            format: value => $"{value} ms",
            onChange: value => Settings.LongPressDelayMs = value);

        ApplyColours();
        SetContentView(scroll);
    }

    private bool IsSystemNightMode()
    {
        var uiMode = (int)(Resources?.Configuration?.UiMode ?? 0);
        return (uiMode & (int)UiMode.NightMask) == (int)UiMode.NightYes;
    }

    private void AddHeading(LinearLayout root, string text)
    {
        var heading = new TextView(this) { Text = text };
        heading.TextSize = 22f;
        _textViewsNeedingColour.Add(heading);
        root.AddView(heading, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent)
        {
            BottomMargin = Dp(6),
        });
    }

    private void AddSummary(LinearLayout root, string text)
    {
        var summary = new TextView(this) { Text = text };
        summary.TextSize = 13f;
        _textViewsNeedingColour.Add(summary);
        root.AddView(summary, SectionParameters(bottomMargin: Dp(4)));
    }

    private void AddSection(LinearLayout root, string title)
    {
        var section = new TextView(this) { Text = title };
        section.TextSize = 14f;
        section.Typeface = Typeface.DefaultBold!;
        _textViewsNeedingColour.Add(section);
        root.AddView(section, SectionParameters(topMargin: Dp(26)));
    }

    private void AddNote(LinearLayout root, string text)
    {
        var note = new TextView(this) { Text = text };
        note.TextSize = 12f;
        _textViewsNeedingColour.Add(note);
        root.AddView(note, RowParameters());
    }

    private void AddToggle(LinearLayout root, string label, string description, bool isChecked, Action<bool> onChange)
    {
        var textColumn = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var title = new TextView(this) { Text = label };
        title.TextSize = 16f;
        _textViewsNeedingColour.Add(title);
        var detail = new TextView(this) { Text = description };
        detail.TextSize = 12f;
        _textViewsNeedingColour.Add(detail);
        textColumn.AddView(title);
        textColumn.AddView(detail);

        var toggle = new Switch(this) { Checked = isChecked };
        toggle.CheckedChange += (_, args) =>
        {
            // Persist immediately; the IME observes preferences live.
            onChange(args.IsChecked);
        };
        _switches.Add(toggle);

        var row = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
        };
        row.AddView(textColumn, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        row.AddView(toggle, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent,
            ViewGroup.LayoutParams.WrapContent));
        root.AddView(row, RowParameters());
    }

    private void AddSlider(
        LinearLayout root,
        string label,
        int minimum,
        int maximum,
        int step,
        int current,
        Func<int, string> format,
        Action<int> onChange)
    {
        var range = (maximum - minimum) / step;
        var caption = new TextView(this) { Text = $"{label}: {format(current)}" };
        caption.TextSize = 16f;
        _textViewsNeedingColour.Add(caption);

        var slider = new SeekBar(this)
        {
            Max = range,
            Progress = Math.Clamp((current - minimum) / step, 0, range),
        };
        slider.ProgressChanged += (_, args) =>
        {
            if (!args.FromUser)
            {
                return;
            }
            var value = minimum + (args.Progress * step);
            caption.Text = $"{label}: {format(value)}";
            onChange(value);
        };

        root.AddView(caption, RowParameters());
        root.AddView(slider, RowParameters());
    }

    private void AddRadioChoice(LinearLayout root, string label, IReadOnlyList<string> options, int selectedIndex, Action<int> onChange)
    {
        var caption = new TextView(this) { Text = label };
        caption.TextSize = 16f;
        _textViewsNeedingColour.Add(caption);
        root.AddView(caption, RowParameters());

        var group = new RadioGroup(this) { Orientation = Orientation.Vertical };
        var ids = new List<int>();
        foreach (var option in options)
        {
            var button = new RadioButton(this) { Text = option };
            button.Id = View.GenerateViewId();
            button.TextSize = 15f;
            _textViewsNeedingColour.Add(button);
            ids.Add(button.Id);
            group.AddView(button);
        }
        group.Check(ids[selectedIndex]);
        group.CheckedChange += (_, args) =>
        {
            var index = ids.IndexOf(args.CheckedId);
            if (index < 0)
            {
                return;
            }
            onChange(index);
            if (label == "Theme")
            {
                // Re-resolve palette colours immediately so the change is visible.
                Recreate();
            }
        };
        root.AddView(group, RowParameters());
    }

    private void ApplyColours()
    {
        Window?.DecorView?.SetBackgroundColor(_palette.Background);
        foreach (var textView in _textViewsNeedingColour)
        {
            textView.SetTextColor(_palette.KeyForeground);
        }
        foreach (var toggle in _switches)
        {
            toggle.SetTextColor(_palette.KeyForeground);
        }
    }

    private LinearLayout.LayoutParams SectionParameters(int? topMargin = null, int? bottomMargin = null)
    {
        return new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = topMargin ?? Dp(14),
            BottomMargin = bottomMargin ?? Dp(2),
        };
    }

    private LinearLayout.LayoutParams RowParameters()
    {
        return new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = Dp(6),
            BottomMargin = Dp(2),
        };
    }

    private int Dp(float value) => (int)((value * Resources!.DisplayMetrics!.Density) + 0.5f);
}

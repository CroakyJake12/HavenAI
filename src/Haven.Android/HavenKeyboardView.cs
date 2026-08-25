// Where:    src/Haven.Android/HavenKeyboardView.cs
// What:     Fully programmatic QWERTY keyboard surface (letters + symbols layers,
//           shift states, optional number row, repeat backspace) for the Haven IME.
// How:      Rows of custom KeyButton TextViews inside weighted LinearLayouts. Keys
//           act on ACTION_DOWN for responsiveness; backspace auto-repeats via a
//           Handler. All visual state derives from a KeyboardPalette; no AXAML,
//           no XML layouts, no host-app resources.
// Why:      android.inputmethodservice.KeyboardView is deprecated and its bindings
//           emit obsolete warnings under TreatWarningsAsErrors; a small custom view
//           keeps full control of theming, height scaling and one-handed padding.
//
// PRIVACY RULE: this view never reads field content and never logs anything.

using Android.Content;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;

namespace Haven.Android;

/// <summary>Shift state machine for the letter layer.</summary>
internal enum HavenShiftState
{
    /// <summary>All letters lowercase.</summary>
    Off = 0,

    /// <summary>Next letter capitalised, then returns to off.</summary>
    Single = 1,

    /// <summary>Caps lock engaged until tapped again.</summary>
    Locked = 2,
}

/// <summary>
/// The typing surface of the Haven keyboard. Raises semantic events
/// (character requested, space pressed, ...) and owns no input-connection logic.
/// </summary>
internal sealed class HavenKeyboardView : LinearLayout
{
    private const float RowHeightBaseDp = 52f;
    private const float KeyGapDp = 2.5f;
    private const float CornerRadiusDp = 7f;

    private readonly List<LetterKey> _letterKeys = [];

    private KeyboardPalette _palette;
    private float _heightScale = 1f;
    private bool _hapticsEnabled = true;
    private bool _soundEnabled;
    private int _longPressDelayMs = 300;
    private bool _numberRowVisible;
    private bool _symbolsLayer;
    private HavenShiftState _shiftState;
    private KeyButton? _shiftButton;
    private KeyButton? _enterButton;
    private string _enterLabel = "Enter";

    /// <summary>Raised when a printable character should be committed.</summary>
    internal event Action<char>? CharacterRequested;

    /// <summary>Raised when backspace should delete one unit.</summary>
    internal event Action? BackspacePressed;

    /// <summary>Raised when the shift key is tapped.</summary>
    internal event Action? ShiftPressed;

    /// <summary>Raised when the ?123/ABC layer toggle is tapped.</summary>
    internal event Action? LayerTogglePressed;

    /// <summary>Raised when the space bar is tapped.</summary>
    internal event Action? SpacePressed;

    /// <summary>Raised when the enter/action key is tapped.</summary>
    internal event Action? EnterPressed;

    /// <summary>Creates the keyboard surface for the given palette and settings.</summary>
    internal HavenKeyboardView(Context context, KeyboardPalette palette)
        : base(context)
    {
        _palette = palette;
        Orientation = Orientation.Vertical;
        Build();
    }

    /// <summary>Current shift state (also drives letter labels).</summary>
    internal HavenShiftState ShiftState => _shiftState;

    /// <summary>True while the symbols layer is displayed.</summary>
    internal bool SymbolsLayerVisible => _symbolsLayer;

    /// <summary>Applies settings/palette; rebuilds rows when anything visual changed.</summary>
    internal void UpdateConfiguration(
        KeyboardPalette palette,
        float heightScale,
        bool hapticsEnabled,
        bool soundEnabled,
        int longPressDelayMs,
        bool numberRowAlways)
    {
        var rebuild = !_palette.Equals(palette)
            || Math.Abs(_heightScale - heightScale) > 0.001f
            || _numberRowVisible != numberRowAlways;
        _palette = palette;
        _heightScale = Math.Clamp(heightScale, 0.7f, 1.4f);
        _hapticsEnabled = hapticsEnabled;
        _soundEnabled = soundEnabled;
        _longPressDelayMs = longPressDelayMs;
        _numberRowVisible = numberRowAlways;
        if (rebuild)
        {
            Build();
        }
    }

    /// <summary>Sets the label shown on the enter key from the editor's imeOptions.</summary>
    internal void SetEnterLabel(string label)
    {
        _enterLabel = label;
        if (_enterButton is not null)
        {
            _enterButton.Text = label;
        }
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromWindow()
    {
        // Key buttons clear their own repeat timers on detach.
        base.OnDetachedFromWindow();
    }

    private int RowHeightPx => (int)((RowHeightBaseDp * _heightScale * Context!.Resources!.DisplayMetrics!.Density) + 0.5f);

    private int GapPx => (int)((KeyGapDp * Context!.Resources!.DisplayMetrics!.Density) + 0.5f);

    private void Build()
    {
        RemoveAllViews();
        _letterKeys.Clear();

        if (_symbolsLayer)
        {
            AddSymbolRows();
        }
        else
        {
            if (_numberRowVisible)
            {
                AddDigitRow();
            }
            AddLetterRows();
        }
        AddBottomRow();
    }

    private LinearLayout MakeRow()
    {
        var row = new LinearLayout(Context!)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new LayoutParams(ViewGroup.LayoutParams.MatchParent, RowHeightPx),
        };
        return row;
    }

    private KeyButton MakeKey(LinearLayout row, string label, float weight, string contentDescription, Action onPressed, Action? onReleased = null, bool autoRepeat = false)
    {
        var button = new KeyButton(
            this,
            onPressed,
            onReleased,
            autoRepeat)
        {
            Text = label,
            Gravity = GravityFlags.Center,
            ContentDescription = contentDescription,
            ImportantForAccessibility = ImportantForAccessibility.Yes,
        };
        button.TextSize = 17f;
        button.SetTextColor(_palette.KeyForeground);
        button.Background = MakeKeyBackground(isModifier: weight > 1.4f);

        var parameters = new LayoutParams(0, ViewGroup.LayoutParams.MatchParent, weight);
        parameters.LeftMargin = GapPx / 2;
        parameters.RightMargin = GapPx / 2;
        row.AddView(button, parameters);
        return button;
    }

    private Drawable MakeKeyBackground(bool isModifier)
    {
        var drawable = new GradientDrawable();
        drawable.SetShape(ShapeType.Rectangle);
        drawable.SetCornerRadius(CornerRadiusDp * Context!.Resources!.DisplayMetrics!.Density);
        drawable.SetColor(isModifier ? _palette.ModifierBackground : _palette.KeyBackground);
        return drawable;
    }

    private void AddDigitRow()
    {
        var row = MakeRow();
        foreach (var digit in new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" })
        {
            MakeKey(row, digit, 1f, digit, () => CommitCharacter(digit[0]));
        }
        AddView(row);
    }

    private void AddLetterRows()
    {
        var row1 = MakeRow();
        foreach (var letter in "qwertyuiop")
        {
            AddLetterKey(row1, letter, 1f);
        }
        AddView(row1);

        var row2 = MakeRow();
        foreach (var letter in "asdfghjkl")
        {
            AddLetterKey(row2, letter, 1f);
        }
        AddView(row2);

        var row3 = MakeRow();
        _shiftButton = MakeKey(row3, ShiftLabel(), 1.5f, "Shift", OnShiftTapped);
        foreach (var letter in "zxcvbnm")
        {
            AddLetterKey(row3, letter, 1f);
        }
        MakeKey(row3, "\u232B", 1.5f, "Backspace", RaiseBackspace, null, autoRepeat: true);
        AddView(row3);
    }

    private void AddLetterKey(LinearLayout row, char lower, float weight)
    {
        var captured = lower;
        var button = MakeKey(
            row,
            _shiftState == HavenShiftState.Off ? captured.ToString() : char.ToUpperInvariant(captured).ToString(),
            weight,
            $"Key {captured}",
            () => CommitCharacter(_shiftState == HavenShiftState.Off ? captured : char.ToUpperInvariant(captured)));
        _letterKeys.Add(new LetterKey(button, captured));
    }

    private void AddSymbolRows()
    {
        var row1 = MakeRow();
        foreach (var symbol in "1234567890")
        {
            MakeKey(row1, symbol.ToString(), 1f, symbol.ToString(), () => CommitCharacter(symbol));
        }
        AddView(row1);

        var row2 = MakeRow();
        foreach (var symbol in "@#$%&-+()")
        {
            MakeKey(row2, symbol.ToString(), 1f, symbol.ToString(), () => CommitCharacter(symbol));
        }
        AddView(row2);

        var row3 = MakeRow();
        foreach (var symbol in "*\"':;!?")
        {
            MakeKey(row3, symbol.ToString(), 1f, symbol.ToString(), () => CommitCharacter(symbol));
        }
        MakeKey(row3, "\u232B", 1.5f, "Backspace", RaiseBackspace, null, autoRepeat: true);
        AddView(row3);
    }

    private void AddBottomRow()
    {
        var row = MakeRow();
        MakeKey(row, _symbolsLayer ? "ABC" : "?123", 1.5f, _symbolsLayer ? "Letters" : "Numbers and symbols", OnLayerToggle);
        MakeKey(row, ",", 1f, "Comma", () => CommitPunctuation(","));
        MakeKey(row, "\u2423", 4f, "Space", RaiseSpace);
        MakeKey(row, ".", 1f, "Period", () => CommitPunctuation("."));
        _enterButton = MakeKey(row, _enterLabel, 1.5f, "Enter or action", RaiseEnter);
        AddView(row);
    }

    private void CommitCharacter(char character)
    {
        CharacterRequested?.Invoke(character);
        ConsumeSingleShift();
    }

    private void CommitPunctuation(string punctuation)
    {
        // Punctuation finalises any word; the service handles composition.
        CharacterRequested?.Invoke(punctuation[0]);
        ResetVisualShiftAfterWordEnd();
    }

    private void OnShiftTapped()
    {
        _shiftState = _shiftState switch
        {
            HavenShiftState.Off => HavenShiftState.Single,
            HavenShiftState.Single => HavenShiftState.Locked,
            _ => HavenShiftState.Off,
        };
        RefreshShiftLabels();
        ShiftPressed?.Invoke();
    }

    private void OnLayerToggle()
    {
        _symbolsLayer = !_symbolsLayer;
        Build();
        LayerTogglePressed?.Invoke();
    }

    private void ConsumeSingleShift()
    {
        if (_shiftState == HavenShiftState.Single)
        {
            _shiftState = HavenShiftState.Off;
            RefreshShiftLabels();
        }
    }

    private void ResetVisualShiftAfterWordEnd() => ConsumeSingleShift();

    private void RaiseBackspace()
    {
        BackspacePressed?.Invoke();
    }

    private void RaiseSpace() => SpacePressed?.Invoke();

    private void RaiseEnter() => EnterPressed?.Invoke();

    private string ShiftLabel() => _shiftState switch
    {
        HavenShiftState.Single => "SHIFT",
        HavenShiftState.Locked => "CAPS",
        _ => "shift",
    };

    private void RefreshShiftLabels()
    {
        if (_shiftButton is not null)
        {
            _shiftButton.Text = ShiftLabel();
        }
        foreach (var letter in _letterKeys)
        {
            letter.Button.Text = (_shiftState == HavenShiftState.Off
                ? letter.Lower
                : char.ToUpperInvariant(letter.Lower)).ToString();
        }
    }

    private sealed record LetterKey(KeyButton Button, char Lower);

    /// <summary>
    /// A single key. Commits on ACTION_DOWN (with haptic/sound feedback) and
    /// optionally auto-repeats while held (backspace). Repeat timers use the
    /// view's own message queue so they die with the window.
    /// </summary>
    private sealed class KeyButton : TextView
    {
        private const int RepeatIntervalMs = 55;

        // View.PlaySoundEffect value for the standard key click (SoundEffectConstants
        // has no bound Click member; CLICK == 0 in the platform constants).
        private const int SoundEffectClick = 0;

        private readonly HavenKeyboardView _owner;
        private readonly Action _onPressed;
        private readonly Action? _onReleased;
        private readonly bool _autoRepeat;
        private bool _pointerDown;

        internal KeyButton(
            HavenKeyboardView owner,
            Action onPressed,
            Action? onReleased,
            bool autoRepeat)
            : base(owner.Context!)
        {
            _owner = owner;
            _onPressed = onPressed;
            _onReleased = onReleased;
            _autoRepeat = autoRepeat;
        }

        public override bool OnTouchEvent(MotionEvent? e)
        {
            if (e is not null)
            {
                switch (e.ActionMasked)
                {
                    case MotionEventActions.Down:
                        _pointerDown = true;
                        Alpha = 0.72f;
                        Feedback();
                        _onPressed();
                        if (_autoRepeat)
                        {
                            ScheduleTick(_owner._longPressDelayMs);
                        }
                        break;
                    case MotionEventActions.Up:
                    case MotionEventActions.Cancel:
                        ReleasePointer();
                        break;
                }
            }
            return true;
        }

        protected override void OnDetachedFromWindow()
        {
            RemoveCallbacks(Tick);
            base.OnDetachedFromWindow();
        }

        private void ReleasePointer()
        {
            if (!_pointerDown)
            {
                return;
            }
            _pointerDown = false;
            Alpha = 1f;
            RemoveCallbacks(Tick);
            _onReleased?.Invoke();
        }

        private void Feedback()
        {
            // Haptics honour both the user setting and the system-wide setting
            // (PerformHapticFeedback consults the global haptic preference).
            if (_owner._hapticsEnabled)
            {
                _ = PerformHapticFeedback(FeedbackConstants.KeyboardTap);
            }
            if (_owner._soundEnabled)
            {
                PlaySoundEffect(SoundEffectClick);
            }
        }

        private void ScheduleTick(int delayMs)
        {
            RemoveCallbacks(Tick);
            PostDelayed(Tick, delayMs);
        }

        private void Tick()
        {
            if (!_pointerDown)
            {
                return;
            }
            _onPressed();
            PostDelayed(Tick, RepeatIntervalMs);
        }
    }
}

// Where:    src/Haven.Android/HavenKeyboardStrip.cs
// What:     Suggestion strip for the Haven IME: up to three candidates (middle slot
//           is always the literal typed word), secure-field lock indicator, AI
//           actions panel with honest inline status text, and the dismissible
//           "Add to calendar?" nudge chip.
// How:      Programmatic LinearLayouts of TextView chips. The strip raises events;
//           HavenImeService decides what insertion/correction to perform.
// Why:      Keeps input logic in the service and pure presentation here, mirroring
//           Haven's separation of state ownership from presentation.
//
// PRIVACY RULE: this view only renders strings handed to it and never logs them.
//   In secure fields the service suppresses AI affordances and shows the lock.

using Android.Content;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;

namespace Haven.Android;

/// <summary>One tappable strip slot.</summary>
/// <param name="Text">Text shown on the chip.</param>
/// <param name="IsLiteralWord">True when the chip is the raw typed word.</param>
internal sealed record StripSlot(string Text, bool IsLiteralWord);

/// <summary>
/// Horizontal suggestion/status bar shown above the keyboard rows.
/// </summary>
internal sealed class HavenKeyboardStripView : LinearLayout
{
    private const float StripHeightDp = 46f;

    private readonly TextView _lockIndicator;
    private readonly TextView[] _slots = new TextView[3];
    private readonly TextView _aiButton;
    private readonly List<TextView> _aiActionChips = [];
    private readonly LinearLayout _secondRow;
    private readonly TextView _statusText;
    private readonly LinearLayout _aiActionsRow;
    private readonly LinearLayout _nudgeRow;
    private readonly TextView _nudgeLabel;
    private readonly TextView _nudgeAccept;
    private readonly TextView _nudgeDismiss;

    private KeyboardPalette _palette;
    private StripSlot?[] _currentSlots = new StripSlot?[3];
    private bool _aiAvailable;
    private string? _aiHint;
    private bool _aiPanelOpen;
    private HavenCalendarNudge? _activeNudge;

    /// <summary>Raised when a candidate/literal slot is tapped.</summary>
    internal event Action<StripSlot>? SlotTapped;

    /// <summary>Raised when the AI chip is tapped.</summary>
    internal event Action? AiButtonTapped;

    /// <summary>Raised when an AI action button inside the panel is tapped.</summary>
    internal event Action<HavenKeyboardAiAction>? AiActionTapped;

    /// <summary>Raised when the user accepts the calendar nudge.</summary>
    internal event Action<HavenCalendarNudge>? NudgeAccepted;

    /// <summary>Raised when the user dismisses the nudge chip.</summary>
    internal event Action? NudgeDismissed;

    /// <summary>Builds the strip bound to a palette.</summary>
    internal HavenKeyboardStripView(Context context, KeyboardPalette palette)
        : base(context)
    {
        _palette = palette;
        Orientation = Orientation.Vertical;

        // LinearLayout.Gravity is get-only in the bindings; vertical centring of
        // wrap-content chips is applied per child via LayoutParameters.Gravity.
        var firstRow = new LinearLayout(Context!)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                (int)((StripHeightDp * Context!.Resources!.DisplayMetrics!.Density) + 0.5f)),
        };

        _lockIndicator = MakeChip(firstRow, "\uD83D\uDD12", weight: 0f);
        _lockIndicator.Visibility = ViewStates.Gone;

        for (var index = 0; index < _slots.Length; index++)
        {
            var slotIndex = index;
            var slot = MakeChip(firstRow, string.Empty, weight: 1f);
            slot.Click += (_, _) => OnSlotClicked(slotIndex);
            _slots[index] = slot;
        }

        _aiButton = MakeChip(firstRow, "AI", weight: 0f);
        _aiButton.SetTextColor(_palette.OnAccent);
        _aiButton.Background = Rounded(_palette.Accent, Dp(16));
        _aiButton.Click += (_, _) => OnAiButtonClicked();

        AddView(firstRow);

        _secondRow = new LinearLayout(Context!)
        {
            Orientation = Orientation.Horizontal,
            Visibility = ViewStates.Gone,
            LayoutParameters = new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent),
        };

        _statusText = new TextView(Context!)
        {
            Gravity = GravityFlags.CenterVertical,
            Visibility = ViewStates.Gone,
        };
        _statusText.TextSize = 13f;
        _statusText.SetPadding(Dp(10), Dp(6), Dp(10), Dp(6));
        _secondRow.AddView(_statusText, new LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        _aiActionsRow = new LinearLayout(Context!) { Orientation = Orientation.Horizontal, Visibility = ViewStates.Gone };
        AddAiAction("Rewrite", HavenKeyboardAiAction.Rewrite);
        AddAiAction("Grammar", HavenKeyboardAiAction.FixGrammar);
        AddAiAction("Shorten", HavenKeyboardAiAction.Shorten);
        AddAiAction("Formal", HavenKeyboardAiAction.ToneFormal);
        AddAiAction("Casual", HavenKeyboardAiAction.ToneFriendly);
        _secondRow.AddView(_aiActionsRow);

        _nudgeRow = new LinearLayout(Context!) { Orientation = Orientation.Horizontal, Visibility = ViewStates.Gone };
        _nudgeLabel = MakeChip(_nudgeRow, string.Empty, weight: 1f);
        _nudgeAccept = MakeChip(_nudgeRow, "Add", weight: 0f);
        _nudgeAccept.SetTextColor(_palette.OnAccent);
        _nudgeAccept.Background = Rounded(_palette.Accent, Dp(14));
        _nudgeAccept.Click += (_, _) =>
        {
            if (_activeNudge is { } nudge)
            {
                NudgeAccepted?.Invoke(nudge);
            }
        };
        _nudgeDismiss = MakeChip(_nudgeRow, "\u2715", weight: 0f);
        _nudgeDismiss.ContentDescription = "Dismiss suggestion";
        _nudgeDismiss.Click += (_, _) =>
        {
            HideNudge();
            NudgeDismissed?.Invoke();
        };
        _secondRow.AddView(_nudgeRow);

        AddView(_secondRow);
        ApplyTheme();
    }

    /// <summary>
    /// Updates candidate slots. Slot layout: [best completion] [typed word]
    /// [second completion]; the middle literal slot always appears while a word is
    /// being typed so users can insert exactly what they keyed.
    /// </summary>
    internal void UpdateCandidates(
        IReadOnlyList<string> completions,
        string? typedWord,
        bool secureField,
        bool incognitoField,
        bool aiAvailable,
        string? aiHint)
    {
        _aiAvailable = aiAvailable;
        _aiHint = aiHint;
        _currentSlots = new StripSlot?[3];

        if (!string.IsNullOrEmpty(typedWord))
        {
            _currentSlots[1] = new StripSlot(typedWord, IsLiteralWord: true);
            if (completions.Count > 0)
            {
                _currentSlots[0] = new StripSlot(completions[0], IsLiteralWord: false);
            }
            if (completions.Count > 1)
            {
                _currentSlots[2] = new StripSlot(completions[1], IsLiteralWord: false);
            }
        }
        else if (completions.Count > 0)
        {
            // No active word: show any standing candidates without a literal slot.
            _currentSlots[0] = new StripSlot(completions[0], IsLiteralWord: false);
            if (completions.Count > 1)
            {
                _currentSlots[2] = new StripSlot(completions[1], IsLiteralWord: false);
            }
        }

        for (var index = 0; index < _slots.Length; index++)
        {
            var current = _currentSlots[index];
            _slots[index].Text = current?.Text ?? string.Empty;
            _slots[index].Visibility = current is null ? ViewStates.Invisible : ViewStates.Visible;
        }

        _lockIndicator.Visibility = secureField ? ViewStates.Visible : ViewStates.Gone;
        _lockIndicator.ContentDescription = secureField && incognitoField
            ? "Secure field. AI and personalisation are disabled."
            : "Secure field. AI actions are disabled.";

        RefreshAiChip();
    }

    /// <summary>Shows a status message inline (e.g. "AI unavailable") for a few seconds.</summary>
    internal void ShowStatus(string message, bool transientHide = true)
    {
        OpenSecondRow();
        _statusText.Text = message;
        _statusText.Visibility = ViewStates.Visible;
        if (transientHide)
        {
            RemoveCallbacks(HideStatus);
            PostDelayed(HideStatus, 3500);
        }
    }

    /// <summary>Closes the AI action panel and hides any transient status text.</summary>
    internal void CloseAiPanel()
    {
        _aiPanelOpen = false;
        _aiActionsRow.Visibility = ViewStates.Gone;
        _statusText.Visibility = ViewStates.Gone;
        RemoveCallbacks(HideStatus);
        CloseSecondRowIfIdle();
        RefreshAiChip();
    }

    /// <summary>Hides the nudge chip.</summary>
    internal void HideNudge()
    {
        _activeNudge = null;
        _nudgeRow.Visibility = ViewStates.Gone;
        CloseSecondRowIfIdle();
    }

    /// <summary>Shows the dismissible "Add to calendar?" nudge chip.</summary>
    internal void ShowNudge(HavenCalendarNudge nudge)
    {
        _activeNudge = nudge;
        _nudgeLabel.Text = "Add \u201C" + nudge.Title + "\u201D to calendar?";
        _nudgeRow.Visibility = ViewStates.Visible;
        OpenSecondRow();
    }

    /// <summary>True when the nudge chip currently holds a suggestion.</summary>
    internal bool IsShowingNudge => _activeNudge is not null;

    /// <inheritdoc/>
    protected override void OnDetachedFromWindow()
    {
        RemoveCallbacks(HideStatus);
        base.OnDetachedFromWindow();
    }

    private void OnSlotClicked(int index)
    {
        if (_currentSlots[index] is { } slot)
        {
            SlotTapped?.Invoke(slot);
        }
    }

    private void OnAiButtonClicked()
    {
        if (!_aiAvailable)
        {
            ShowStatus(_aiHint ?? "AI unavailable");
            return;
        }
        _aiPanelOpen = !_aiPanelOpen;
        _aiActionsRow.Visibility = _aiPanelOpen ? ViewStates.Visible : ViewStates.Gone;
        if (_aiPanelOpen)
        {
            OpenSecondRow();
        }
        else
        {
            CloseSecondRowIfIdle();
        }
        AiButtonTapped?.Invoke();
    }

    private void AddAiAction(string label, HavenKeyboardAiAction action)
    {
        var chip = MakeChip(_aiActionsRow, label, weight: 0f);
        _aiActionChips.Add(chip);
        chip.Click += (_, _) =>
        {
            if (_aiPanelOpen)
            {
                AiActionTapped?.Invoke(action);
            }
        };
    }

    private void HideStatus()
    {
        _statusText.Visibility = ViewStates.Gone;
        CloseSecondRowIfIdle();
    }

    private void OpenSecondRow() => _secondRow.Visibility = ViewStates.Visible;

    private void CloseSecondRowIfIdle()
    {
        if (!_aiPanelOpen && _statusText.Visibility != ViewStates.Visible && _nudgeRow.Visibility != ViewStates.Visible)
        {
            _secondRow.Visibility = ViewStates.Gone;
        }
    }

    private void RefreshAiChip()
    {
        _aiButton.Alpha = _aiAvailable ? 1f : 0.4f;
        _aiButton.ContentDescription = _aiAvailable
            ? "AI text actions"
            : _aiHint ?? "AI unavailable";
    }

    private TextView MakeChip(LinearLayout row, string text, float weight)
    {
        var chip = new TextView(Context!)
        {
            Text = text,
            Gravity = GravityFlags.Center,
        };
        chip.TextSize = 15f;
        chip.SetPadding(Dp(12), Dp(4), Dp(12), Dp(4));
        if (weight > 0f)
        {
            row.AddView(chip, new LayoutParams(0, ViewGroup.LayoutParams.MatchParent, weight));
        }
        else
        {
            row.AddView(chip, new LayoutParams(
                ViewGroup.LayoutParams.WrapContent,
                ViewGroup.LayoutParams.WrapContent)
            {
                Gravity = GravityFlags.CenterVertical,
            });
        }
        return chip;
    }

    private void ApplyTheme()
    {
        SetBackgroundColor(_palette.StripBackground);
        foreach (var slot in _slots)
        {
            slot.SetTextColor(_palette.KeyForeground);
        }
        foreach (var chip in _aiActionChips)
        {
            chip.SetTextColor(_palette.KeyForeground);
            chip.Background = Rounded(_palette.ModifierBackground, Dp(14));
        }
        _statusText.SetTextColor(_palette.KeyForegroundDim);
        _nudgeLabel.SetTextColor(_palette.KeyForeground);
    }

    private int Dp(float value) => (int)((value * Context!.Resources!.DisplayMetrics!.Density) + 0.5f);

    private static Drawable Rounded(global::Android.Graphics.Color color, float cornerRadiusPx)
    {
        var drawable = new GradientDrawable();
        drawable.SetShape(ShapeType.Rectangle);
        drawable.SetCornerRadius(cornerRadiusPx);
        drawable.SetColor(color);
        return drawable;
    }
}

// Where:    src/Haven.Android/HavenImeService.cs
// What:     The Haven Keyboard input method editor (IME): offline QWERTY typing,
//           dictionary suggestions, autocorrect-on-space, secure-field protection,
//           opt-in AI actions and a local calendar nudge chip.
// How:      An [Service]-declared InputMethodService bound with BIND_INPUT_METHOD,
//           advertising android.view.InputMethod and the @xml/method metadata.
//           Composition uses InputConnection.SetComposingText; corrections replace
//           the composing region; AI results are applied inside batch edits.
// Why:      Haven needs a first-party keyboard that works fully offline with zero
//           AI, never leaks what the user types, and only routes text off-device
//           through an explicit user-initiated AI action on a non-secure field.
//
// ============================================================================
// PRIVACY RULE (READ BEFORE EDITING THIS FILE):
//   1. NEVER log keystrokes, composing words, selections, field content or AI
//      results. There is intentionally NO logging anywhere in the IME.
//   2. Secure fields (password / visible password / web password / number
//      password) get: NO AI actions, lock indicator, and identical local-only
//      handling. Static-dictionary suggestions remain available because they
//      involve no network and no learning; there is deliberately no history or
//      personalisation store anywhere in this IME to disable.
//   3. IME_FLAG_NO_PERSONALIZED_LEARNING is honoured trivially: this keyboard
//      keeps no personalised state at all, so the flag adds no behaviour beyond
//      documentation. If learning is ever introduced, gate it on this flag AND
//      remove that claim from this comment.
//   4. The ONLY network path is HavenKeyboardAiController -> the executor wired
//      at bootstrap, triggered solely by an explicit tap on an AI action while
//      the field is non-secure, AI is enabled in settings and the network is up.
// ============================================================================

using System.Globalization;
using System.Text;
using Android.App;
using Android.Content;
using Android.Content.Res;
using Android.Net;
using Android.Provider;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using Android.InputMethodServices;

// The IME mixes Android.Widget and Android.Content.Res; pin Orientation to widgets.
using Orientation = Android.Widget.Orientation;

namespace Haven.Android;

/// <summary>
/// Haven Keyboard: an offline-first soft keyboard for Android with optional,
/// explicitly user-triggered AI text actions.
/// </summary>
[Service(
    Name = "com.cakemods.haven.HavenImeService",
    Label = "Haven Keyboard",
    Permission = "android.permission.BIND_INPUT_METHOD",
    Exported = true)]
[IntentFilter(new[] { "android.view.InputMethod" })]
[MetaData("android.view.im", Resource = "@xml/method")]
public sealed class HavenImeService : InputMethodService
{
    /// <summary>Low byte of imeOptions holding the enter-key action (EditorInfo.IME_MASK_ACTION).</summary>
    private const int EnterActionMask = 0xFF;

    /// <summary>EditorInfo.IME_FLAG_NO_PERSONALIZED_LEARNING (constant is stable pre-API 26).</summary>
    private const int FlagNoPersonalizedLearning = 0x10000000;

    private const int UnknownCursor = int.MinValue;
    private const int MaxSuggestions = 2;

    private readonly HavenKeyboardSuggestor _suggestor = new();
    private readonly StringBuilder _composing = new();
    private HavenKeyboardSettings? _settingsInstance;

    private HavenKeyboardStripView? _strip;
    private HavenKeyboardView? _keyboard;
    private KeyboardPalette _palette = KeyboardTheme.Resolve(KeyboardThemeMode.FollowSystem, false);
    private bool _lastNightMode;
    private FieldProfile _profile = new(IsSecure: false, Incognito: false, EnterAction: 0);
    private bool _trackingComposing;
    private int _expectedEnd = UnknownCursor;
    private CancellationTokenSource? _aiCts;
    private bool _aiBusy;
    private bool _aiAvailable;
    private string? _aiHint;
    private int _generation;
    private string? _shownNudgeKey;

    /// <summary>
    /// Lazily created preferences accessor. Context-backed services must not be
    /// used from inside the constructor, so this waits for first real use.
    /// </summary>
    private HavenKeyboardSettings Settings => _settingsInstance ??= new HavenKeyboardSettings(this);

    /// <summary>Immutable snapshot of how the current field must be treated.</summary>
    private sealed record FieldProfile(bool IsSecure, bool Incognito, int EnterAction);

    /// <summary>Source text captured for one AI action.</summary>
    private sealed record AiSource(string Source, int DeleteChars, bool IsSelection);

    /// <inheritdoc/>
    public override void OnCreate()
    {
        base.OnCreate();
        // No initialisation touches user content; nothing about fields is ever read here.
    }

    /// <inheritdoc/>
    public override View? OnCreateInputView()
    {
        StartNewGeneration();
        return BuildRootView();
    }

    /// <inheritdoc/>
    public override void OnStartInput(EditorInfo? attribute, bool restarting)
    {
        base.OnStartInput(attribute, restarting);
        StartNewGeneration();
        _profile = ComputeProfile(attribute);
    }

    /// <inheritdoc/>
    public override void OnStartInputView(EditorInfo? info, bool restarting)
    {
        base.OnStartInputView(info, restarting);
        StartNewGeneration();
        RefreshThemeIfChanged();
        _keyboard?.UpdateConfiguration(
            _palette,
            Settings.HeightScale,
            Settings.HapticsEnabled,
            Settings.SoundEnabled,
            Settings.LongPressDelayMs,
            Settings.NumberRowAlways);
        ApplyFieldProfile(ComputeProfile(info));
    }

    /// <inheritdoc/>
    public override void OnUpdateSelection(
        int oldSelStart,
        int oldSelEnd,
        int newSelStart,
        int newSelEnd,
        int candidatesStart,
        int candidatesEnd)
    {
        base.OnUpdateSelection(oldSelStart, oldSelEnd, newSelStart, newSelEnd, candidatesStart, candidatesEnd);
        if (!_trackingComposing)
        {
            return;
        }

        // Abandon our word tracker when the framework dropped the composing
        // region or the cursor moved somewhere we did not put it.
        if (candidatesStart < 0 || (_expectedEnd != UnknownCursor && newSelEnd != _expectedEnd))
        {
            ResetTracking();
        }
        else
        {
            _expectedEnd = newSelEnd;
        }
        UpdateSuggestions();
    }

    /// <inheritdoc/>
    public override void OnFinishInput()
    {
        base.OnFinishInput();
        StartNewGeneration();
        _aiCts?.Cancel();
        ResetTracking();
        _strip?.CloseAiPanel();
        _strip?.HideNudge();
    }

    /// <inheritdoc/>
    public override void OnConfigurationChanged(Configuration? newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        if (_keyboard is not null)
        {
            RefreshThemeIfChanged();
        }
    }

    /// <inheritdoc/>
    public override void OnDestroy()
    {
        StartNewGeneration();
        _aiCts?.Cancel();
        _aiCts?.Dispose();
        _aiCts = null;
        base.OnDestroy();
    }

    // ------------------------------------------------------------------ view

    private View BuildRootView()
    {
        var nightMode = IsSystemNightMode();
        _lastNightMode = nightMode;
        _palette = KeyboardTheme.Resolve(Settings.ThemeMode, nightMode);

        var root = new FrameLayout(this);
        var column = new LinearLayout(this) { Orientation = Orientation.Vertical };

        // One-handed mode reserves 12% of the screen width on one side.
        var oneHanded = Settings.OneHandedMode;
        var sidePadding = oneHanded == KeyboardOneHandedMode.Off
            ? 0
            : (int)((Resources?.DisplayMetrics?.WidthPixels ?? 1080) * 0.12f);
        var layout = new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent,
            GravityFlags.Bottom)
        {
            LeftMargin = oneHanded == KeyboardOneHandedMode.Left ? sidePadding : 0,
            RightMargin = oneHanded == KeyboardOneHandedMode.Right ? sidePadding : 0,
        };

        _strip = new HavenKeyboardStripView(this, _palette);
        _strip.SlotTapped += HandleSlotTapped;
        _strip.AiActionTapped += action => _ = RunAiActionAsync(action);
        _strip.NudgeAccepted += OpenCalendarForNudge;
        _strip.NudgeDismissed += () => _shownNudgeKey = null;
        column.AddView(_strip);

        _keyboard = new HavenKeyboardView(this, _palette);
        _keyboard.CharacterRequested += HandleCharacter;
        _keyboard.BackspacePressed += HandleBackspace;
        _keyboard.SpacePressed += HandleSpace;
        _keyboard.EnterPressed += HandleEnter;
        _keyboard.ShiftPressed += UpdateSuggestions;
        _keyboard.LayerTogglePressed += UpdateSuggestions;
        column.AddView(_keyboard);

        root.AddView(column, layout);
        return root;
    }

    private bool IsSystemNightMode()
    {
        var uiMode = (int)(Resources?.Configuration?.UiMode ?? 0);
        return (uiMode & (int)UiMode.NightMask) == (int)UiMode.NightYes;
    }

    private void RefreshThemeIfChanged()
    {
        var nightMode = IsSystemNightMode();
        var resolved = KeyboardTheme.Resolve(Settings.ThemeMode, nightMode);
        if (ReferenceEquals(resolved, _palette) && nightMode == _lastNightMode)
        {
            return;
        }
        var rebuilt = BuildRootView();
        SetInputView(rebuilt);
        ApplyFieldProfile(_profile);
        UpdateSuggestions();
    }

    // ------------------------------------------------------------- profiles

    private static FieldProfile ComputeProfile(EditorInfo? info)
    {
        if (info is null)
        {
            return new FieldProfile(IsSecure: false, Incognito: false, EnterAction: 0);
        }
        var inputType = info.InputType;
        var secure = IsSecureInput(inputType);
        var incognito = ((int)info.ImeOptions & FlagNoPersonalizedLearning) != 0;
        var enterAction = (int)info.ImeOptions & EnterActionMask;
        return new FieldProfile(secure, incognito, enterAction);
    }

    /// <summary>
    /// Password-class detection. Email addresses are deliberately NOT treated as
    /// secure: they contain no secret beyond the address itself.
    /// </summary>
    private static bool IsSecureInput(InputTypes inputType)
    {
        var variation = inputType & InputTypes.MaskVariation;
        return variation == InputTypes.TextVariationPassword
            || variation == InputTypes.TextVariationVisiblePassword
            || variation == InputTypes.TextVariationWebPassword
            || ((inputType & InputTypes.MaskClass) == InputTypes.ClassNumber
                && variation == InputTypes.NumberVariationPassword);
    }

    private void ApplyFieldProfile(FieldProfile profile)
    {
        _profile = profile;
        _shownNudgeKey = null;
        _strip?.HideNudge();
        _strip?.CloseAiPanel();
        ComputeAiAvailability();
        _keyboard?.SetEnterLabel(EnterLabel(profile.EnterAction));
        UpdateSuggestions();
    }

    private void ComputeAiAvailability()
    {
        _aiAvailable = HavenKeyboardAiController.IsConfigured
            && Settings.AiEnabled
            && !_profile.IsSecure
            && IsNetworkAvailable();
        _aiHint = !Settings.AiEnabled
            ? "AI is off - enable it in Haven Keyboard settings"
            : _profile.IsSecure
                ? "AI is disabled in secure fields"
                : !HavenKeyboardAiController.IsConfigured
                    ? "AI unavailable"
                    : !_aiAvailable ? "AI offline" : null;
    }

    private static string EnterLabel(int enterAction) => enterAction switch
    {
        2 => "Go",
        3 => "Search",
        4 => "Send",
        5 => "Next",
        6 => "Done",
        _ => "Enter",
    };

    // ------------------------------------------------------------ key input

    private void HandleCharacter(char character)
    {
        if (CurrentInputConnection is not { } connection)
        {
            return;
        }

        // Non-letter characters end any composing word and insert literally.
        if (!char.IsLetter(character))
        {
            CommitPunctuation(connection, character.ToString());
            return;
        }

        connection.BeginBatchEdit();
        try
        {
            if (!_trackingComposing)
            {
                ResetTracking();
                _trackingComposing = true;
            }
            _composing.Append(character);
            connection.SetComposingText(_composing.ToString(), 1);
            if (_expectedEnd != UnknownCursor)
            {
                _expectedEnd++;
            }
        }
        finally
        {
            connection.EndBatchEdit();
        }
        UpdateSuggestions();
    }

    private void CommitPunctuation(IInputConnection connection, string punctuation)
    {
        connection.BeginBatchEdit();
        try
        {
            connection.FinishComposingText();
            connection.CommitText(punctuation, 1);
        }
        finally
        {
            connection.EndBatchEdit();
        }
        ResetTracking();
        UpdateSuggestions();
        if (punctuation is "." or "!" or "?")
        {
            ScanForCalendarNudge();
        }
    }

    private void HandleBackspace()
    {
        if (CurrentInputConnection is not { } connection)
        {
            return;
        }

        connection.BeginBatchEdit();
        try
        {
            if (_trackingComposing && _composing.Length > 0)
            {
                _composing.Length--;
                if (_composing.Length == 0)
                {
                    connection.SetComposingText(string.Empty, 1);
                    connection.FinishComposingText();
                    ResetTracking();
                }
                else
                {
                    connection.SetComposingText(_composing.ToString(), 1);
                    if (_expectedEnd != UnknownCursor)
                    {
                        _expectedEnd--;
                    }
                }
            }
            else
            {
                connection.DeleteSurroundingText(1, 0);
            }
        }
        finally
        {
            connection.EndBatchEdit();
        }
        UpdateSuggestions();
    }

    private void HandleSpace()
    {
        if (CurrentInputConnection is not { } connection)
        {
            return;
        }

        var hadWord = _trackingComposing && _composing.Length > 0;
        string? replacement = null;
        if (hadWord && !_profile.IsSecure)
        {
            // Conservative autocorrect: only a pure prefix extension of exactly
            // what was typed may be substituted. Edit-distance candidates are
            // never applied automatically.
            replacement = _suggestor.TopPrefixCompletion(_composing.ToString());
        }

        connection.BeginBatchEdit();
        try
        {
            if (hadWord)
            {
                if (replacement is not null)
                {
                    connection.SetComposingText(
                        HavenKeyboardSuggestor.PreserveCase(_composing.ToString(), replacement),
                        1);
                }
                connection.FinishComposingText();
            }
            connection.CommitText(" ", 1);
        }
        finally
        {
            connection.EndBatchEdit();
        }
        ResetTracking();
        UpdateSuggestions();
        ScanForCalendarNudge();
    }

    private void HandleEnter()
    {
        if (CurrentInputConnection is not { } connection)
        {
            return;
        }

        connection.BeginBatchEdit();
        try
        {
            connection.FinishComposingText();
            if (_profile.EnterAction is >= 2 and <= 6)
            {
                // 2..6 maps onto ImeAction.Go/Search/Send/Next/Done.
                connection.PerformEditorAction((ImeAction)_profile.EnterAction);
            }
            else
            {
                connection.CommitText("\n", 1);
            }
        }
        finally
        {
            connection.EndBatchEdit();
        }
        ResetTracking();
        UpdateSuggestions();
    }

    private void HandleSlotTapped(StripSlot slot)
    {
        if (CurrentInputConnection is not { } connection)
        {
            return;
        }

        connection.BeginBatchEdit();
        try
        {
            if (slot.IsLiteralWord)
            {
                // Middle slot: insert exactly what the user keyed.
                connection.FinishComposingText();
            }
            else
            {
                var typed = _trackingComposing ? _composing.ToString() : null;
                if (!string.IsNullOrEmpty(typed))
                {
                    connection.SetComposingText(
                        HavenKeyboardSuggestor.PreserveCase(typed!, slot.Text),
                        1);
                }
                else
                {
                    connection.CommitText(slot.Text, 1);
                }
                connection.FinishComposingText();
            }
            connection.CommitText(" ", 1);
        }
        finally
        {
            connection.EndBatchEdit();
        }
        ResetTracking();
        UpdateSuggestions();
    }

    private void ResetTracking()
    {
        _composing.Clear();
        _trackingComposing = false;
        _expectedEnd = UnknownCursor;
    }

    private void UpdateSuggestions()
    {
        if (_strip is null)
        {
            return;
        }
        var word = _trackingComposing ? _composing.ToString() : null;
        IReadOnlyList<string> completions = Array.Empty<string>();
        if (!string.IsNullOrEmpty(word))
        {
            completions = _suggestor.Suggest(word!, MaxSuggestions);
        }
        _strip.UpdateCandidates(completions, word, _profile.IsSecure, _profile.Incognito, _aiAvailable, _aiHint);
    }

    // ------------------------------------------------------------ AI actions

    private async Task RunAiActionAsync(HavenKeyboardAiAction action)
    {
        if (_strip is null)
        {
            return;
        }
        if (_aiBusy || !_aiAvailable || CurrentInputConnection is not { } connection)
        {
            _strip.ShowStatus(_aiHint ?? "AI unavailable");
            return;
        }

        var generation = _generation;
        var captured = CaptureAiSource(connection);
        if (captured is null)
        {
            _strip.ShowStatus("Select or type some text first");
            return;
        }

        _aiBusy = true;
        _aiCts?.Cancel();
        _aiCts?.Dispose();
        _aiCts = new CancellationTokenSource();
        _strip.ShowStatus("Working\u2026", transientHide: false);

        var result = await HavenKeyboardAiController
            .RunAsync(action, captured.Source, message => PostStatus(generation, message), _aiCts.Token)
            .ConfigureAwait(false);

        _aiBusy = false;
        if (generation != _generation)
        {
            // Focus moved while generating: drop everything without touching the
            // (possibly different) field the keyboard is now attached to.
            return;
        }
        if (result is null)
        {
            return; // Controller already reported honest status text.
        }
        ApplyAiResult(generation, captured, result);
    }

    private AiSource? CaptureAiSource(IInputConnection connection)
    {
        string? selected = null;
        try
        {
            selected = connection.GetSelectedText(0);
        }
        catch
        {
            // Fields without selection support fall through to sentence capture.
        }
        if (!string.IsNullOrWhiteSpace(selected))
        {
            return new AiSource(selected.Trim(), DeleteChars: 0, IsSelection: true);
        }

        var before = connection.GetTextBeforeCursor(600, 0);
        if (string.IsNullOrWhiteSpace(before))
        {
            return null;
        }

        var trimmedEnd = before.TrimEnd();
        if (trimmedEnd.Length == 0)
        {
            return null;
        }
        var cut = 0;
        for (var index = trimmedEnd.Length - 1; index >= 0; index--)
        {
            var candidate = trimmedEnd[index];
            if (candidate is '.' or '!' or '?' or ';' or '\n')
            {
                cut = index + 1;
                break;
            }
        }
        var segment = before[cut..];
        var source = segment.Trim();
        if (source.Length < 2)
        {
            return null;
        }
        return new AiSource(source, before.Length - cut, IsSelection: false);
    }

    private void ApplyAiResult(int generation, AiSource captured, string result)
    {
        // Marshal onto the UI thread through the strip view; if the strip is
        // gone the field is gone and there is nothing to apply.
        _strip?.Post(() =>
        {
            if (generation != _generation || CurrentInputConnection is not { } connection)
            {
                return;
            }
            try
            {
                connection.BeginBatchEdit();
                try
                {
                    if (!captured.IsSelection)
                    {
                        connection.DeleteSurroundingText(captured.DeleteChars, 0);
                    }

                    // For selections CommitText replaces the selected range.
                    connection.CommitText(result, 1);
                }
                finally
                {
                    connection.EndBatchEdit();
                }
            }
            catch
            {
                PostStatus(generation, "Could not apply the result");
                return;
            }
            ResetTracking();
            UpdateSuggestions();
            PostStatus(generation, "Done");
        });
    }

    private void PostStatus(int generation, string message)
    {
        if (generation != _generation)
        {
            return;
        }
        _strip?.Post(() =>
        {
            if (generation == _generation)
            {
                _strip?.ShowStatus(message);
            }
        });
    }

    // -------------------------------------------------------- calendar nudge

    private void ScanForCalendarNudge()
    {
        if (_profile.IsSecure || _strip is null || _strip.IsShowingNudge)
        {
            return;
        }
        string recent = string.Empty;
        try
        {
            recent = CurrentInputConnection?.GetTextBeforeCursor(160, 0) ?? string.Empty;
        }
        catch
        {
            return;
        }
        var nudge = HavenKeyboardNudgeDetector.Detect(recent);
        if (nudge is null)
        {
            return;
        }
        var key = nudge.Title + "|" + nudge.BeginTime.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
        if (key == _shownNudgeKey)
        {
            return;
        }
        _shownNudgeKey = key;
        _strip.ShowNudge(nudge);
    }

    private void OpenCalendarForNudge(HavenCalendarNudge nudge)
    {
        try
        {
            using var intent = new Intent(Intent.ActionInsert);
            intent.SetData(CalendarContract.Events.ContentUri);
            intent.PutExtra("title", nudge.Title);
            intent.PutExtra("description", "Added from Haven Keyboard");

            // String keys mirror CalendarContract.EXTRA_EVENT_BEGIN_TIME / .TITLE
            // so no extra binding surface is required.
            intent.PutExtra("beginTime", nudge.BeginTime.ToUnixTimeMilliseconds());
            intent.AddFlags(ActivityFlags.NewTask);
            StartActivity(intent);
            _strip?.HideNudge();
        }
        catch
        {
            _strip?.ShowStatus("No calendar app found");
        }
    }

    // ---------------------------------------------------------------- helpers

    private bool IsNetworkAvailable()
    {
        try
        {
            if (GetSystemService(Context.ConnectivityService) is not ConnectivityManager connectivity
                || connectivity.ActiveNetwork is not { } network)
            {
                return false;
            }
            var capabilities = connectivity.GetNetworkCapabilities(network);
            return capabilities?.HasCapability(NetCapability.Validated) == true;
        }
        catch
        {
            return false;
        }
    }

    private void StartNewGeneration()
    {
        _generation++;
        _aiBusy = false;
    }
}

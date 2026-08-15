using System.Globalization;

namespace Haven.UI.Components;

public static class InputDefaults
{
    public const string SystemClass = "Input";
    public const string FocusTransition = "InputFocus";
}

public sealed class Input : HavenElement
{
    public static readonly HavenProperty<string> TextProperty = HavenPropertyRegistry.Register(new HavenProperty<string>("Input.Text", string.Empty));
    public static readonly HavenProperty<string> PlaceholderProperty = HavenPropertyRegistry.Register(new HavenProperty<string>("Input.Placeholder", string.Empty));
    public static readonly HavenProperty<bool> MultilineProperty = HavenPropertyRegistry.Register(new HavenProperty<bool>("Input.Multiline", false));
    public static readonly HavenProperty<int> CaretIndexProperty = HavenPropertyRegistry.Register(new HavenProperty<int>("Input.CaretIndex", 0));

    public Input()
    {
        Accessibility.Role = HavenAccessibleRole.Input;
        Accessibility.Focusable = true;
        SetValue(HavenProperties.Hover, true, HavenValueSource.Default);
        SetValue(HavenProperties.MinHeight, HavenLength.Px(48), HavenValueSource.Default);
        SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(24)), HavenValueSource.Default);
        SetValue(HavenProperties.Background, "SurfaceRaised", HavenValueSource.Default);
        SetValue(HavenProperties.Padding, HavenThickness.Parse("0px 18px"), HavenValueSource.Default);
        SetValue(HavenProperties.Transition, InputDefaults.FocusTransition, HavenValueSource.Default);
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set
        {
            var next = value ?? string.Empty;
            SetValue(TextProperty, next);
            SetValue(CaretIndexProperty, NormalizeCaret(next, GetValue(CaretIndexProperty)));
        }
    }

    public string Placeholder
    {
        get => GetValue(PlaceholderProperty);
        set
        {
            var next = value ?? string.Empty;
            SetValue(PlaceholderProperty, next);
            if (string.IsNullOrWhiteSpace(Accessibility.AccessibleName)) Accessibility.AccessibleName = next;
        }
    }

    public bool Multiline { get => GetValue(MultilineProperty); set => SetValue(MultilineProperty, value); }
    public int CaretIndex => GetValue(CaretIndexProperty);

    public void PlaceCaretAtEnd() => SetCaret(Text.Length);
    public void PlaceCaretAtStart() => SetCaret(0);

    public bool MoveCaret(int direction)
    {
        if (direction == 0) return false;
        var next = direction < 0 ? PreviousBoundary(Text, CaretIndex) : NextBoundary(Text, CaretIndex);
        if (next == CaretIndex) return false;
        SetCaret(next);
        return true;
    }

    public bool InsertText(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        var insertion = Multiline ? value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n') : value.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal);
        if (insertion.Length == 0) return false;
        var index = NormalizeCaret(Text, CaretIndex);
        var next = Text.Insert(index, insertion);
        SetValue(TextProperty, next);
        SetCaret(index + insertion.Length);
        return true;
    }

    public bool Backspace()
    {
        var index = NormalizeCaret(Text, CaretIndex);
        if (index <= 0) return false;
        var start = PreviousBoundary(Text, index);
        SetValue(TextProperty, Text.Remove(start, index - start));
        SetCaret(start);
        return true;
    }

    public bool Delete()
    {
        var index = NormalizeCaret(Text, CaretIndex);
        if (index >= Text.Length) return false;
        var end = NextBoundary(Text, index);
        SetValue(TextProperty, Text.Remove(index, end - index));
        SetCaret(index);
        return true;
    }

    public override HavenComponentMetadata Metadata => new(
        "Input",
        "Components/Input/Input.cs",
        [InputDefaults.SystemClass],
        [InputDefaults.FocusTransition],
        "Haven owns field chrome, text editing, caret state, focus state, and rendering; platform backends only translate platform input events into Haven input events.");

    protected override void OnStateChanged()
    {
        ClearValue(HavenProperties.BorderColor, HavenValueSource.State);
        ClearValue(HavenProperties.BorderWidth, HavenValueSource.State);
        ClearValue(HavenProperties.Glow, HavenValueSource.State);
        ClearValue(HavenProperties.Opacity, HavenValueSource.State);
        ClearValue(HavenProperties.Transition, HavenValueSource.State);

        if (State.HasFlag(HavenElementState.Disabled))
        {
            SetValue(HavenProperties.Opacity, .52d, HavenValueSource.State);
            return;
        }

        if (State.HasFlag(HavenElementState.Focused))
        {
            SetValue(HavenProperties.BorderColor, "AccentSecondary", HavenValueSource.State);
            SetValue(HavenProperties.BorderWidth, HavenLength.Px(2), HavenValueSource.State);
            SetValue(HavenProperties.Glow, "AccentTertiaryGlow", HavenValueSource.State);
            SetValue(HavenProperties.Transition, InputDefaults.FocusTransition, HavenValueSource.State);
            return;
        }

        if (State.HasFlag(HavenElementState.Hover))
        {
            SetValue(HavenProperties.BorderColor, "AccentSecondary", HavenValueSource.State);
            SetValue(HavenProperties.BorderWidth, HavenLength.Px(1), HavenValueSource.State);
            SetValue(HavenProperties.Transition, InputDefaults.FocusTransition, HavenValueSource.State);
        }
    }

    private void SetCaret(int index) => SetValue(CaretIndexProperty, NormalizeCaret(Text, index));

    private static int NormalizeCaret(string text, int index)
    {
        index = Math.Clamp(index, 0, text.Length);
        if (index == 0 || index == text.Length) return index;
        var boundaries = StringInfo.ParseCombiningCharacters(text);
        var previous = 0;
        foreach (var boundary in boundaries)
        {
            if (boundary == index) return index;
            if (boundary > index) return previous;
            previous = boundary;
        }
        return text.Length;
    }

    private static int PreviousBoundary(string text, int index)
    {
        index = NormalizeCaret(text, index);
        if (index <= 0) return 0;
        var previous = 0;
        foreach (var boundary in StringInfo.ParseCombiningCharacters(text))
        {
            if (boundary >= index) break;
            previous = boundary;
        }
        return previous;
    }

    private static int NextBoundary(string text, int index)
    {
        index = NormalizeCaret(text, index);
        foreach (var boundary in StringInfo.ParseCombiningCharacters(text))
            if (boundary > index) return boundary;
        return text.Length;
    }
}

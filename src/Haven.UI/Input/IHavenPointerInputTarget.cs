namespace Haven.UI;

/// <summary>Backend-neutral keyboard/pointer modifier state delivered by platform hosts.</summary>
public readonly record struct HavenInputModifiers(
    bool Shift = false,
    bool Control = false,
    bool Alt = false,
    bool Meta = false);

/// <summary>Backend-neutral pointer gesture delivered by HavenInputRouter.</summary>
public readonly record struct HavenPointerInput(
    HavenPoint Position,
    HavenPoint LocalPosition,
    HavenPointerKind PointerKind,
    HavenKeyModifiers Modifiers = HavenKeyModifiers.None,
    HavenPointerButton Button = HavenPointerButton.Primary);

/// <summary>Consumes raw pointer press/move/release without exposing a platform input type to Haven.UI.</summary>
public interface IHavenPointerInputTarget
{
    bool PointerPressed(HavenPointerInput input);
    bool PointerMoved(HavenPointerInput input);
    bool PointerReleased(HavenPointerInput input);
    bool PointerCancelled(HavenPointerInput input) => PointerReleased(input);
}
/// <summary>Consumes a backend-neutral pointer-wheel gesture before ordinary scroll containers.</summary>
public interface IHavenScrollInputTarget
{
    bool PointerWheel(HavenPoint localPosition, double deltaX, double deltaY);
}

[Flags]
public enum HavenKeyModifiers
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
    Meta = 8
}

public readonly record struct HavenKeyInput(HavenKey Key, HavenKeyModifiers Modifiers)
{
    public bool Shift => Modifiers.HasFlag(HavenKeyModifiers.Shift);
    public bool Control => Modifiers.HasFlag(HavenKeyModifiers.Control);
    public bool Alt => Modifiers.HasFlag(HavenKeyModifiers.Alt);
    public bool Meta => Modifiers.HasFlag(HavenKeyModifiers.Meta);
    public bool PrimaryModifier => Control || Meta;
}

/// <summary>Rich keyboard input for custom Haven elements such as grids and node canvases.</summary>
public interface IHavenKeyboardInputTarget
{
    bool KeyDown(HavenKeyInput input);
    bool KeyUp(HavenKeyInput input) => false;
}

/// <summary>Text composition for custom Haven elements.</summary>
public interface IHavenTextInputTarget
{
    bool TextInput(string? text);
}

/// <summary>Clipboard contract for custom Haven elements. The platform host owns the OS clipboard.</summary>
public interface IHavenClipboardInputTarget
{
    string? Copy();
    string? Cut();
    bool Paste(string? text);
}

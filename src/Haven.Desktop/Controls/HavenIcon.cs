/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Controls/HavenIcon.cs, in the Desktop controls layer, containing reusable Avalonia behavior and visual building blocks.
 * What: This file owns HavenIcon. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Haven.Desktop.Controls;

/// <summary>
/// Resolves stable Haven icon keys to medium-weight outlined 24px geometry. This is
/// also used for user-created catalog items, so an unknown key always renders
/// a visible fallback instead of disappearing.
/// </summary>
public sealed class HavenIcon : PathIcon
{
    /// <summary>
    /// Stores icon key property locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public static readonly StyledProperty<string> IconKeyProperty =
        AvaloniaProperty.Register<HavenIcon, string>(nameof(IconKey), "info");

    /// <summary>
    /// Stores icons locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Geometry> Icons = BuildIcons();

    static HavenIcon() => IconKeyProperty.Changed.AddClassHandler<HavenIcon>((icon, _) => icon.ResolveIcon());

    public HavenIcon() => ResolveIcon();

    // PathIcon's default control theme is keyed to PathIcon itself. Without
    // this override a derived HavenIcon has geometry and accessibility text,
    // but no template, so it occupies space without drawing anything.
    /// <summary>
    /// Gets or updates style key override, the bindable or domain state represented by this property.
    /// </summary>
    protected override Type StyleKeyOverride => typeof(PathIcon);

    public string IconKey
    {
        get => GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value);
    }

    /// <summary>
    /// Reports whether known applies to the current state.
    /// </summary>
    public static bool IsKnown(string? key) => !string.IsNullOrWhiteSpace(key) && Icons.ContainsKey(key);

    /// <summary>
    /// Returns icon geometry for code-built controls that need to use Avalonia's
    /// concrete PathIcon theme directly. Unknown keys receive the visible info icon.
    /// </summary>
    public static Geometry GeometryFor(string? key)
    {
        var normalized = string.IsNullOrWhiteSpace(key) ? "info" : key.Trim();
        return Icons.TryGetValue(normalized, out var geometry) ? geometry : Icons["info"];
    }

    /// <summary>
    /// Performs the resolve icon step owned by this component.
    /// </summary>
    private void ResolveIcon()
    {
        var key = string.IsNullOrWhiteSpace(IconKey) ? "info" : IconKey.Trim();
        Data = Icons.TryGetValue(key, out var geometry) ? geometry : Icons["info"];
    }

    /// <summary>
    /// Builds icons from the currently available inputs.
    /// </summary>
    private static IReadOnlyDictionary<string, Geometry> BuildIcons()
    {
        var icons = new Dictionary<string, Geometry>(StringComparer.OrdinalIgnoreCase);
        void Add(string key, string data, params string[] aliases)
        {
            // The path data already describes the intended filled icon, including
            // internal cut-outs. Hollowing every glyph by subtracting a scaled copy
            // destroyed multi-part shapes and made unrelated icons look like rings.
            // Even/odd fill keeps genuine cut-outs while preserving the authored
            // semantic silhouette.
            var geometry = PathGeometry.Parse(data);
            geometry.FillRule = FillRule.EvenOdd;
            icons[key] = geometry;
            foreach (var alias in aliases) icons[alias] = geometry;
        }

        Add("home", "M12,2 L22,11 L19,11 L19,21 L14,21 L14,15 L10,15 L10,21 L5,21 L5,11 L2,11 Z", "dashboard");
        Add("chat", "M5,3 L19,3 C20.1,3 21,3.9 21,5 L21,15 C21,16.1 20.1,17 19,17 L9,17 L4,21 L4,17.8 C3.4,17.45 3,16.78 3,16 L3,5 C3,3.9 3.9,3 5,3 Z", "message");
        Add("study", "M12,3 L2,8.5 L6,10.7 L6,16 L12,20 L18,16 L18,10.7 L20,9.6 L20,15 L22,15 L22,8.5 Z M8,11.8 L12,14 L16,11.8 L16,15 L12,17.5 L8,15 Z", "teach", "lesson", "education");
        Add("call", "M7.2,3 L10.4,7.2 L8.5,9.1 C9.7,11.6 12.4,14.3 14.9,15.5 L16.8,13.6 L21,16.8 L19.2,20 C18.7,20.9 17.7,21.3 16.7,21 C9.7,18.8 5.2,14.3 3,7.3 C2.7,6.3 3.1,5.3 4,4.8 Z", "phone");
        Add("tasks", "M5,3 L19,3 C20.1,3 21,3.9 21,5 L21,19 C21,20.1 20.1,21 19,21 L5,21 C3.9,21 3,20.1 3,19 L3,5 C3,3.9 3.9,3 5,3 Z M7,7 L9,7 L9,9 L7,9 Z M11,7 L18,7 L18,9 L11,9 Z M7,11 L9,11 L9,13 L7,13 Z M11,11 L18,11 L18,13 L11,13 Z M7,15 L9,15 L9,17 L7,17 Z M11,15 L16,15 L16,17 L11,17 Z", "task", "do", "goal");
        Add("studio", "M12,2 C6.5,2 2,6.5 2,12 C2,17.5 6.5,22 12,22 L13.7,22 C15.7,22 16.9,19.8 15.8,18.2 C15.2,17.3 15.9,16 17,16 L18.2,16 C20.3,16 22,14.3 22,12.2 C22,6.6 17.5,2 12,2 Z M7,9 C5.9,9 5,8.1 5,7 C5,5.9 5.9,5 7,5 C8.1,5 9,5.9 9,7 C9,8.1 8.1,9 7,9 Z M12,7 C10.9,7 10,6.1 10,5 C10,3.9 10.9,3 12,3 C13.1,3 14,3.9 14,5 C14,6.1 13.1,7 12,7 Z M17,9 C15.9,9 15,8.1 15,7 C15,5.9 15.9,5 17,5 C18.1,5 19,5.9 19,7 C19,8.1 18.1,9 17,9 Z M7,15 C5.9,15 5,14.1 5,13 C5,11.9 5.9,11 7,11 C8.1,11 9,11.9 9,13 C9,14.1 8.1,15 7,15 Z", "palette", "code", "create");
        Add("browse", "M12,2 C17.5,2 22,6.5 22,12 C22,17.5 17.5,22 12,22 C6.5,22 2,17.5 2,12 C2,6.5 6.5,2 12,2 Z M8.1,6 C7.3,7.4 6.8,9.1 6.7,11 L10.8,11 C10.9,9 11.3,7.3 12,6 Z M13.2,6 C13.9,7.3 14.3,9 14.4,11 L18.5,11 C18.3,9.1 17.8,7.4 17,6 Z M6.7,13 C6.8,14.9 7.3,16.6 8.1,18 L12,18 C11.3,16.7 10.9,15 10.8,13 Z M14.4,13 C14.3,15 13.9,16.7 13.2,18 L17,18 C17.8,16.6 18.3,14.9 18.5,13 Z", "globe", "browser-use", "web-search");
        Add("plan", "M6,2 L8,2 L8,4 L16,4 L16,2 L18,2 L18,4 L20,4 C21.1,4 22,4.9 22,6 L22,20 C22,21.1 21.1,22 20,22 L4,22 C2.9,22 2,21.1 2,20 L2,6 C2,4.9 2.9,4 4,4 L6,4 Z M4,9 L20,9 L20,20 L4,20 Z M7,12 L11,12 L11,16 L7,16 Z", "calendar", "automation");
        Add("training", "M12,2 L22,7.5 L18,9.7 L18,16 L12,20 L6,16 L6,9.7 L2,7.5 Z M8,10.8 L12,13 L16,10.8 L16,15 L12,17.5 L8,15 Z");
        Add("notes", "M5,2 L19,2 C20.1,2 21,2.9 21,4 L21,20 C21,21.1 20.1,22 19,22 L5,22 C3.9,22 3,21.1 3,20 L3,4 C3,2.9 3.9,2 5,2 Z M7,6 L17,6 L17,8 L7,8 Z M7,10 L17,10 L17,12 L7,12 Z M7,14 L14,14 L14,16 L7,16 Z", "book", "document");
        Add("present", "M3,3 L21,3 C22.1,3 23,3.9 23,5 L23,16 C23,17.1 22.1,18 21,18 L14,18 L14,20 L18,22 L16.8,24 L12,21.4 L7.2,24 L6,22 L10,20 L10,18 L3,18 C1.9,18 1,17.1 1,16 L1,5 C1,3.9 1.9,3 3,3 Z M4,6 L4,15 L20,15 L20,6 Z");
        Add("data", "M12,2 C17.5,2 22,3.8 22,6 L22,18 C22,20.2 17.5,22 12,22 C6.5,22 2,20.2 2,18 L2,6 C2,3.8 6.5,2 12,2 Z M5,6 C5,6.7 7.7,8 12,8 C16.3,8 19,6.7 19,6 C19,5.3 16.3,5 12,5 C7.7,5 5,5.3 5,6 Z M5,10 L5,13 C6.3,14.1 9,15 12,15 C15,15 17.7,14.1 19,13 L19,10 C17.2,10.8 14.8,11 12,11 C9.2,11 6.8,10.8 5,10 Z M5,16 L5,18 C5,18.7 7.7,19 12,19 C16.3,19 19,18.7 19,18 L19,16 C17.2,16.8 14.8,17 12,17 C9.2,17 6.8,16.8 5,16 Z");
        Add("vision", "M12,4 C17.3,4 21.5,7.2 24,12 C21.5,16.8 17.3,20 12,20 C6.7,20 2.5,16.8 0,12 C2.5,7.2 6.7,4 12,4 Z M12,7 C9.2,7 7,9.2 7,12 C7,14.8 9.2,17 12,17 C14.8,17 17,14.8 17,12 C17,9.2 14.8,7 12,7 Z M12,9 C13.7,9 15,10.3 15,12 C15,13.7 13.7,15 12,15 C10.3,15 9,13.7 9,12 C9,10.3 10.3,9 12,9 Z", "eye");
        Add("play", "M7,6 L17,6 C19.7,6 22,8.2 22,11 L22,17 C22,19.2 20.2,21 18,21 C16.5,21 15.2,20.2 14.5,19 L9.5,19 C8.8,20.2 7.5,21 6,21 C3.8,21 2,19.2 2,17 L2,11 C2,8.2 4.3,6 7,6 Z M7,9 L7,12 L4,12 L4,15 L7,15 L7,18 L10,18 L10,15 L13,15 L13,12 L10,12 L10,9 Z M17,10 C15.9,10 15,10.9 15,12 C15,13.1 15.9,14 17,14 C18.1,14 19,13.1 19,12 C19,10.9 18.1,10 17,10 Z");
        Add("translate", "M3,3 L12,3 L12,6 L9,6 C8.5,8.1 7.7,10 6.6,11.6 C7.6,12.7 8.8,13.6 10.3,14.4 L9,17 C7.4,16.1 6,15 4.8,13.7 C3.9,14.5 3,15.2 2,15.9 L0.5,13.5 C1.5,12.9 2.4,12.2 3.1,11.5 C2.3,10.3 1.6,9 1,7.5 L3.7,6.6 C4.1,7.6 4.5,8.5 5,9.3 C5.5,8.3 5.9,7.2 6.2,6 L3,6 Z M15,8 L18,8 L24,22 L20.8,22 L19.5,18.5 L13.5,18.5 L12.2,22 L9,22 Z M16.5,10.8 L14.5,16 L18.5,16 Z", "language");
        Add("all-modes", "M3,3 L10,3 L10,10 L3,10 Z M14,3 L21,3 L21,10 L14,10 Z M3,14 L10,14 L10,21 L3,21 Z M14,14 L21,14 L21,21 L14,21 Z", "grid");
        Add("window", "M3,3 L21,3 C22.1,3 23,3.9 23,5 L23,19 C23,20.1 22.1,21 21,21 L3,21 C1.9,21 1,20.1 1,19 L1,5 C1,3.9 1.9,3 3,3 Z M4,7 L20,7 L20,18 L4,18 Z M5,4.5 C4.45,4.5 4,4.95 4,5.5 C4,6.05 4.45,6.5 5,6.5 C5.55,6.5 6,6.05 6,5.5 C6,4.95 5.55,4.5 5,4.5 Z");
        Add("menu", "M4,5 L20,5 L20,8 L4,8 Z M4,10.5 L20,10.5 L20,13.5 L4,13.5 Z M4,16 L20,16 L20,19 L4,19 Z");
        Add("close", "M6.4,5 L12,10.6 L17.6,5 L19,6.4 L13.4,12 L19,17.6 L17.6,19 L12,13.4 L6.4,19 L5,17.6 L10.6,12 L5,6.4 Z");
        Add("chevron-down", "M6,8 L12,14 L18,8 L20,10 L12,18 L4,10 Z");
        Add("chevron-left", "M15.5,4 L7.5,12 L15.5,20 L18,17.5 L12.5,12 L18,6.5 Z", "arrow-left");
        Add("chevron-right", "M8.5,4 L16.5,12 L8.5,20 L6,17.5 L11.5,12 L6,6.5 Z");
        Add("plus", "M10.5,3 L13.5,3 L13.5,10.5 L21,10.5 L21,13.5 L13.5,13.5 L13.5,21 L10.5,21 L10.5,13.5 L3,13.5 L3,10.5 L10.5,10.5 Z");
        Add("send", "M2,11 L22,2 L15,22 L11,14 L2,11 Z M11,14 L22,2 L8,12 Z", "paper-plane");
        Add("check", "M9.2,17.5 L3.7,12 L6,9.7 L9.2,12.9 L18,4.1 L20.3,6.4 Z");
        Add("search", "M10.5,3 C14.64,3 18,6.36 18,10.5 C18,12.08 17.51,13.55 16.67,14.76 L22,20.09 L20.09,22 L14.76,16.67 C13.55,17.51 12.08,18 10.5,18 C6.36,18 3,14.64 3,10.5 C3,6.36 6.36,3 10.5,3 Z M10.5,6 C8.01,6 6,8.01 6,10.5 C6,12.99 8.01,15 10.5,15 C12.99,15 15,12.99 15,10.5 C15,8.01 12.99,6 10.5,6 Z");
        Add("refresh", "M12,4 C14.2,4 16.2,4.9 17.7,6.3 L15,9 L22,9 L22,2 L19.8,4.2 C17.7,2.2 15,1 12,1 C5.9,1 1,5.9 1,12 L4,12 C4,7.6 7.6,4 12,4 Z M20,12 C20,16.4 16.4,20 12,20 C9.8,20 7.8,19.1 6.3,17.7 L9,15 L2,15 L2,22 L4.2,19.8 C6.3,21.8 9,23 12,23 C18.1,23 23,18.1 23,12 Z", "reload");
        Add("rocket", "M14.2,2 C17.9,2.7 21.3,6.1 22,9.8 L15.8,16 L11,15 L9,13 L8,9.2 Z M15.4,5.8 C14.4,5.8 13.6,6.6 13.6,7.6 C13.6,8.6 14.4,9.4 15.4,9.4 C16.4,9.4 17.2,8.6 17.2,7.6 C17.2,6.6 16.4,5.8 15.4,5.8 Z M8.3,10 L5,10.5 L2,14 L7.1,15.1 Z M10.9,16.9 L12,22 L15.5,19 L16,15.7 Z M7.6,16.4 L9.6,18.4 L5,22 L2,22 L2,19 Z");
        Add("clock", "M12,2 C17.5,2 22,6.5 22,12 C22,17.5 17.5,22 12,22 C6.5,22 2,17.5 2,12 C2,6.5 6.5,2 12,2 Z M12,5 C8.1,5 5,8.1 5,12 C5,15.9 8.1,19 12,19 C15.9,19 19,15.9 19,12 C19,8.1 15.9,5 12,5 Z M10.8,6.7 L13.2,6.7 L13.2,11.2 L16.8,13.3 L15.6,15.4 L10.8,12.6 Z");
        Add("bell", "M12,2 C14.2,2 16,3.8 16,6 L16,7.1 C18.5,8.5 20,11.1 20,14 L20,17 L22,19 L2,19 L4,17 L4,14 C4,11.1 5.5,8.5 8,7.1 L8,6 C8,3.8 9.8,2 12,2 Z M9,21 L15,21 C14.5,22.2 13.4,23 12,23 C10.6,23 9.5,22.2 9,21 Z");
        Add("commands", "M4,4 L12,12 L4,20 L2,18 L8,12 L2,6 Z M12,18 L22,18 L22,21 L12,21 Z");
        Add("settings", "M10,2 L14,2 L14.7,5 C15.5,5.3 16.2,5.7 16.9,6.2 L19.8,5.3 L21.8,8.7 L19.5,10.8 C19.6,11.2 19.6,11.6 19.6,12 C19.6,12.4 19.6,12.8 19.5,13.2 L21.8,15.3 L19.8,18.7 L16.9,17.8 C16.2,18.3 15.5,18.7 14.7,19 L14,22 L10,22 L9.3,19 C8.5,18.7 7.8,18.3 7.1,17.8 L4.2,18.7 L2.2,15.3 L4.5,13.2 C4.4,12.8 4.4,12.4 4.4,12 C4.4,11.6 4.4,11.2 4.5,10.8 L2.2,8.7 L4.2,5.3 L7.1,6.2 C7.8,5.7 8.5,5.3 9.3,5 Z M12,8 C9.8,8 8,9.8 8,12 C8,14.2 9.8,16 12,16 C14.2,16 16,14.2 16,12 C16,9.8 14.2,8 12,8 Z");
        Add("archive", "M4,3 L20,3 C21.1,3 22,3.9 22,5 L22,8 C22,8.7 21.6,9.4 21,9.7 L21,20 C21,21.1 20.1,22 19,22 L5,22 C3.9,22 3,21.1 3,20 L3,9.7 C2.4,9.4 2,8.7 2,8 L2,5 C2,3.9 2.9,3 4,3 Z M5,10 L5,20 L19,20 L19,10 Z M9,13 L15,13 L15,16 L9,16 Z");
        Add("agents", "M8,3 C10.2,3 12,4.8 12,7 C12,9.2 10.2,11 8,11 C5.8,11 4,9.2 4,7 C4,4.8 5.8,3 8,3 Z M16.5,4 C18.4,4 20,5.6 20,7.5 C20,9.4 18.4,11 16.5,11 C14.6,11 13,9.4 13,7.5 C13,5.6 14.6,4 16.5,4 Z M8,13 C12.4,13 15,15.2 15,18 L15,21 L1,21 L1,18 C1,15.2 3.6,13 8,13 Z M16.5,13 C20.4,13 23,15 23,17.5 L23,20 L17,20 L17,18 C17,16.2 16.3,14.6 15,13.4 C15.5,13.1 16,13 16.5,13 Z", "agent", "agent-default", "agent-auto", "agent-research", "duo");
        Add("plugin", "M9,2 L15,2 L15,7 L19,7 C20.7,7 22,8.3 22,10 C22,11.7 20.7,13 19,13 L15,13 L15,22 L9,22 L9,18 C9,16.3 7.7,15 6,15 C4.3,15 3,16.3 3,18 L3,22 L2,22 L2,13 L7,13 L7,9 L2,9 L2,3 L9,3 Z", "plugin-custom");
        Add("prompt", "M12,2 C17,2 21,5.8 21,10.5 C21,13.6 19.2,16.3 16.5,17.8 L16.5,20 L7.5,20 L7.5,17.8 C4.8,16.3 3,13.6 3,10.5 C3,5.8 7,2 12,2 Z M9,22 L15,22 L15,24 L9,24 Z", "lightbulb");
        Add("more", "M5,9 C6.7,9 8,10.3 8,12 C8,13.7 6.7,15 5,15 C3.3,15 2,13.7 2,12 C2,10.3 3.3,9 5,9 Z M12,9 C13.7,9 15,10.3 15,12 C15,13.7 13.7,15 12,15 C10.3,15 9,13.7 9,12 C9,10.3 10.3,9 12,9 Z M19,9 C20.7,9 22,10.3 22,12 C22,13.7 20.7,15 19,15 C17.3,15 16,13.7 16,12 C16,10.3 17.3,9 19,9 Z");
        Add("folder", "M3,4 L9,4 L11,7 L21,7 C22.1,7 23,7.9 23,9 L23,20 C23,21.1 22.1,22 21,22 L3,22 C1.9,22 1,21.1 1,20 L1,6 C1,4.9 1.9,4 3,4 Z");
        Add("file", "M6,2 L15,2 L21,8 L21,22 L6,22 C4.9,22 4,21.1 4,20 L4,4 C4,2.9 4.9,2 6,2 Z M14,4 L14,9 L19,9 Z M8,13 L17,13 L17,15 L8,15 Z M8,17 L15,17 L15,19 L8,19 Z", "context", "handoff", "report", "inspect");
        Add("image", "M4,3 L20,3 C21.1,3 22,3.9 22,5 L22,19 C22,20.1 21.1,21 20,21 L4,21 C2.9,21 2,20.1 2,19 L2,5 C2,3.9 2.9,3 4,3 Z M5,6 L5,17 L9,12 L12,15 L15,11 L19,17 L19,6 Z M8,7 C9.1,7 10,7.9 10,9 C10,10.1 9.1,11 8,11 C6.9,11 6,10.1 6,9 C6,7.9 6.9,7 8,7 Z", "camera", "photo");
        Add("cpu", "M7,2 L9,2 L9,5 L11,5 L11,2 L13,2 L13,5 L15,5 L15,2 L17,2 L17,5 L19,5 L19,7 L22,7 L22,9 L19,9 L19,11 L22,11 L22,13 L19,13 L19,15 L22,15 L22,17 L19,17 L19,19 L17,19 L17,22 L15,22 L15,19 L13,19 L13,22 L11,22 L11,19 L9,19 L9,22 L7,22 L7,19 L5,19 L5,17 L2,17 L2,15 L5,15 L5,13 L2,13 L2,11 L5,11 L5,9 L2,9 L2,7 L5,7 L5,5 L7,5 Z M8,8 L8,16 L16,16 L16,8 Z M10,10 L14,10 L14,14 L10,14 Z", "processor", "chip");
        Add("edit", "M16.6,2.6 C17.4,1.8 18.7,1.8 19.5,2.6 L21.4,4.5 C22.2,5.3 22.2,6.6 21.4,7.4 L8,20.8 L2,22 L3.2,16 Z M5.4,17.2 L4.8,19.2 L6.8,18.6 L17.8,7.6 L16.4,6.2 Z");
        Add("copy", "M8,2 L20,2 C21.1,2 22,2.9 22,4 L22,18 C22,19.1 21.1,20 20,20 L8,20 C6.9,20 6,19.1 6,18 L6,4 C6,2.9 6.9,2 8,2 Z M3,6 L4,6 L4,19 C4,20.7 5.3,22 7,22 L18,22 L18,23 L6,23 C3.8,23 2,21.2 2,19 L2,7 C2,6.4 2.4,6 3,6 Z M9,6 L19,6 L19,8 L9,8 Z M9,10 L19,10 L19,12 L9,12 Z");
        Add("branch", "M5,2 C6.7,2 8,3.3 8,5 C8,6.3 7.2,7.4 6,7.8 L6,10 C6,11.1 6.9,12 8,12 L16,12 L16,7.8 C14.8,7.4 14,6.3 14,5 C14,3.3 15.3,2 17,2 C18.7,2 20,3.3 20,5 C20,6.3 19.2,7.4 18,7.8 L18,16.2 C19.2,16.6 20,17.7 20,19 C20,20.7 18.7,22 17,22 C15.3,22 14,20.7 14,19 C14,17.7 14.8,16.6 16,16.2 L16,14 L8,14 C5.8,14 4,12.2 4,10 L4,7.8 C2.8,7.4 2,6.3 2,5 C2,3.3 3.3,2 5,2 Z");
        Add("delete", "M8,2 L16,2 L17,5 L22,5 L22,8 L20,8 L20,21 C20,22.1 19.1,23 18,23 L6,23 C4.9,23 4,22.1 4,21 L4,8 L2,8 L2,5 L7,5 Z M8,10 L11,10 L11,19 L8,19 Z M13,10 L16,10 L16,19 L13,19 Z", "danger");
        Add("info", "M12,2 C17.5,2 22,6.5 22,12 C22,17.5 17.5,22 12,22 C6.5,22 2,17.5 2,12 C2,6.5 6.5,2 12,2 Z M10.5,10 L13.5,10 L13.5,18 L10.5,18 Z M10.5,6 L13.5,6 L13.5,9 L10.5,9 Z", "fallback", "explain");
        Add("build", "M14,2 C17.3,2 20,4.7 20,8 C20,9.1 19.7,10.1 19.2,11 L22,13.8 L18.8,17 L16,14.2 C15.1,14.7 14.1,15 13,15 C9.7,15 7,12.3 7,9 C7,8.2 7.1,7.5 7.4,6.8 L10.5,9.9 L13.9,6.5 L10.8,3.4 C11.8,2.5 12.8,2 14,2 Z M3,13 L11,21 L8,24 L0,16 Z", "production");
        Add("test", "M8,2 L16,2 L16,5 L15,5 L15,9 L21,19 C21.8,20.3 20.9,22 19.4,22 L4.6,22 C3.1,22 2.2,20.3 3,19 L9,9 L9,5 L8,5 Z M8,16 L16,16 L13,11 L11,11 Z", "extended-validation", "stress-test", "debug");
        Add("mic", "M12,2 C14.2,2 16,3.8 16,6 L16,12 C16,14.2 14.2,16 12,16 C9.8,16 8,14.2 8,12 L8,6 C8,3.8 9.8,2 12,2 Z M4,11 L7,11 L7,12 C7,14.8 9.2,17 12,17 C14.8,17 17,14.8 17,12 L17,11 L20,11 L20,12 C20,15.9 17,19.2 13.5,19.9 L13.5,23 L10.5,23 L10.5,19.9 C7,19.2 4,15.9 4,12 Z");
        Add("pause", "M5,3 L10,3 L10,21 L5,21 Z M14,3 L19,3 L19,21 L14,21 Z");
        Add("mute", "M4,3 L22,21 L20,23 L15.8,18.8 C14.7,19.5 13.4,20 12,20 L12,23 L9,23 L9,19.6 C5.6,18.4 3,15.4 3,12 L3,10 L6,10 L6,12 C6,13.6 6.7,15 7.9,15.9 L6.4,14.4 C6.1,13.7 6,12.9 6,12 L6,6.4 L2,2.4 Z M12,2 C14.2,2 16,3.8 16,6 L16,12 C16,12.5 15.9,13 15.7,13.4 L8,5.7 C8.2,3.6 9.9,2 12,2 Z M18,10 L21,10 L21,12 C21,14 20.4,15.9 19.4,17.4 L17.2,15.2 C17.7,14.3 18,13.2 18,12 Z");
        Add("screen-share", "M3,3 L21,3 C22.1,3 23,3.9 23,5 L23,17 C23,18.1 22.1,19 21,19 L14,19 L14,21 L18,21 L18,23 L6,23 L6,21 L10,21 L10,19 L3,19 C1.9,19 1,18.1 1,17 L1,5 C1,3.9 1.9,3 3,3 Z M4,6 L4,16 L20,16 L20,6 Z M12,7 L17,12 L14.8,14.2 L12,11.4 L9.2,14.2 L7,12 Z");
        Add("hang-up", "M4,9 C9.3,5.8 14.7,5.8 20,9 C21.2,9.7 21.5,11.2 20.8,12.4 L18.6,16.2 C18,17.2 16.8,17.6 15.7,17.1 L12.8,15.7 C12.3,15.4 12,14.9 12,14.3 L12,12.2 C10.7,12 9.3,12 8,12.2 L8,14.3 C8,14.9 7.7,15.4 7.2,15.7 L4.3,17.1 C3.2,17.6 2,17.2 1.4,16.2 L0.2,12.4 C-0.5,11.2 -0.2,9.7 1,9 Z", "phone-off");
        Add("pin", "M7,2 L17,2 L17,4 L15,6 L15,11 L19,15 L19,17 L13,17 L13,23 L11,23 L11,17 L5,17 L5,15 L9,11 L9,6 L7,4 Z");
        Add("bookmark", "M6,2 L18,2 C19.1,2 20,2.9 20,4 L20,22 L12,17 L4,22 L4,4 C4,2.9 4.9,2 6,2 Z");
        Add("rapid", "M13,2 L4,14 L11,14 L10,22 L20,10 L13,10 Z", "bolt", "actions");
        Add("experiment", "M9,2 L15,2 L15,5 L14,5 L14,9 L20,19 C20.8,20.4 19.8,22 18.2,22 L5.8,22 C4.2,22 3.2,20.4 4,19 L10,9 L10,5 L9,5 Z M8,17 L16,17 L13,12 L11,12 Z");
        Add("golden-rules", "M12,2 L15,8 L22,9 L17,14 L18,21 L12,18 L6,21 L7,14 L2,9 L9,8 Z");
        Add("rigid", "M4,2 L20,2 C21.1,2 22,2.9 22,4 L22,20 C22,21.1 21.1,22 20,22 L4,22 C2.9,22 2,21.1 2,20 L2,4 C2,2.9 2.9,2 4,2 Z M6,6 L18,6 L18,18 L6,18 Z");
        Add("warning", "M12,2 L23,21 L1,21 Z M12,6 L3.5,20 L20.5,20 Z M11,10 L13,10 L13,15 L11,15 Z M11,16 L13,16 L13,18 L11,18 Z");
        Add("expand", "M3,3 L10,3 L10,5 L5,5 L5,10 L3,10 Z M21,3 L14,3 L14,5 L19,5 L19,10 L21,10 Z M3,21 L10,21 L10,19 L5,19 L5,14 L3,14 Z M21,21 L14,21 L14,19 L19,19 L19,14 L21,14 Z");
        Add("dock-right", "M3,3 L20,3 C21.1,3 22,3.9 22,5 L22,19 C22,20.1 21.1,21 20,21 L3,21 C1.9,21 1,20.1 1,19 L1,5 C1,3.9 1.9,3 3,3 Z M16,5 L16,19 L20,19 L20,5 Z");

        return icons;
    }
}

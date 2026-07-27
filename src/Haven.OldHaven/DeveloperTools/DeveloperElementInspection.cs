/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/DeveloperTools/DeveloperElementInspection.cs in the Desktop composition layer.
 * What: Formats runtime controls and applies a deliberately small set of safe live property edits.
 * How: Public Avalonia properties become readable rows and CSS-like selectors; validated values update the selected element.
 * Why: Inspection and quick layout experiments should not require reflection or private framework APIs.
 */

using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace Haven.Desktop.DeveloperTools;

internal sealed record DeveloperPropertyEntry(string Name, string Value);

/// <summary>
/// Converts runtime elements into readable tree labels, selectors, and property rows.
/// </summary>
internal static class DeveloperElementFormatter
{
    public static bool Matches(Visual visual, string filter)
    {
        var haystack = new StringBuilder(visual.GetType().Name);
        if (visual is StyledElement styled)
        {
            haystack.Append(' ').Append(styled.Name);
            foreach (var item in styled.Classes) haystack.Append(' ').Append(item);
        }
        haystack.Append(' ').Append(GetBriefText(visual));
        return haystack.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    public static string BuildSelector(Visual visual)
    {
        var parts = new Stack<string>();
        Visual? current = visual;
        while (current is not null && parts.Count < 5)
        {
            parts.Push(BuildSelectorPart(current));
            if (current is StyledElement { Name: { Length: > 0 } }) break;
            current = current.GetVisualParent();
        }
        return string.Join(" > ", parts);
    }

    public static string GetBriefText(Visual visual)
    {
        var value = visual switch
        {
            TextBlock textBlock => textBlock.Text,
            TextBox textBox => textBox.Text,
            ContentControl { Content: string content } => content,
            HeaderedContentControl { Header: string header } => header,
            _ => null
        };
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 44 ? $"\"{normalized}\"" : $"\"{normalized[..41]}…\"";
    }

    public static IReadOnlyList<DeveloperPropertyEntry> GetProperties(Visual visual)
    {
        var rows = new List<DeveloperPropertyEntry>
        {
            new("Type", visual.GetType().FullName ?? visual.GetType().Name),
            new("Selector", BuildSelector(visual)),
            new("Bounds", FormatRect(visual.Bounds)),
            new("Visible", visual.IsVisible.ToString(CultureInfo.InvariantCulture)),
            new("Effectively visible", visual.IsEffectivelyVisible.ToString(CultureInfo.InvariantCulture)),
            new("Opacity", visual.Opacity.ToString("0.###", CultureInfo.InvariantCulture)),
            new("Z index", visual.ZIndex.ToString(CultureInfo.InvariantCulture)),
            new("Clip to bounds", visual.ClipToBounds.ToString(CultureInfo.InvariantCulture)),
            new("Visual parent", visual.GetVisualParent()?.GetType().Name ?? "(none)")
        };

        if (visual is StyledElement styled)
        {
            rows.Add(new("Name", string.IsNullOrWhiteSpace(styled.Name) ? "(unnamed)" : styled.Name));
            rows.Add(new("Classes", styled.Classes.Count == 0 ? "(none)" : string.Join(' ', styled.Classes)));
            rows.Add(new("Data context", styled.DataContext?.GetType().FullName ?? "null"));
            rows.Add(new("Templated parent", styled.TemplatedParent?.GetType().Name ?? "(none)"));
            rows.Add(new("Theme variant", styled.ActualThemeVariant?.ToString() ?? "(default)"));
        }

        if (visual is Layoutable layout)
        {
            rows.Add(new("Desired size", FormatSize(layout.DesiredSize)));
            rows.Add(new("Width", FormatLength(layout.Width)));
            rows.Add(new("Height", FormatLength(layout.Height)));
            rows.Add(new("Minimum", $"{layout.MinWidth:0.###} × {layout.MinHeight:0.###}"));
            rows.Add(new("Maximum", $"{FormatLength(layout.MaxWidth)} × {FormatLength(layout.MaxHeight)}"));
            rows.Add(new("Margin", layout.Margin.ToString()));
            rows.Add(new("Alignment", $"{layout.HorizontalAlignment}, {layout.VerticalAlignment}"));
        }

        if (visual is InputElement input)
        {
            rows.Add(new("Enabled", input.IsEnabled.ToString(CultureInfo.InvariantCulture)));
            rows.Add(new("Hit test visible", input.IsHitTestVisible.ToString(CultureInfo.InvariantCulture)));
            rows.Add(new("Pointer over", input.IsPointerOver.ToString(CultureInfo.InvariantCulture)));
            rows.Add(new("Keyboard focus within", input.IsKeyboardFocusWithin.ToString(CultureInfo.InvariantCulture)));
        }

        if (visual is Control control)
            rows.Add(new("Data validation errors", DataValidationErrors.GetHasErrors(control).ToString(CultureInfo.InvariantCulture)));
        if (visual is TextBlock textBlock)
            rows.Add(new("Text", textBlock.Text ?? string.Empty));
        if (visual is TextBox textBox)
            rows.Add(new("Text", textBox.Text ?? string.Empty));
        if (visual is ContentControl contentControl)
            rows.Add(new("Content", FormatObject(contentControl.Content)));
        if (visual is ItemsControl itemsControl)
            rows.Add(new("Item count", itemsControl.ItemCount.ToString(CultureInfo.InvariantCulture)));
        if (visual is Panel panel)
            rows.Add(new("Child count", panel.Children.Count.ToString(CultureInfo.InvariantCulture)));

        return rows;
    }

    private static string BuildSelectorPart(Visual visual)
    {
        var builder = new StringBuilder(visual.GetType().Name);
        if (visual is StyledElement styled)
        {
            if (!string.IsNullOrWhiteSpace(styled.Name)) builder.Append('#').Append(styled.Name);
            foreach (var item in styled.Classes.Take(3)) builder.Append('.').Append(item);
        }
        return builder.ToString();
    }

    private static string FormatRect(Rect rect) =>
        $"x={rect.X:0.###}, y={rect.Y:0.###}, {rect.Width:0.###} × {rect.Height:0.###}";

    private static string FormatSize(Size size) => $"{size.Width:0.###} × {size.Height:0.###}";

    private static string FormatLength(double value) => double.IsNaN(value) ? "Auto" : value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string FormatObject(object? value) => value switch
    {
        null => "null",
        string text => text,
        _ => value.GetType().FullName ?? value.ToString() ?? "(unknown)"
    };
}

/// <summary>
/// Applies a deliberately small, safe set of runtime edits to the selected element.
/// </summary>
internal static class DeveloperElementEditor
{
    public static string Read(Visual visual, string property) => property switch
    {
        "Opacity" => visual.Opacity.ToString("0.###", CultureInfo.InvariantCulture),
        "Width" when visual is Layoutable layout => FormatLength(layout.Width),
        "Height" when visual is Layoutable layout => FormatLength(layout.Height),
        "Margin" when visual is Layoutable layout => FormatThickness(layout.Margin),
        "IsVisible" => visual.IsVisible.ToString(CultureInfo.InvariantCulture),
        "Text" when visual is TextBlock textBlock => textBlock.Text ?? string.Empty,
        "Text" when visual is TextBox textBox => textBox.Text ?? string.Empty,
        "Content" when visual is ContentControl { Content: string content } => content,
        _ => string.Empty
    };

    public static bool TryApply(Visual visual, string property, string value, out string message)
    {
        try
        {
            switch (property)
            {
                case "Opacity":
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var opacity)
                        || opacity is < 0 or > 1)
                    {
                        message = "Opacity must be a number from 0 to 1.";
                        return false;
                    }
                    visual.Opacity = opacity;
                    break;

                case "Width" when visual is Layoutable layout:
                    if (!TryParseLength(value, out var width, out message)) return false;
                    layout.Width = width;
                    break;

                case "Height" when visual is Layoutable layout:
                    if (!TryParseLength(value, out var height, out message)) return false;
                    layout.Height = height;
                    break;

                case "Margin" when visual is Layoutable layout:
                    if (!TryParseThickness(value, out var margin))
                    {
                        message = "Margin accepts 1, 2, or 4 comma-separated numbers.";
                        return false;
                    }
                    layout.Margin = margin;
                    break;

                case "IsVisible":
                    if (!bool.TryParse(value, out var visible))
                    {
                        message = "IsVisible must be true or false.";
                        return false;
                    }
                    visual.IsVisible = visible;
                    break;

                case "Text" when visual is TextBlock textBlock:
                    textBlock.Text = value;
                    break;

                case "Text" when visual is TextBox textBox:
                    textBox.Text = value;
                    break;

                case "Content" when visual is ContentControl contentControl && contentControl.Content is null or string:
                    contentControl.Content = value;
                    break;

                default:
                    message = $"{property} is not editable for {visual.GetType().Name}.";
                    return false;
            }

            message = $"Applied {property} to {visual.GetType().Name}.";
            return true;
        }
        catch (Exception exception)
        {
            message = $"Could not apply {property}: {exception.Message}";
            return false;
        }
    }

    private static bool TryParseLength(string text, out double value, out string message)
    {
        if (string.Equals(text.Trim(), "Auto", StringComparison.OrdinalIgnoreCase))
        {
            value = double.NaN;
            message = string.Empty;
            return true;
        }
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || value < 0)
        {
            message = "Length must be Auto or a non-negative number.";
            return false;
        }
        message = string.Empty;
        return true;
    }

    private static bool TryParseThickness(string text, out Thickness thickness)
    {
        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var values = new double[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            if (!double.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out values[index]))
            {
                thickness = default;
                return false;
            }
        }

        thickness = values.Length switch
        {
            1 => new Thickness(values[0]),
            2 => new Thickness(values[0], values[1]),
            4 => new Thickness(values[0], values[1], values[2], values[3]),
            _ => default
        };
        return values.Length is 1 or 2 or 4;
    }

    private static string FormatLength(double value) =>
        double.IsNaN(value) ? "Auto" : value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string FormatThickness(Thickness value) =>
        $"{value.Left:0.###},{value.Top:0.###},{value.Right:0.###},{value.Bottom:0.###}";
}

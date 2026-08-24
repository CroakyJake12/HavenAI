using System.Globalization;
using Haven.UI.Components;

namespace Haven.UI;

public static class HavenPropertyCodec
{
    public static void Set(HavenElement element, string propertyName, string value, HavenValueSource source = HavenValueSource.Explicit)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        value ??= string.Empty;
        switch (propertyName.Trim().ToLowerInvariant())
        {
            case "name": element.SetValue(HavenProperties.Name, EmptyToNull(value), source); return;
            case "group": element.SetValue(HavenProperties.Group, value, source); return;
            case "class": element.SetValue(HavenProperties.Class, value, source); return;
            case "width": element.SetValue(HavenProperties.Width, HavenLength.Parse(value), source); return;
            case "height": element.SetValue(HavenProperties.Height, HavenLength.Parse(value), source); return;
            case "minwidth": element.SetValue(HavenProperties.MinWidth, HavenLength.Parse(value), source); return;
            case "minheight": element.SetValue(HavenProperties.MinHeight, HavenLength.Parse(value), source); return;
            case "maxwidth": element.SetValue(HavenProperties.MaxWidth, HavenLength.Parse(value), source); return;
            case "maxheight": element.SetValue(HavenProperties.MaxHeight, HavenLength.Parse(value), source); return;
            case "aspectratio": element.SetValue(HavenProperties.AspectRatio, ParseNullableDouble(value), source); return;
            case "margin": element.SetValue(HavenProperties.Margin, HavenThickness.Parse(value), source); return;
            case "padding": element.SetValue(HavenProperties.Padding, HavenThickness.Parse(value), source); return;
            case "gap": element.SetValue(HavenProperties.Gap, HavenLength.Parse(value), source); return;
            case "horizontalalignment": element.SetValue(HavenProperties.HorizontalAlignment, ParseEnum<HavenHorizontalAlignment>(value), source); return;
            case "verticalalignment": element.SetValue(HavenProperties.VerticalAlignment, ParseEnum<HavenVerticalAlignment>(value), source); return;
            case "row": element.SetValue(HavenProperties.Row, ParseInt(value), source); return;
            case "column": element.SetValue(HavenProperties.Column, ParseInt(value), source); return;
            case "rowspan": element.SetValue(HavenProperties.RowSpan, Math.Max(1, ParseInt(value)), source); return;
            case "columnspan": element.SetValue(HavenProperties.ColumnSpan, Math.Max(1, ParseInt(value)), source); return;
            case "left": element.SetValue(HavenProperties.Left, HavenLength.Parse(value), source); return;
            case "top": element.SetValue(HavenProperties.Top, HavenLength.Parse(value), source); return;
            case "layoutparticipation": element.SetValue(HavenProperties.LayoutParticipation, ParseEnum<HavenLayoutParticipation>(value), source); return;
            case "background": element.SetValue(HavenProperties.Background, value, source); return;
            case "foreground": element.SetValue(HavenProperties.Foreground, value, source); return;
            case "accent": element.SetValue(HavenProperties.Accent, value, source); return;
            case "opacity": element.SetValue(HavenProperties.Opacity, ParseDouble(value), source); return;
            case "borderwidth": element.SetValue(HavenProperties.BorderWidth, HavenLength.Parse(value), source); return;
            case "bordercolor": element.SetValue(HavenProperties.BorderColor, value, source); return;
            case "radius": element.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Parse(value)), source); return;
            case "shadow": HavenEffects.TryResolveShadow(value, out _); element.SetValue(HavenProperties.Shadow, value, source); return;
            case "glow": element.SetValue(HavenProperties.Glow, value, source); return;
            case "backdropblur": element.SetValue(HavenProperties.BackdropBlur, ParseDouble(value), source); return;
            case "scale": element.SetValue(HavenProperties.Scale, ParseDouble(value), source); return;
            case "rotation": element.SetValue(HavenProperties.Rotation, ParseDouble(value), source); return;
            case "translationx": element.SetValue(HavenProperties.TranslationX, HavenLength.Parse(value), source); return;
            case "translationy": element.SetValue(HavenProperties.TranslationY, HavenLength.Parse(value), source); return;
            case "transformorigin": element.SetValue(HavenProperties.TransformOrigin, value, source); return;
            case "clip": element.SetValue(HavenProperties.Clip, ParseBool(value), source); return;
            case "overflow": element.SetValue(HavenProperties.Overflow, ParseEnum<HavenOverflow>(value), source); return;
            case "zindex": element.SetValue(HavenProperties.ZIndex, ParseInt(value), source); return;
            case "visibility": element.SetValue(HavenProperties.Visibility, ParseEnum<HavenVisibility>(value), source); return;
            case "enabled":
                var enabled = ParseBool(value);
                element.SetValue(HavenProperties.Enabled, enabled, source);
                element.Accessibility.Enabled = enabled;
                element.SetState(HavenElementState.Disabled, !enabled);
                return;
            case "hover": element.SetValue(HavenProperties.Hover, ParseNullableBool(value), source); return;
            case "pointerevents": element.SetValue(HavenProperties.PointerEvents, ParseEnum<HavenPointerEvents>(value), source); return;
            case "cursor": element.SetValue(HavenProperties.Cursor, ParseEnum<HavenCursor>(value), source); return;
            case "responsive": element.SetValue(HavenProperties.Responsive, ParseBool(value), source); return;
            case "animation": element.SetValue(HavenProperties.Animation, EmptyToNull(value), source); return;
            case "transition": element.SetValue(HavenProperties.Transition, EmptyToNull(value), source); return;
            case "fontfamily": element.SetValue(HavenProperties.FontFamily, value, source); return;
            case "fontsize": element.SetValue(HavenProperties.FontSize, ParseDouble(value), source); return;
            case "fontweight": element.SetValue(HavenProperties.FontWeight, ParseInt(value), source); return;
        }

        switch (element)
        {
            case Button button when propertyName.Equals("Variant", StringComparison.OrdinalIgnoreCase) || propertyName.Equals("Type", StringComparison.OrdinalIgnoreCase): button.Variant = ParseEnum<ButtonVariant>(value); return;
            case Button button when propertyName.Equals("Content", StringComparison.OrdinalIgnoreCase): button.Content = value; return;
            case Button button when propertyName.Equals("IconKey", StringComparison.OrdinalIgnoreCase): button.IconKey = value; return;
            case Text text when propertyName.Equals("Content", StringComparison.OrdinalIgnoreCase) || propertyName.Equals("Text", StringComparison.OrdinalIgnoreCase): text.Content = value; return;
            case Markdown markdown when propertyName.Equals("Content", StringComparison.OrdinalIgnoreCase) || propertyName.Equals("Text", StringComparison.OrdinalIgnoreCase): markdown.Content = value; return;
            case Text text when propertyName.Equals("Level", StringComparison.OrdinalIgnoreCase): text.Level = ParseEnum<TextLevel>(value); return;
            case Container container when propertyName.Equals("Layout", StringComparison.OrdinalIgnoreCase): container.Layout = ParseEnum<HavenLayout>(value); return;
            case Container container when propertyName.Equals("Columns", StringComparison.OrdinalIgnoreCase): container.Columns = value; return;
            case Container container when propertyName.Equals("Rows", StringComparison.OrdinalIgnoreCase): container.Rows = value; return;
            case Input input when propertyName.Equals("Text", StringComparison.OrdinalIgnoreCase): input.Text = value; return;
            case Input input when propertyName.Equals("Placeholder", StringComparison.OrdinalIgnoreCase): input.Placeholder = value; return;
            case Input input when propertyName.Equals("Multiline", StringComparison.OrdinalIgnoreCase): input.Multiline = ParseBool(value); return;
            case Input input when propertyName.Equals("SubmitOnEnter", StringComparison.OrdinalIgnoreCase): input.SubmitOnEnter = ParseBool(value); return;
            case Input input when propertyName.Equals("Secret", StringComparison.OrdinalIgnoreCase) || propertyName.Equals("IsSecret", StringComparison.OrdinalIgnoreCase) || propertyName.Equals("Password", StringComparison.OrdinalIgnoreCase): input.IsSecret = ParseBool(value); return;
            case Input input when propertyName.Equals("RevealSecret", StringComparison.OrdinalIgnoreCase): input.RevealSecret = ParseBool(value); return;
            case Input input when propertyName.Equals("AllowSecretClipboard", StringComparison.OrdinalIgnoreCase): input.AllowSecretClipboard = ParseBool(value); return;
            case Toggle toggle when propertyName.Equals("Checked", StringComparison.OrdinalIgnoreCase): toggle.IsChecked = ParseBool(value); return;
            case Slider slider when propertyName.Equals("Minimum", StringComparison.OrdinalIgnoreCase): slider.Minimum = ParseDouble(value); return;
            case Slider slider when propertyName.Equals("Maximum", StringComparison.OrdinalIgnoreCase): slider.Maximum = ParseDouble(value); return;
            case Slider slider when propertyName.Equals("Value", StringComparison.OrdinalIgnoreCase): slider.Value = ParseDouble(value); return;
            case Slider slider when propertyName.Equals("Step", StringComparison.OrdinalIgnoreCase): slider.Step = ParseDouble(value); return;
            case Select select when propertyName.Equals("Items", StringComparison.OrdinalIgnoreCase): select.Items = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries); return;
            case Select select when propertyName.Equals("SelectedIndex", StringComparison.OrdinalIgnoreCase): select.SelectedIndex = ParseInt(value); return;
            case Select select when propertyName.Equals("Expanded", StringComparison.OrdinalIgnoreCase): select.IsExpanded = ParseBool(value); return;
            case Progress progress when propertyName.Equals("Minimum", StringComparison.OrdinalIgnoreCase): progress.Minimum = ParseDouble(value); return;
            case Progress progress when propertyName.Equals("Maximum", StringComparison.OrdinalIgnoreCase): progress.Maximum = ParseDouble(value); return;
            case Progress progress when propertyName.Equals("Value", StringComparison.OrdinalIgnoreCase): progress.Value = ParseDouble(value); return;
            case Image image when propertyName.Equals("Source", StringComparison.OrdinalIgnoreCase): image.Source = value; return;
            case Image image when propertyName.Equals("Fit", StringComparison.OrdinalIgnoreCase): image.Fit = ParseEnum<HavenImageFit>(value); return;
            case Icon icon when propertyName.Equals("Key", StringComparison.OrdinalIgnoreCase): icon.Key = value; return;
            case Video video when propertyName.Equals("Source", StringComparison.OrdinalIgnoreCase): video.Source = value; return;
            case Video video when propertyName.Equals("AutoPlay", StringComparison.OrdinalIgnoreCase): video.AutoPlay = ParseBool(value); return;
            case Web web when propertyName.Equals("Url", StringComparison.OrdinalIgnoreCase): web.Url = value; return;
            case Separator separator when propertyName.Equals("Orientation", StringComparison.OrdinalIgnoreCase): separator.Orientation = ParseEnum<SeparatorOrientation>(value); return;
            case Page page when propertyName.Equals("PageAccent", StringComparison.OrdinalIgnoreCase): page.PageAccent = value; return;
        }

        throw new KeyNotFoundException($"Property '{propertyName}' is not valid for Haven component '{element.Metadata.ComponentName}'.");
    }

    public static bool IsSafeActionProperty(string propertyName) => propertyName.Trim().ToLowerInvariant() is
        "visibility" or "enabled" or "opacity" or "checked" or "selected" or "expanded";

    private static bool ParseBool(string value) => bool.TryParse(value, out var result) ? result : throw new FormatException($"'{value}' is not a Boolean.");
    private static bool? ParseNullableBool(string value) => value.Equals("Auto", StringComparison.OrdinalIgnoreCase) ? null : ParseBool(value);
    private static double ParseDouble(string value) => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
    private static double? ParseNullableDouble(string value) => value.Equals("Auto", StringComparison.OrdinalIgnoreCase) ? null : ParseDouble(value);
    private static int ParseInt(string value) => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
    private static T ParseEnum<T>(string value) where T : struct, Enum => Enum.TryParse<T>(value, true, out var parsed) ? parsed : throw new FormatException($"'{value}' is not a valid {typeof(T).Name} value.");
    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

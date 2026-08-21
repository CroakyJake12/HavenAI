#if !ANDROID
using System.Text;

namespace Haven.Desktop.Overlay;

/// <summary>
/// Projects bounded universal-selection payloads into user-visible Overlay summaries and
/// reviewable Chat handoff text without becoming a second capture, permission, or runtime owner.
/// </summary>
internal static class OverlaySelectionPresentation
{
    internal static string ContextLabel(OverlayContextEnvelope context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!string.IsNullOrWhiteSpace(context.SelectedText))
        {
            var compact = Compact(context.SelectedText, 220);
            return (context.WasTruncated ? "Bounded selection · " : "Selected text · ") + compact;
        }

        if (context.SelectedItems.Count == 1)
            return SelectionLabel(context.SelectedItems[0].Bound());

        if (context.SelectedItems.Count > 1)
        {
            var kinds = context.SelectedItems
                .Select(item => SelectionKindLabel(item.Kind))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3);
            return $"{context.SelectedItems.Count} selected items · {string.Join(", ", kinds)}";
        }

        if (!string.IsNullOrWhiteSpace(context.MediaReference))
            return context.Kind == OverlayContextKind.Image ? "Selected image context" : "Selected screen region";
        return context.Attachments.Count > 0
            ? $"{context.Attachments.Count} context attachment(s)"
            : "Context provenance only.";
    }

    internal static string ReviewDetails(OverlayContextEnvelope context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.SelectedItems.Count == 0) return string.Empty;

        var builder = new StringBuilder("Selected items:");
        foreach (var raw in context.SelectedItems)
        {
            var item = raw.Bound();
            builder.AppendLine();
            builder.Append("- " ).Append(SelectionKindLabel(item.Kind));

            var label = !string.IsNullOrWhiteSpace(item.DisplayName)
                ? item.DisplayName
                : item.Semantic?.AccessibleName;
            if (!string.IsNullOrWhiteSpace(label)) builder.Append(" · " ).Append(label);
            if (!string.IsNullOrWhiteSpace(item.Semantic?.Role)) builder.Append(" · role " ).Append(item.Semantic.Role);
            if (!string.IsNullOrWhiteSpace(item.Semantic?.ControlType)) builder.Append(" · control " ).Append(item.Semantic.ControlType);
            if (!string.IsNullOrWhiteSpace(item.Semantic?.AutomationId)) builder.Append(" · automation id " ).Append(item.Semantic.AutomationId);
            if (item.Semantic?.IsEnabled is bool enabled) builder.Append(" · enabled " ).Append(enabled ? "yes" : "no");
            if (item.Semantic?.IsSelected is bool selected) builder.Append(" · selected " ).Append(selected ? "yes" : "no");
            if (!string.IsNullOrWhiteSpace(item.Semantic?.MediaKind)) builder.Append(" · media " ).Append(item.Semantic.MediaKind);
            if (item.Semantic?.MediaPositionSeconds is double seconds) builder.Append(" · position " ).Append(seconds.ToString("0.###")).Append('s');
            if (item.Bounds is { } bounds)
                builder.Append($" · bounds {bounds.X:0.#},{bounds.Y:0.#} {bounds.Width:0.#}×{bounds.Height:0.#}");
            if (!string.IsNullOrWhiteSpace(item.MediaReference)) builder.Append(" · media reference " ).Append(item.MediaReference);
            if (item.Attachment is { } attachment)
            {
                var attachmentLabel = !string.IsNullOrWhiteSpace(attachment.DisplayName)
                    ? attachment.DisplayName
                    : attachment.Id;
                builder.Append(" · attachment " ).Append(Compact(attachmentLabel, 512));
            }

            if (!string.IsNullOrWhiteSpace(item.Text)
                && !string.Equals(item.Text, context.SelectedText, StringComparison.Ordinal))
            {
                builder.AppendLine();
                builder.Append("  Text: " ).Append(Compact(item.Text, 2_048));
            }
        }
        return builder.ToString();
    }

    private static string SelectionLabel(OverlaySelectionItem item)
    {
        if (item.Kind == OverlaySelectionKind.Text && !string.IsNullOrWhiteSpace(item.Text))
            return "Selected text · " + Compact(item.Text, 220);

        var label = !string.IsNullOrWhiteSpace(item.DisplayName)
            ? item.DisplayName
            : item.Semantic?.AccessibleName;
        var parts = new List<string> { "Selected " + SelectionKindLabel(item.Kind) };
        if (!string.IsNullOrWhiteSpace(label)) parts.Add(label);
        if (!string.IsNullOrWhiteSpace(item.Semantic?.ControlType)
            && !string.Equals(item.Semantic.ControlType, label, StringComparison.OrdinalIgnoreCase))
            parts.Add(item.Semantic.ControlType);
        if (item.Semantic?.MediaPositionSeconds is double seconds) parts.Add(seconds.ToString("0.###") + "s");
        return string.Join(" · ", parts);
    }

    private static string SelectionKindLabel(OverlaySelectionKind kind) => kind switch
    {
        OverlaySelectionKind.UiComponent => "UI component",
        OverlaySelectionKind.Region => "screen region",
        OverlaySelectionKind.Window => "window",
        OverlaySelectionKind.Screen => "screen",
        OverlaySelectionKind.Video => "video",
        OverlaySelectionKind.Image => "image",
        _ => "text"
    };

    private static string Compact(string value, int maxLength)
    {
        var compact = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= maxLength ? compact : compact[..(maxLength - 1)] + "…";
    }
}
#endif

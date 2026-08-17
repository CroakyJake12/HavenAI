#if !ANDROID
namespace Haven.Desktop.Overlay;

internal enum OverlayContextKind
{
    None = 0,
    Text = 1,
    Image = 2,
    Region = 3,
    Mixed = 4,
    UiComponent = 5,
    Video = 6,
    Window = 7,
    Screen = 8
}

internal enum OverlaySelectionKind
{
    Text = 1,
    Image = 2,
    UiComponent = 3,
    Video = 4,
    Region = 5,
    Window = 6,
    Screen = 7
}

internal enum OverlayContextPermissionState
{
    NotRequired = 0,
    Granted = 1,
    Denied = 2,
    Unavailable = 3
}

internal sealed record OverlaySelectionBounds(double X, double Y, double Width, double Height);

internal sealed record OverlayContextProvenance(
    string? SourceApplication,
    string? SourceWindow,
    OverlaySelectionBounds? Bounds,
    DateTimeOffset CapturedAt,
    DateTimeOffset ExpiresAt,
    OverlayContextPermissionState PermissionState,
    string? PermissionDescription);

internal sealed record OverlayContextAttachmentReference(
    string Id,
    string Kind,
    string? MimeType,
    string? DisplayName,
    string? MetadataJson);

internal sealed record OverlaySelectionSemanticMetadata(
    string? Role,
    string? AccessibleName,
    string? AutomationId,
    string? ControlType,
    bool? IsEnabled,
    bool? IsSelected,
    string? MediaKind,
    double? MediaPositionSeconds)
{
    public OverlaySelectionSemanticMetadata Bound(int maxStringLength = 512)
    {
        if (maxStringLength < 1) throw new ArgumentOutOfRangeException(nameof(maxStringLength));
        return this with
        {
            Role = Limit(Role, maxStringLength),
            AccessibleName = Limit(AccessibleName, maxStringLength),
            AutomationId = Limit(AutomationId, maxStringLength),
            ControlType = Limit(ControlType, maxStringLength),
            MediaKind = Limit(MediaKind, maxStringLength)
        };
    }

    private static string? Limit(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];
}

internal sealed record OverlaySelectionItem(
    string Id,
    OverlaySelectionKind Kind,
    OverlaySelectionBounds? Bounds,
    string? Text,
    string? MediaReference,
    OverlayContextAttachmentReference? Attachment,
    OverlaySelectionSemanticMetadata? Semantic,
    string? DisplayName)
{
    public bool HasPayload => Bounds is not null
                              || !string.IsNullOrWhiteSpace(Text)
                              || !string.IsNullOrWhiteSpace(MediaReference)
                              || Attachment is not null
                              || Semantic is not null
                              || !string.IsNullOrWhiteSpace(DisplayName);

    public OverlaySelectionItem Bound(int maxTextLength = 8_192, int maxDisplayNameLength = 512)
    {
        if (maxTextLength < 1) throw new ArgumentOutOfRangeException(nameof(maxTextLength));
        if (maxDisplayNameLength < 1) throw new ArgumentOutOfRangeException(nameof(maxDisplayNameLength));
        return this with
        {
            Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Limit(Id, 256)!,
            Text = Limit(Text, maxTextLength),
            DisplayName = Limit(DisplayName, maxDisplayNameLength),
            MediaReference = Limit(MediaReference, 4_096),
            Semantic = Semantic?.Bound()
        };
    }

    private static string? Limit(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];
}

internal sealed record OverlayContextEnvelope(
    OverlayContextKind Kind,
    string? SelectedText,
    List<OverlayContextAttachmentReference> Attachments,
    string? MediaReference,
    OverlayContextProvenance Provenance,
    bool WasTruncated = false,
    List<OverlaySelectionItem>? Selections = null)
{
    public IReadOnlyList<OverlaySelectionItem> SelectedItems => Selections ?? [];

    public bool HasTextualSelection =>
        !string.IsNullOrWhiteSpace(SelectedText)
        || Kind is OverlayContextKind.Text or OverlayContextKind.Mixed
        || SelectedItems.Any(item => item.Kind == OverlaySelectionKind.Text);

    public bool HasVisualSelection =>
        Kind is OverlayContextKind.Image or OverlayContextKind.Region or OverlayContextKind.Mixed or OverlayContextKind.Video or OverlayContextKind.Window or OverlayContextKind.Screen
        || SelectedItems.Any(item => item.Kind is OverlaySelectionKind.Image or OverlaySelectionKind.Region or OverlaySelectionKind.Video or OverlaySelectionKind.Window or OverlaySelectionKind.Screen);

    public bool HasInteractiveSelection =>
        Kind == OverlayContextKind.UiComponent
        || SelectedItems.Any(item => item.Kind == OverlaySelectionKind.UiComponent);

    public bool HasMediaSelection =>
        Kind == OverlayContextKind.Video
        || SelectedItems.Any(item => item.Kind == OverlaySelectionKind.Video);

    public bool HasWindowOrScreenSelection =>
        Kind is OverlayContextKind.Window or OverlayContextKind.Screen
        || SelectedItems.Any(item => item.Kind is OverlaySelectionKind.Window or OverlaySelectionKind.Screen);

    public bool HasPayload => !string.IsNullOrWhiteSpace(SelectedText)
                              || Attachments.Count > 0
                              || !string.IsNullOrWhiteSpace(MediaReference)
                              || SelectedItems.Any(item => item.HasPayload);

    public bool IsExpired(DateTimeOffset now) => now >= Provenance.ExpiresAt;

    public OverlayContextEnvelope Bound(
        int maxTextLength = 32_768,
        int maxAttachments = 8,
        int maxSelections = 16)
    {
        if (maxTextLength < 1) throw new ArgumentOutOfRangeException(nameof(maxTextLength));
        if (maxAttachments < 0) throw new ArgumentOutOfRangeException(nameof(maxAttachments));
        if (maxSelections < 0) throw new ArgumentOutOfRangeException(nameof(maxSelections));

        var text = SelectedText;
        var truncated = WasTruncated;
        if (text is { Length: > 0 } && text.Length > maxTextLength)
        {
            text = text[..maxTextLength];
            truncated = true;
        }

        var attachments = Attachments.Take(maxAttachments).ToList();
        if (attachments.Count != Attachments.Count) truncated = true;

        var selections = SelectedItems.Take(maxSelections).Select(item => item.Bound()).ToList();
        if (selections.Count != SelectedItems.Count) truncated = true;
        if (SelectedItems.Zip(selections).Any(pair => pair.First.Text?.Length != pair.Second.Text?.Length
                                                     || pair.First.DisplayName?.Length != pair.Second.DisplayName?.Length
                                                     || pair.First.MediaReference?.Length != pair.Second.MediaReference?.Length))
            truncated = true;

        return this with
        {
            SelectedText = text,
            Attachments = attachments,
            Selections = selections,
            WasTruncated = truncated
        };
    }
}

internal sealed record OverlaySurfaceGeometry(double Width, double Height, double X, double Y)
{
    public static OverlaySurfaceGeometry Default => new(520, 620, 120, 120);

    public OverlaySurfaceGeometry Bound() => this with
    {
        Width = Math.Clamp(Width, 240, 1600),
        Height = Math.Clamp(Height, 160, 1200)
    };
}

internal sealed record OverlaySessionState(
    Guid Id,
    string AppKey,
    string Title,
    Guid? ThreadId,
    bool IsPinned,
    bool IsVisible,
    OverlaySurfaceGeometry Geometry,
    OverlayContextEnvelope? Context,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? SourceAssociation);

internal sealed class OverlayWorkspacePersistedState
{
    public int Version { get; init; } = 1;
    public List<OverlaySessionState> PinnedSessions { get; init; } = [];
}

internal sealed record OverlayWorkspaceSnapshot(Guid? ActiveSessionId, IReadOnlyList<OverlaySessionState> Sessions);

internal sealed record OverlayContextActionDescriptor(
    string Id,
    string Label,
    string IconKey,
    bool RequiresContext,
    bool IsGenerated = false,
    string? ToolName = null,
    Haven.Core.CapabilityRiskClass? RiskClass = null,
    Haven.Core.CapabilityAvailability? Availability = null,
    string? ProviderId = null,
    string? ImplementationKey = null);

internal static class OverlayContextActionCatalog
{
    public static IReadOnlyList<OverlayContextActionDescriptor> BuildFixed(OverlayContextEnvelope? context)
    {
        var actions = new List<OverlayContextActionDescriptor>
        {
            Action("paste", "Paste", "paste", false)
        };

        if (context is null || !context.HasPayload) return actions;

        actions.Add(Action("ask-haven", "Ask Haven", "sparkles", true));
        actions.Add(Action("share", "Share", "share", true));

        if (context.HasTextualSelection)
        {
            actions.Add(Action("copy", "Copy text", "copy", true));
            actions.Add(Action("cut", "Cut text", "cut", true));
            actions.Add(Action("explain", "Explain", "info", true));
            actions.Add(Action("summarise", "Summarise", "summary", true));
            actions.Add(Action("rewrite", "Rewrite", "edit", true));
            actions.Add(Action("add-task", "Add as task", "tasks", true));
            actions.Add(Action("add-plan", "Add to Plan", "calendar", true));
            actions.Add(Action("send-study", "Send to Study", "study", true));
            actions.Add(Action("search", "Search", "search", true));
            actions.Add(Action("save-write", "Save to Write", "write", true));
        }

        if (context.HasVisualSelection)
        {
            actions.Add(Action("analyse", "Analyse", "vision", true));
            actions.Add(Action("visual-search", "Search visually", "search", true));
            actions.Add(Action("send-vision", "Send to Vision", "vision", true));
            if (!context.HasMediaSelection)
                actions.Add(Action("ocr-copy", "Extract text", "scan", true));
        }

        if (context.HasInteractiveSelection)
        {
            actions.Add(Action("inspect-control", "Inspect control", "info", true));
            actions.Add(Action("run-automation", "Run automation", "automation", true));
            actions.Add(Action("open-in-app", "Open in app", "apps", true));
        }

        if (context.HasMediaSelection)
        {
            actions.Add(Action("analyse-frame", "Analyse this frame", "vision", true));
            actions.Add(Action("summarise-media", "Summarise visible media", "summary", true));
        }

        return actions
            .GroupBy(action => action.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static OverlayContextActionDescriptor Action(string id, string label, string iconKey, bool requiresContext) =>
        new(id, label, iconKey, requiresContext);
}
#endif

using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

public sealed class NotesMediaTransformState
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public bool LockAspectRatio { get; set; } = true;
    public bool FlipHorizontal { get; set; }
    public bool FlipVertical { get; set; }
    public double Opacity { get; set; } = 1;
    public double Brightness { get; set; }
    public double Contrast { get; set; }
    public double Saturation { get; set; }
    public double AnchorX { get; set; }
    public double AnchorY { get; set; }
    public string AnchorMode { get; set; } = "Character";
    public string Transcript { get; set; } = string.Empty;
    public string Captions { get; set; } = string.Empty;
    public string PosterFramePath { get; set; } = string.Empty;
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record NotesMediaVerification(
    string Path,
    long SizeBytes,
    string Sha256,
    bool SizeMatches,
    bool HashMatches,
    DateTimeOffset VerifiedAt);

public interface INotesMediaAssetService
{
    Task<NotesMediaVerification> VerifyAsync(NotesMediaData media, CancellationToken cancellationToken);
    Task<NotesMediaData> ReplaceAsync(NotesMediaData current, string sourcePath, CancellationToken cancellationToken);
    Task<string> SaveCopyAsync(NotesMediaData media, string destinationPath, CancellationToken cancellationToken);
    Task OpenAsync(NotesMediaData media, CancellationToken cancellationToken);
}

public static class NotesMediaTransformStore
{
    public const string MetadataKey = "haven.notes.media-transform.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static NotesMediaTransformState Load(NotesBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        if (!block.Metadata.TryGetValue(MetadataKey, out var json) || string.IsNullOrWhiteSpace(json))
            return new NotesMediaTransformState();
        if (json.Length > 1_000_000) throw new InvalidDataException("The Notes media transform state is too large.");
        var state = JsonSerializer.Deserialize<NotesMediaTransformState>(json, JsonOptions)
                    ?? throw new InvalidDataException("The Notes media transform state was empty.");
        if (state.SchemaVersion != NotesMediaTransformState.CurrentSchemaVersion)
            throw new InvalidDataException("Unsupported Notes media transform schema.");
        Normalize(state);
        return state;
    }

    public static void Save(NotesBlock block, NotesMediaTransformState state)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(state);
        state.SchemaVersion = NotesMediaTransformState.CurrentSchemaVersion;
        Normalize(state);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        if (json.Length > 1_000_000) throw new InvalidDataException("The Notes media transform state is too large.");
        block.Metadata[MetadataKey] = json;
    }

    public static void ApplyCrop(NotesMediaData media, double left, double top, double right, double bottom)
    {
        ArgumentNullException.ThrowIfNull(media);
        media.CropLeft = Math.Clamp(left, 0, 0.99);
        media.CropTop = Math.Clamp(top, 0, 0.99);
        media.CropRight = Math.Clamp(right, 0, 0.99);
        media.CropBottom = Math.Clamp(bottom, 0, 0.99);
        if (media.CropLeft + media.CropRight >= 1)
        {
            media.CropLeft = 0;
            media.CropRight = 0;
            throw new ArgumentException("Horizontal crop values must leave visible image width.");
        }
        if (media.CropTop + media.CropBottom >= 1)
        {
            media.CropTop = 0;
            media.CropBottom = 0;
            throw new ArgumentException("Vertical crop values must leave visible image height.");
        }
    }

    public static void Resize(NotesMediaData media, NotesMediaTransformState state, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(state);
        width = Math.Clamp(width, 1, 10_000);
        height = Math.Clamp(height, 1, 10_000);
        if (state.LockAspectRatio && media.Width > 0 && media.Height > 0)
        {
            var ratio = media.Width / media.Height;
            var widthChange = Math.Abs(width - media.Width) / Math.Max(media.Width, 1);
            var heightChange = Math.Abs(height - media.Height) / Math.Max(media.Height, 1);
            if (widthChange >= heightChange) height = width / ratio;
            else width = height * ratio;
        }
        media.Width = Math.Clamp(width, 1, 10_000);
        media.Height = Math.Clamp(height, 1, 10_000);
    }

    private static void Normalize(NotesMediaTransformState state)
    {
        state.Opacity = ClampFinite(state.Opacity, 1, 0, 1);
        state.Brightness = ClampFinite(state.Brightness, 0, -1, 1);
        state.Contrast = ClampFinite(state.Contrast, 0, -1, 1);
        state.Saturation = ClampFinite(state.Saturation, 0, -1, 1);
        state.AnchorX = ClampFinite(state.AnchorX, 0, -1_000_000, 1_000_000);
        state.AnchorY = ClampFinite(state.AnchorY, 0, -1_000_000, 1_000_000);
        state.AnchorMode = state.AnchorMode is "Character" or "Paragraph" or "Page"
            ? state.AnchorMode
            : "Character";
        state.Transcript ??= string.Empty;
        state.Captions ??= string.Empty;
        state.PosterFramePath ??= string.Empty;
        state.Metadata = state.Metadata is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(state.Metadata, StringComparer.OrdinalIgnoreCase);
    }

    private static double ClampFinite(double value, double fallback, double minimum, double maximum) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
}

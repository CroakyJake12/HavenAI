/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/NotesMediaServices.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns NotesMediaTransformState, NotesMediaVerification, INotesMediaAssetService, NotesMediaTransformStore. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Represents notes media transform state and keeps its related state and behavior together.
/// </summary>
public sealed class NotesMediaTransformState
{
    /// <summary>
    /// Stores current schema version locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public const int CurrentSchemaVersion = 1;
    /// <summary>
    /// Gets or updates schema version, the bindable or domain state represented by this property.
    /// </summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    /// <summary>
    /// Gets or updates lock aspect ratio, the bindable or domain state represented by this property.
    /// </summary>
    public bool LockAspectRatio { get; set; } = true;
    /// <summary>
    /// Gets or updates flip horizontal, the bindable or domain state represented by this property.
    /// </summary>
    public bool FlipHorizontal { get; set; }
    /// <summary>
    /// Gets or updates flip vertical, the bindable or domain state represented by this property.
    /// </summary>
    public bool FlipVertical { get; set; }
    /// <summary>
    /// Gets or updates opacity, the bindable or domain state represented by this property.
    /// </summary>
    public double Opacity { get; set; } = 1;
    /// <summary>
    /// Gets or updates brightness, the bindable or domain state represented by this property.
    /// </summary>
    public double Brightness { get; set; }
    /// <summary>
    /// Gets or updates contrast, the bindable or domain state represented by this property.
    /// </summary>
    public double Contrast { get; set; }
    /// <summary>
    /// Gets or updates saturation, the bindable or domain state represented by this property.
    /// </summary>
    public double Saturation { get; set; }
    /// <summary>
    /// Gets or updates anchor x, the bindable or domain state represented by this property.
    /// </summary>
    public double AnchorX { get; set; }
    /// <summary>
    /// Gets or updates anchor y, the bindable or domain state represented by this property.
    /// </summary>
    public double AnchorY { get; set; }
    /// <summary>
    /// Gets or updates anchor mode, the bindable or domain state represented by this property.
    /// </summary>
    public string AnchorMode { get; set; } = "Character";
    /// <summary>
    /// Gets or updates transcript, the bindable or domain state represented by this property.
    /// </summary>
    public string Transcript { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates captions, the bindable or domain state represented by this property.
    /// </summary>
    public string Captions { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates poster frame path, the bindable or domain state represented by this property.
    /// </summary>
    public string PosterFramePath { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates metadata, the bindable or domain state represented by this property.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Represents notes media verification and keeps its related state and behavior together.
/// </summary>
public sealed record NotesMediaVerification(
    string Path,
    long SizeBytes,
    string Sha256,
    bool SizeMatches,
    bool HashMatches,
    DateTimeOffset VerifiedAt);

/// <summary>
/// Defines the i notes media asset service contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface INotesMediaAssetService
{
    Task<NotesMediaVerification> VerifyAsync(NotesMediaData media, CancellationToken cancellationToken);
    Task<NotesMediaData> ReplaceAsync(NotesMediaData current, string sourcePath, CancellationToken cancellationToken);
    Task<string> SaveCopyAsync(NotesMediaData media, string destinationPath, CancellationToken cancellationToken);
    Task OpenAsync(NotesMediaData media, CancellationToken cancellationToken);
}

/// <summary>
/// Represents notes media transform store and keeps its related state and behavior together.
/// </summary>
public static class NotesMediaTransformStore
{
    /// <summary>
    /// Stores metadata key locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public const string MetadataKey = "haven.notes.media-transform.v1";
    /// <summary>
    /// Stores json options locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    /// <summary>
    /// Performs the load step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the save step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the apply crop step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the resize step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the normalize step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the clamp finite step owned by this component.
    /// </summary>
    private static double ClampFinite(double value, double fallback, double minimum, double maximum) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
}

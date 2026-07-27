/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Controls/NotesTransformIdentity.cs, in the Desktop controls layer, containing reusable Avalonia behavior and visual building blocks.
 * What: This file owns Transform. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia.Media;

namespace Haven.Desktop.Controls;

/// <summary>
/// Represents transform and keeps its related state and behavior together.
/// </summary>
internal static class Transform
{
    /// <summary>
    /// Gets or updates identity, the bindable or domain state represented by this property.
    /// </summary>
    public static ITransform Identity { get; } = new ScaleTransform(1, 1);
}

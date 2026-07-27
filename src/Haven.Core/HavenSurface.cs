/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Core/HavenSurface.cs, in the dependency-free Core layer, where shared domain models and rules live.
 * What: This file owns HavenSurface. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: This code stays free of UI and storage dependencies so the same rule or data shape can be reused and tested everywhere.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

namespace Haven.Core;

/// <summary>
/// Identifies a top-level Haven product surface. Unlike <see cref="HavenMode"/>,
/// this value is UI navigation state and is deliberately not persisted in the
/// conversation database.
/// </summary>
public enum HavenSurface
{
    Home,
    Chat,
    Teach,
    Do,
    Studio,
    Browse,
    Plan,
    Training
}


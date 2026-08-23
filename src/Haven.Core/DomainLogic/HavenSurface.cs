/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Core/DomainLogic/HavenSurface.cs, in the dependency-free Core layer, where shared domain models and rules live.
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
    Home = 0,
    Chat = 1,
    Study = 2,
    Tasks = 3,
    Studio = 4,
    Browse = 5,
    Plan = 6,
    Training = 7,
    Imagine = 8,
    Present = 9,
    Data = 10,
    Vision = 11,
    Play = 12,
    Translate = 13,
    Launcher = 14,
    Go = 15,
    Dashboard = 16,
    Write = 17,
    Canvas = 18,
    Automations = 19,
    Spaces = 20,
    Terminal = 21,

    Mail = 23,
    // UI aliases retained for saved layout JSON written before the rename.
    Teach = Study,
    Do = Tasks
}

/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/InternalsVisibleTo.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns top-level declarations and support code. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Haven.Infrastructure.Tests")]

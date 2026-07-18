/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/LocalOllamaAbstractions.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns ILocalOllamaClient. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

namespace Haven.Application;

/// <summary>
/// Identifies the direct loopback Ollama transport. User-facing surfaces should
/// normally depend on <see cref="IProviderModelClient"/> or <see cref="IOllamaClient"/>,
/// while provider routing uses this boundary to avoid resolving itself recursively.
/// </summary>
public interface ILocalOllamaClient : IOllamaClient
{
}

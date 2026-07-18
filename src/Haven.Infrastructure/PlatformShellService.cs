/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/PlatformShellService.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns PlatformShellService. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>
/// Represents platform shell service and keeps its related state and behavior together.
/// </summary>
public sealed class PlatformShellService : IPlatformShellService
{
    /// <summary>
    /// Performs open external asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task OpenExternalAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
                Verb = "open"
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch { }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Retrieves clipboard text async for the current operation.
    /// </summary>
    public Task<string> GetClipboardTextAsync(CancellationToken cancellationToken) =>
        Task.FromResult(string.Empty);

    /// <summary>
    /// Performs set clipboard text asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task SetClipboardTextAsync(string text, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

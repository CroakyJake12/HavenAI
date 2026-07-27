/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Services/OllamaWakeService.cs in the Windows desktop-services layer.
 * What: Starts a locally installed Ollama server on demand and waits until its HTTP endpoint responds.
 * How: ChatPageViewModel calls this service only for unqualified local-model names after an availability check fails.
 * Why: Process launching is a platform concern and should not be embedded in the chat presentation model.
 * Maintenance: Keep launch attempts bounded, hidden, cancellable, and harmless when Ollama is already running.
 */

using System.ComponentModel;
using System.Diagnostics;
using Haven.Application;

namespace Haven.Desktop.Services;

/// <summary>
/// Wakes a local Ollama installation without blocking the UI and verifies that it
/// is genuinely reachable before chat continues.
/// </summary>
public sealed class OllamaWakeService(IOllamaClient models)
{
    /// <summary>
    /// Probes only the local Ollama endpoint.  This deliberately lives on the
    /// wake service instead of the provider-routing client, because a reachable
    /// cloud provider must not make Haven think that local Ollama is running.
    /// </summary>
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
        models.IsAvailableAsync(cancellationToken);

    /// <summary>
    /// Returns immediately when Ollama is available; otherwise starts `ollama serve`
    /// and polls for up to fifteen seconds so send can produce a useful outcome.
    /// </summary>
    public async Task<bool> EnsureAvailableAsync(CancellationToken cancellationToken)
    {
        if (await models.IsAvailableAsync(cancellationToken).ConfigureAwait(false)) return true;

        try
        {
            var executable = ResolveExecutable();
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "serve",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return false;
        }

        for (var attempt = 0; attempt < 30; attempt++)
        {
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            if (await models.IsAvailableAsync(cancellationToken).ConfigureAwait(false)) return true;
        }
        return false;
    }

    /// <summary>Prefers Ollama's standard Windows install path and otherwise uses PATH resolution.</summary>
    private static string ResolveExecutable()
    {
        var installed = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Ollama", "ollama.exe");
        return File.Exists(installed) ? installed : "ollama.exe";
    }
}

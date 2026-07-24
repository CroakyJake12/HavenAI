using System.Diagnostics;
using System.Text.Json;

namespace Haven.Desktop.Services;

public sealed record VisualStudioInstallation(
    string DisplayName,
    string InstallationPath,
    string InstallationVersion,
    bool IsComplete,
    bool IsLaunchable,
    bool IsConnected);

/// <summary>
/// Detects local Visual Studio installations through Microsoft's bundled vswhere utility
/// and supports an explicitly connected installation folder. Detection is read-only.
/// </summary>
public sealed class VisualStudioInstallationService
{
    private const string VsWhereRelativePath = "Microsoft Visual Studio/Installer/vswhere.exe";
    private readonly object _gate = new();
    private string? _connectedPath;

    public string? ConnectedPath
    {
        get
        {
            lock (_gate)
            {
                return _connectedPath;
            }
        }
    }

    public bool TryConnect(string installationPath, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(installationPath))
        {
            error = "Choose a Visual Studio installation folder.";
            return false;
        }

        string canonical;
        try
        {
            canonical = Path.GetFullPath(installationPath.Trim());
        }
        catch (Exception)
        {
            error = "That installation path is invalid.";
            return false;
        }

        if (!Directory.Exists(canonical) || !LooksLikeVisualStudio(canonical))
        {
            error = "The selected folder does not look like a complete Visual Studio installation.";
            return false;
        }

        lock (_gate)
        {
            _connectedPath = canonical;
        }

        return true;
    }

    public void Disconnect()
    {
        lock (_gate)
        {
            _connectedPath = null;
        }
    }

    public async Task<IReadOnlyList<VisualStudioInstallation>> GetAvailableInstallationsAsync(
        CancellationToken cancellationToken)
    {
        var results = new List<VisualStudioInstallation>();
        var connected = ConnectedPath;

        if (!string.IsNullOrWhiteSpace(connected) &&
            Directory.Exists(connected) &&
            LooksLikeVisualStudio(connected))
        {
            results.Add(new VisualStudioInstallation(
                "Connected Visual Studio",
                connected,
                string.Empty,
                true,
                true,
                true));
        }

        if (!OperatingSystem.IsWindows())
        {
            return results;
        }

        var vsWherePath = GetVsWherePath();
        if (vsWherePath is null)
        {
            return results;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = vsWherePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in new[]
        {
            "-all",
            "-products", "*",
            "-prerelease",
            "-format", "json",
            "-property", "installationPath",
            "-property", "catalogProductDisplayVersion",
            "-property", "displayName",
            "-property", "isComplete",
            "-property", "isLaunchable"
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            return results;
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var json = await outputTask.ConfigureAwait(false);
        _ = await errorTask.ConfigureAwait(false);

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(json))
        {
            return results;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            foreach (var item in document.RootElement.EnumerateArray())
            {
                var path = GetString(item, "installationPath");
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                {
                    continue;
                }

                if (results.Any(existing =>
                        string.Equals(existing.InstallationPath, path, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                results.Add(new VisualStudioInstallation(
                    GetString(item, "displayName") ?? "Visual Studio",
                    path,
                    GetString(item, "catalogProductDisplayVersion") ?? string.Empty,
                    GetBoolean(item, "isComplete"),
                    GetBoolean(item, "isLaunchable"),
                    false));
            }
        }
        catch (JsonException)
        {
            return results;
        }

        return results
            .OrderByDescending(item => item.IsConnected)
            .ThenByDescending(item => item.IsLaunchable)
            .ThenByDescending(item => item.IsComplete)
            .ToArray();
    }

    public async Task<bool> HasAvailableInstallationAsync(CancellationToken cancellationToken)
    {
        var installations = await GetAvailableInstallationsAsync(cancellationToken).ConfigureAwait(false);
        return installations.Any(item => item.IsConnected || (item.IsComplete && item.IsLaunchable));
    }

    private static string? GetVsWherePath()
    {
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var candidates = new[]
        {
            string.IsNullOrWhiteSpace(programFilesX86)
                ? string.Empty
                : Path.Combine(programFilesX86, VsWhereRelativePath),
            Path.Combine(AppContext.BaseDirectory, "vswhere.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static bool LooksLikeVisualStudio(string path) =>
        File.Exists(Path.Combine(path, "Common7", "IDE", "devenv.exe")) ||
        File.Exists(Path.Combine(path, "MSBuild", "Current", "Bin", "MSBuild.exe"));

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool GetBoolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
        value.GetBoolean();
}

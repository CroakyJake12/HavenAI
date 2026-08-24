using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class NativePluginProcessFactory : INativePluginProcessFactory
{
    public INativePluginProcess Create(InstalledExtensionPackage package) => new NativePluginProcess(package);
}

internal sealed class NativePluginProcess(InstalledExtensionPackage package) : INativePluginProcess
{
    private bool _started;
    public string PackageId => package.Manifest.PackageId;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await ExtensionPackageIntegrity.VerifyInstalledAsync(package, cancellationToken).ConfigureAwait(false);
        foreach (var capability in package.Manifest.Capabilities)
        {
            var path = Resolve(capability.EntryPoint);
            if (!File.Exists(path)) throw new FileNotFoundException($"Plugin entry point '{capability.EntryPoint}' was not found.", path);
        }
        _started = true;
    }

    public async Task<string> InvokeAsync(string capabilityId, string redactedArgumentsJson, CancellationToken cancellationToken)
    {
        if (!_started) throw new InvalidOperationException("Plugin process boundary has not been started.");
        var capability = package.Manifest.Capabilities.FirstOrDefault(item => item.Id.Equals(capabilityId, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException("Plugin capability was not declared.");
        await ExtensionPackageIntegrity.VerifyInstalledAsync(package, cancellationToken).ConfigureAwait(false);
        var entryPoint = Resolve(capability.EntryPoint);
        var start = new ProcessStartInfo
        {
            FileName = entryPoint.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? "dotnet" : entryPoint,
            WorkingDirectory = package.InstallPath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (entryPoint.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(entryPoint);
        start.ArgumentList.Add("--haven-plugin-stdio");
        start.Environment.Clear();
        start.Environment["PATH"] = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        start.Environment["DOTNET_ROOT"] = Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? string.Empty;
        start.Environment["HAVEN_PLUGIN_ID"] = package.Manifest.PackageId;
        start.Environment["HAVEN_PLUGIN_PERMISSIONS"] = ((int)package.GrantedPermissions).ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Plugin process could not be started.");
        var request = JsonSerializer.Serialize(new { capabilityId, arguments = JsonDocument.Parse(redactedArgumentsJson).RootElement });
        await process.StandardInput.WriteLineAsync(request.AsMemory(), cancellationToken).ConfigureAwait(false);
        process.StandardInput.Close();
        var outputTask = ReadLimitedAsync(process.StandardOutput, 64_000, cancellationToken);
        var errorTask = ReadLimitedAsync(process.StandardError, 16_000, cancellationToken);
        var exitTask = process.WaitForExitAsync(cancellationToken);
        try { await Task.WhenAll(exitTask, outputTask, errorTask).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        var output = outputTask.Result;
        var error = errorTask.Result;
        if (process.ExitCode != 0) throw new InvalidOperationException(SensitiveTextRedactor.Redact(error, 8_000));
        return SensitiveTextRedactor.Redact(output, 64_000);
    }

    public Task StopAsync(CancellationToken cancellationToken) { _started = false; return Task.CompletedTask; }
    public ValueTask DisposeAsync() { _started = false; return ValueTask.CompletedTask; }

    private static async Task<string> ReadLimitedAsync(StreamReader reader, int maximumCharacters, CancellationToken cancellationToken)
    {
        var result = new StringBuilder(Math.Min(maximumCharacters, 4_096));
        var buffer = new char[4_096];
        var exceeded = false;
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            var remaining = maximumCharacters - result.Length;
            if (remaining > 0) result.Append(buffer, 0, Math.Min(read, remaining));
            if (read > remaining) exceeded = true;
        }
        if (exceeded) throw new InvalidDataException($"Plugin output exceeded the {maximumCharacters:N0}-character host limit.");
        return result.ToString();
    }

    private string Resolve(string relative)
    {
        var root = Path.GetFullPath(package.InstallPath) + Path.DirectorySeparatorChar;
        var result = Path.GetFullPath(Path.Combine(package.InstallPath, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!result.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Plugin entry point escaped the installed package.");
        var current = Path.GetFullPath(package.InstallPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("Installed plugin roots cannot be linked directories.");
        foreach (var segment in Path.GetRelativePath(current, result).Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (File.Exists(current) || Directory.Exists(current))
                if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                    throw new InvalidDataException("Installed plugins cannot execute linked content.");
        }
        return result;
    }
}

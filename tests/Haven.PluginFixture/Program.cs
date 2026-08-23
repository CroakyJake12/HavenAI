using System.Text.Json;

namespace Haven.PluginFixture;

public sealed class FixtureMarker { }

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (!args.Contains("--haven-plugin-stdio", StringComparer.Ordinal))
            return 64;

        var input = await Console.In.ReadLineAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(input))
            return 65;

        using var document = JsonDocument.Parse(input);
        var root = document.RootElement;
        var capabilityId = root.GetProperty("capabilityId").GetString() ?? string.Empty;
        var arguments = root.GetProperty("arguments");

        var markerName = arguments.TryGetProperty("markerName", out var markerElement)
            ? markerElement.GetString()
            : null;
        if (!string.IsNullOrWhiteSpace(markerName))
            await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(), markerName), capabilityId).ConfigureAwait(false);

        if (capabilityId.Equals("fixture.crash", StringComparison.Ordinal))
        {
            Console.Error.Write("Fixture crash token=fixture-process-secret-456");
            return 23;
        }

        var message = arguments.TryGetProperty("message", out var messageElement)
            ? messageElement.GetString()
            : null;
        var token = arguments.TryGetProperty("token", out var tokenElement)
            ? tokenElement.GetString()
            : null;

        Console.Out.Write(JsonSerializer.Serialize(new
        {
            capabilityId,
            message,
            token,
            processId = Environment.ProcessId,
            commandLine = Environment.CommandLine,
            packageId = Environment.GetEnvironmentVariable("HAVEN_PLUGIN_ID"),
            permissions = Environment.GetEnvironmentVariable("HAVEN_PLUGIN_PERMISSIONS")
        }));
        return 0;
    }
}

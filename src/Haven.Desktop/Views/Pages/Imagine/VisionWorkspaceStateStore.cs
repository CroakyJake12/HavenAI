using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Haven.Desktop.Views.Pages.Imagine;

internal sealed record VisionWorkspaceState(
    string? SourcePath,
    string Question,
    string Response,
    string Model,
    string? AnalysisKey)
{
    public static VisionWorkspaceState Empty { get; } = new(null, string.Empty, string.Empty, string.Empty, null);
}

internal sealed class VisionWorkspaceStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;
    private readonly string _assetDirectory;

    public VisionWorkspaceStateStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Haven",
            "vision-workspace.json");
        _assetDirectory = Path.Combine(Path.GetDirectoryName(_path)!, "Vision");
    }

    public async Task<VisionWorkspaceState> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path)) return VisionWorkspaceState.Empty;
            try
            {
                await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                return await JsonSerializer.DeserializeAsync<VisionWorkspaceState>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                       ?? VisionWorkspaceState.Empty;
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                return VisionWorkspaceState.Empty;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(VisionWorkspaceState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_path) ?? throw new InvalidOperationException("The Vision state path has no directory.");
            Directory.CreateDirectory(directory);
            var temporaryPath = _path + ".tmp";
            await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporaryPath, _path, true);
        }
        finally { _gate.Release(); }
    }

    public async Task<string> PersistSourceAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) throw new FileNotFoundException("The Vision source image is unavailable.", sourcePath);
        Directory.CreateDirectory(_assetDirectory);
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension)) extension = ".img";
        var target = Path.Combine(_assetDirectory, "current" + extension);
        if (Path.GetFullPath(sourcePath).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) return target;
        await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(target + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Close();
        File.Move(target + ".tmp", target, true);
        return target;
    }

    internal static async Task<string> BuildAnalysisKeyAsync(string imagePath, string model, string prompt, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0) hash.AppendData(buffer, 0, read);
        hash.AppendData(Encoding.UTF8.GetBytes("\nmodel:" + model + "\nprompt:" + prompt));
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}

internal sealed record VisionHandoff(string SourcePath, string Response, string Model);

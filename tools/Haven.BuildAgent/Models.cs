using System.Text.Json.Serialization;

namespace Haven.BuildAgent;

public sealed class BuildAgentOptions
{
    public const string SectionName = "BuildAgent";

    public string RepositoryRoot { get; set; } = ".";

    public string ArtifactRoot { get; set; } = ".haven-agent/artifacts";

    public string VisualModel { get; set; } = string.Empty;

    public Dictionary<string, BuildProfile> BuildProfiles { get; set; } = [];

    public Dictionary<string, RunProfile> RunProfiles { get; set; } = [];

    public Dictionary<string, string> ReferenceImages { get; set; } = [];

    public string RepositoryRootPath => Path.GetFullPath(Environment.ExpandEnvironmentVariables(RepositoryRoot));

    public string ArtifactRootPath => ResolveRepositoryPath(ArtifactRoot);

    public void Validate()
    {
        if (!Directory.Exists(RepositoryRootPath))
        {
            throw new DirectoryNotFoundException($"Repository root does not exist: {RepositoryRootPath}");
        }

        if (BuildProfiles.Count == 0)
        {
            throw new InvalidOperationException("At least one BuildProfiles entry is required.");
        }

        if (RunProfiles.Count == 0)
        {
            throw new InvalidOperationException("At least one RunProfiles entry is required.");
        }

        Directory.CreateDirectory(ArtifactRootPath);
    }

    public BuildProfile GetBuildProfile(string key)
    {
        return FindValue(BuildProfiles, key, "build profile");
    }

    public RunProfile GetRunProfile(string key)
    {
        return FindValue(RunProfiles, key, "run profile");
    }

    public string GetReferenceImagePath(string key)
    {
        string relativePath = FindValue(ReferenceImages, key, "reference image");
        string fullPath = ResolveRepositoryPath(relativePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Reference image '{key}' was not found.", fullPath);
        }

        return fullPath;
    }

    public string ResolveRepositoryPath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        string root = RepositoryRootPath;
        string candidate = Path.GetFullPath(
            Path.IsPathRooted(relativePath)
                ? Environment.ExpandEnvironmentVariables(relativePath)
                : Path.Combine(root, Environment.ExpandEnvironmentVariables(relativePath)));

        EnsureContained(root, candidate);
        return candidate;
    }

    public string CreateArtifactDirectory(string category, Guid id)
    {
        string safeCategory = string.Concat(category.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_'));
        if (safeCategory.Length == 0)
        {
            throw new ArgumentException("Artifact category must contain at least one safe character.", nameof(category));
        }

        string path = Path.Combine(ArtifactRootPath, safeCategory, id.ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public string ToArtifactUrl(string absolutePath)
    {
        string fullPath = Path.GetFullPath(absolutePath);
        EnsureContained(ArtifactRootPath, fullPath);

        string relative = Path.GetRelativePath(ArtifactRootPath, fullPath);
        string[] segments = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        return "/artifacts/" + string.Join('/', segments.Select(Uri.EscapeDataString));
    }

    private static TValue FindValue<TValue>(IReadOnlyDictionary<string, TValue> dictionary, string key, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        foreach ((string candidateKey, TValue value) in dictionary)
        {
            if (string.Equals(candidateKey, key, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        throw new KeyNotFoundException($"Unknown {description} '{key}'. Allowed values: {string.Join(", ", dictionary.Keys)}");
    }

    private static void EnsureContained(string root, string candidate)
    {
        string relative = Path.GetRelativePath(root, candidate);
        if (Path.IsPathRooted(relative)
            || relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Path escapes the configured repository root: {candidate}");
        }
    }
}

public sealed class BuildProfile
{
    public string Target { get; set; } = "Haven.sln";
}

public sealed class RunProfile
{
    public string Executable { get; set; } = string.Empty;

    public string WorkingDirectory { get; set; } = ".";

    public string WindowTitleContains { get; set; } = "Haven";
}

public sealed record BuildRequest(
    string Profile = "haven",
    string Configuration = "Debug",
    string Verbosity = "minimal");

public sealed record TestRequest(
    string Profile = "haven",
    string Configuration = "Debug",
    string Verbosity = "minimal",
    bool NoBuild = false);

public sealed record StartRunRequest(
    string Profile = "haven-desktop",
    string Configuration = "Debug",
    bool FreshDataProfile = true);

public sealed record CaptureRequest(Guid RunId, int WaitSeconds = 20);

public sealed record VisualCompareRequest(
    Guid RunId,
    string ReferenceKey,
    bool UseAiReview = true,
    int WaitSeconds = 20,
    int PixelThreshold = 20,
    string? Focus = null);

public sealed record BuildDiagnostic(
    string Severity,
    string Code,
    string Message,
    string? File,
    int? Line,
    int? Column,
    string? Project,
    string? Origin);

public sealed record TestSummary(int Failed, int Passed, int Skipped, int Total);

public sealed record JobSnapshot(
    Guid Id,
    string Kind,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int? ExitCode,
    IReadOnlyList<BuildDiagnostic> Diagnostics,
    TestSummary? Tests,
    string? ConsoleLogUrl,
    string? MsBuildLogUrl,
    string? BinaryLogUrl,
    string? TestResultsUrl,
    string? Failure);

public sealed record RunSnapshot(
    Guid Id,
    string Profile,
    string Configuration,
    string Status,
    int ProcessId,
    DateTimeOffset StartedAt,
    DateTimeOffset? ExitedAt,
    int? ExitCode,
    string LogUrl,
    string? DataProfilePath,
    string? Failure);

public sealed record CaptureResult(
    Guid RunId,
    int Width,
    int Height,
    DateTimeOffset CapturedAt,
    string ArtifactUrl,
    [property: JsonIgnore] string AbsolutePath);

public sealed record DifferenceBounds(int Left, int Top, int Right, int Bottom);

public sealed record PixelComparisonResult(
    double SimilarityPercent,
    double ChangedPixelPercent,
    bool DimensionsMatch,
    int ActualWidth,
    int ActualHeight,
    int ReferenceWidth,
    int ReferenceHeight,
    int PixelThreshold,
    DifferenceBounds? DifferenceBounds,
    string DifferenceImageUrl);

public sealed record VisualComparisonResult(
    CaptureResult Actual,
    string ReferenceKey,
    PixelComparisonResult PixelComparison,
    string AiReviewStatus,
    string? AiReview);

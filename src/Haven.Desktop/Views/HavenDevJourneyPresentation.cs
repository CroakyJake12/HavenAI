namespace Haven.Desktop.Views;

/// <summary>
/// Presentation-only Haven Dev state used by the existing Project HUI scene.
/// Real workspace, process, diagnostic, device and source-control ownership belongs to Haven Dev Core providers.
/// </summary>
internal enum HavenDevExplorerMode
{
    Logical,
    Filesystem
}

internal enum HavenDevTool
{
    Build,
    Problems,
    Device,
    Logcat,
    Tests,
    Changes
}

internal sealed record HavenDevDiagnosticPresentation(
    string RelativePath,
    int Line,
    string Severity,
    string Code,
    string Message);

internal sealed record HavenDevJourneyPresentationState(
    string EvidenceLabel,
    HavenDevExplorerMode ExplorerMode,
    IReadOnlyList<string> ExplorerRows,
    string ActivePath,
    HavenDevTool ActiveTool,
    string ToolOutput,
    bool CanDeploy,
    string DeployStatus,
    HavenDevDiagnosticPresentation? Diagnostic)
{
    public static string SimulatedEvidenceLabel => "SIMULATED — NOT REAL AOSP VALIDATION";
}

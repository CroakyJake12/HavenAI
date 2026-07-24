from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]

def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count == 0 and new in text:
        return text
    if count != 1:
        raise RuntimeError(f"{label}: expected one match, found {count}")
    return text.replace(old, new, 1)

responsive_path = ROOT / "src/Haven.Application/ResponsiveCallCoordinator.cs"
responsive = responsive_path.read_text(encoding="utf-8")

anchor = """    public ResponsiveCallCoordinator(
        CallCoordinator inner,
        ISpeechOutputService speechOutput,
        ISpeechOutputWarmup speechWarmup,
        CallOptimizedOllamaClient models)
"""
overload = """    public ResponsiveCallCoordinator(
        CallCoordinator inner,
        ISpeechOutputService speechOutput,
        CallOptimizedOllamaClient models)
        : this(inner, speechOutput, NoOpSpeechOutputWarmup.Instance, models)
    {
    }

"""
if "NoOpSpeechOutputWarmup.Instance" not in responsive:
    responsive = replace_once(
        responsive,
        anchor,
        overload + anchor,
        "ResponsiveCallCoordinator overload",
    )

noop = """
    private sealed class NoOpSpeechOutputWarmup : ISpeechOutputWarmup
    {
        public static NoOpSpeechOutputWarmup Instance { get; } = new();

        public Task WarmAsync(string? voiceName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
"""
if "private sealed class NoOpSpeechOutputWarmup" not in responsive:
    closing = responsive.rfind("\n}")
    if closing < 0:
        raise RuntimeError(b"ResponsiveCallCoordinator closing brace was not found.")
    responsive = responsive[:closing] + noop + responsive[closing:]

responsive_path.write_text(responsive, encoding="utf-8", newline="\n")

registration_path = ROOT / "src/Haven.Desktop/Services/DesktopCallServiceRegistration.cs"
registration = registration_path.read_text(encoding="utf-8")
registration = replace_once(
    registration,
    "        services.AddSingleton<CallVoicePreviewController>();",
    """        services.AddSingleton<CallVoicePreviewController>(provider => new CallVoicePreviewController(
            provider.GetRequiredService<ISpeechOutputService>(),
            provider.GetRequiredService<IProductionDiagnostics>()));""",
    "CallVoicePreviewController registration",
)
registration_path.write_text(registration, encoding="utf-8", newline="\n")

for source_root in (
    ROOT / "src/Haven.Application",
    ROOT / "src/Haven.Desktop",
):
    for source in source_root.rglob("*.cs"):
        text = source.read_text(encoding="utf-8")
        updated = text.replace("Haven Voice", "Haven Call")
        if updated != text:
            source.write_text(updated, encoding="utf-8", newline="\n")

print("Applied call DI fallbacks, preview isolation, and Haven Call naming.")

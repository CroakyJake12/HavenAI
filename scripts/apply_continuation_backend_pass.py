from __future__ import annotations

from pathlib import Path
import subprocess
import sys


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count == 0 and new in text:
        return text
    if count != 1:
        raise RuntimeError(f"{label}: expected one match, found {count}")
    return text.replace(old, new, 1)


root = Path(__file__).resolve().parents[1]

chat_patch_path = root / "scripts" / "apply_beta_chat_patch.py"
chat_patch = chat_patch_path.read_text(encoding="utf-8")
old_stream_transform = '''    send_text = replace_once(
        send_text,
        "        if (!canUseTools)\\n        {\\n            await foreach",
        "        if (!canUseTools)\\n"
        "        {\\n"
        "            var firstChunk = true;\\n"
        "            await foreach",
        "stream first chunk state",
    )'''
new_stream_transform = '''    send_text = replace_once(
        send_text,
        "        else\\n        {\\n            await foreach",
        "        else\\n"
        "        {\\n"
        "            var firstChunk = true;\\n"
        "            await foreach",
        "stream first chunk state",
    )'''
chat_patch = replace_once(
    chat_patch,
    old_stream_transform,
    new_stream_transform,
    "Chat streaming-state transformation",
)
chat_patch_path.write_text(chat_patch, encoding="utf-8", newline="\n")

responsive_path = root / "src" / "Haven.Application" / "ResponsiveCallCoordinator.cs"
responsive = responsive_path.read_text(encoding="utf-8")
constructor_anchor = "    public ResponsiveCallCoordinator(\n        CallCoordinator inner,"
constructor_overload = '''    public ResponsiveCallCoordinator(
        CallCoordinator inner,
        ISpeechOutputService speechOutput,
        CallOptimizedOllamaClient models)
        : this(inner, speechOutput, NoOpSpeechOutputWarmup.Instance, models)
    {
    }

'''
if "NoOpSpeechOutputWarmup.Instance" not in responsive:
    responsive = replace_once(
        responsive,
        constructor_anchor,
        constructor_overload + constructor_anchor,
        "Responsive call constructor",
    )

no_op = '''
    private sealed class NoOpSpeechOutputWarmup : ISpeechOutputWarmup
    {
        public static NoOpSpeechOutputWarmup Instance { get; } = new();

        public Task WarmAsync(string? voiceName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
'''
if "private sealed class NoOpSpeechOutputWarmup" not in responsive:
    closing = responsive.rfind("\n}")
    if closing < 0:
        raise RuntimeError("Responsive call closing brace was not found.")
    responsive = responsive[:closing] + no_op + responsive[closing:]
responsive_path.write_text(responsive, encoding="utf-8", newline="\n")

registration_path = root / "src" / "Haven.Desktop" / "Services" / "DesktopCallServiceRegistration.cs"
registration = registration_path.read_text(encoding="utf-8")
old_registration = "        services.AddSingleton<CallVoicePreviewController>();"
new_registration = '''        services.AddSingleton<CallVoicePreviewController>(provider => new CallVoicePreviewController(
            provider.GetRequiredService<ISpeechOutputService>(),
            provider.GetRequiredService<IProductionDiagnostics>()));'''
registration = replace_once(
    registration,
    old_registration,
    new_registration,
    "Call voice preview registration",
)
registration_path.write_text(registration, encoding="utf-8", newline="\n")

for source_root in (
    root / "src" / "Haven.Application",
    root / "src" / "Haven.Desktop",
):
    for source in source_root.rglob("*.cs"):
        text = source.read_text(encoding="utf-8")
        updated = text.replace("Haven Voice", "Haven Call")
        if updated != text:
            source.write_text(updated, encoding="utf-8", newline="\n")

subprocess.run(
    [sys.executable, str(chat_patch_path)],
    cwd=root,
    check=True,
)

print("Applied Chat orchestration, call DI fallbacks, and Haven Call naming.")


from pathlib import Path

def replace_idempotent(path: Path, old: str, new: str, expected: int = 1) -> None:
    text = path.read_text(encoding="utf-8")
    old_count = text.count(old)
    new_count = text.count(new)
    if old_count == expected:
        path.write_text(text.replace(old, new), encoding="utf-8")
        return
    if old_count == 0 and new_count >= expected:
        return
    raise RuntimeError(
        f"{path}: expected {expected} old occurrence(s) or an already-applied replacement; "
        f"found old={old_count}, new={new_count}. Pattern starts {old[:100]!r}"
    )

root = Path(__file__).resolve().parents[1]
control = root / "src/Haven.Desktop/Controls/ModelConfigurationControl.cs"
ollama = root / "src/Haven.Infrastructure/OllamaClient.cs"

replace_idempotent(control, "private int _effortPercent = 60;", "private int _effortPercent = 50;")

replace_idempotent(
    control,
'''        _effortSlider = new Slider
        {
            Minimum = 20,
            Maximum = 100,
            Value = _effortPercent,
            TickFrequency = 20,
            IsSnapToTickEnabled = true,
            LargeChange = 20,
            SmallChange = 20,''',
'''        _effortSlider = new Slider
        {
            Minimum = ReasoningScale.MinimumPercentage,
            Maximum = ReasoningScale.MaximumPercentage,
            Value = _effortPercent,
            TickFrequency = ReasoningScale.StepSize,
            IsSnapToTickEnabled = true,
            LargeChange = ReasoningScale.StepSize,
            SmallChange = ReasoningScale.StepSize,''')

replace_idempotent(
    control,
'''                new TextBlock
                {
                    Text = "Effort snaps to 20% increments. Higher effort gives the model more time to reason before answering.",
                    Classes = { "muted2" },
                    FontSize = 9,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center
                }''',
'''                new TextBlock
                {
                    Text = "Reasoning has four levels: 25%, 50%, 75%, and 100%. The accuracy-preserving large-model runtime activates at 100%.",
                    Classes = { "muted2" },
                    FontSize = 9,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center
                }''')

replace_idempotent(
    control,
"        var snapped = Math.Clamp((int)Math.Round(e.NewValue / 20d) * 20, 20, 100);",
"        var snapped = ReasoningScale.SnapPercentage(e.NewValue);")

replace_idempotent(
    control,
'''    private static EffortLevel EffortForPercentage(int percentage) => percentage switch
    {
        <= 20 => EffortLevel.Low,
        <= 60 => EffortLevel.Medium,
        <= 80 => EffortLevel.High,
        _ => EffortLevel.Max
    };''',
'''    private static EffortLevel EffortForPercentage(int percentage) =>
        ReasoningScale.FromPercentage(percentage);''')

replace_idempotent(
    control,
'''    private static int PercentageForEffort(EffortLevel effort) => effort switch
    {
        EffortLevel.Low => 20,
        EffortLevel.Medium => 60,
        EffortLevel.High => 80,
        EffortLevel.Max => 100,
        _ => 60
    };''',
'''    private static int PercentageForEffort(EffortLevel effort) =>
        ReasoningScale.ToPercentage(effort);''')

replace_idempotent(
    control,
'''    private static string EffortDescription(int percentage) => percentage switch
    {
        20 => "Fastest responses, least accurate",
        40 or 60 => "Balanced responses",
        80 => "Slow responses, more accurate",
        100 => "Slowest responses, most accurate",
        _ => "Balanced responses"
    };''',
'''    private static string EffortDescription(int percentage) => percentage switch
    {
        25 => "Fastest responses with a small bounded context",
        50 => "Balanced speed and reasoning",
        75 => "Deeper reasoning with a larger bounded context",
        100 => "Maximum reasoning with accuracy-preserving large-model runtime",
        _ => "Balanced speed and reasoning"
    };''')

replace_idempotent(
    ollama,
'''    private static object BuildPayload(OllamaChatRequest request, bool stream)
    {
        var messages = new List<object>();''',
'''    private static object BuildPayload(OllamaChatRequest request, bool stream)
    {
        var runtimeProfile = LocalInferenceRuntimeProfile.Create(
            request.Effort,
            request.Options?.ContextLimit ?? 32768);
        var messages = new List<object>();''')

replace_idempotent(
    ollama,
'''    private static object BuildToolPayload(OllamaToolRequest request)
    {
        var messages = new List<object>();''',
'''    private static object BuildToolPayload(OllamaToolRequest request)
    {
        var runtimeProfile = LocalInferenceRuntimeProfile.Create(
            request.Effort,
            request.Options?.ContextLimit ?? 32768);
        var messages = new List<object>();''')

replace_idempotent(
    ollama,
"                num_ctx = Math.Clamp(request.Options?.ContextLimit ?? 32768, 2048, 262144),",
"                num_ctx = runtimeProfile.ContextLimit,",
expected=2)

replace_idempotent(
    ollama,
'            keep_alive = "10m"',
'            keep_alive = runtimeProfile.KeepAlive',
expected=2)

print("Applied four-step reasoning control and accuracy-preserving Ollama runtime profile.")

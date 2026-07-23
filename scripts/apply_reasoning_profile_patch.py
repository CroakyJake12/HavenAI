from pathlib import Path

def replace_once(path: Path, old: str, new: str) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: expected one occurrence, found {count}: {old[:80]!r}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8")

def replace_all(path: Path, old: str, new: str, expected: int) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != expected:
        raise RuntimeError(f"{path}: expected {expected} occurrences, found {count}: {old[:80]!r}")
    path.write_text(text.replace(old, new), encoding="utf-8")

root = Path(__file__).resolve().parents[1]
control = root / "src/Haven.Desktop/Controls/ModelConfigurationControl.cs"
ollama = root / "src/Haven.Infrastructure/OllamaClient.cs"

replace_once(control, "private int _effortPercent = 60;", "private int _effortPercent = 50;")

replace_once(
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

replace_once(
    control,
    'Text = "Effort snaps to 20% increments. Higher effort gives the model more time to reason before answering.",',
    'Text = "Reasoning has four levels: 25%, 50%, 75%, and 100%. The accuracy-preserving large-model runtime activates at 100%.",')

replace_once(
    control,
    "        var snapped = Math.Clamp((int)Math.Round(e.NewValue / 20d) * 20, 20, 100);",
    "        var snapped = ReasoningScale.SnapPercentage(e.NewValue);")

old_effort = '''    private static EffortLevel EffortForPercentage(int percentage) => percentage switch
    {
        <= 20 => EffortLevel.Low,
        <= 60 => EffortLevel.Medium,
        <= 80 => EffortLevel.High,
        _ => EffortLevel.Max
    };'''
new_effort = '''    private static EffortLevel EffortForPercentage(int percentage) =>
        ReasoningScale.FromPercentage(percentage);'''
replace_once(control, old_effort, new_effort)

old_percentage = '''    private static int PercentageForEffort(EffortLevel effort) => effort switch
    {
        EffortLevel.Low => 20,
        EffortLevel.Medium => 60,
        EffortLevel.High => 80,
        EffortLevel.Max => 100,
        _ => 60
    };'''
new_percentage = '''    private static int PercentageForEffort(EffortLevel effort) =>
        ReasoningScale.ToPercentage(effort);'''
replace_once(control, old_percentage, new_percentage)

old_description = '''    private static string EffortDescription(int percentage) => percentage switch
    {
        20 => "Fastest responses, least accurate",
        40 or 60 => "Balanced responses",
        80 => "Slow responses, more accurate",
        100 => "Slowest responses, most accurate",
        _ => "Balanced responses"
    };'''
new_description = '''    private static string EffortDescription(int percentage) => percentage switch
    {
        25 => "Fastest responses with a small bounded context",
        50 => "Balanced speed and reasoning",
        75 => "Deeper reasoning with a larger bounded context",
        100 => "Maximum reasoning with accuracy-preserving large-model runtime",
        _ => "Balanced speed and reasoning"
    };'''
replace_once(control, old_description, new_description)

replace_once(
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

replace_once(
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

replace_all(
    ollama,
    "num_ctx = Math.Clamp(request.Options?.ContextLimit ?? 32768, 2048, 262144),",
    "num_ctx = runtimeProfile.ContextLimit,",
    expected=2)

replace_all(
    ollama,
    'keep_alive = "10m"',
    "keep_alive = runtimeProfile.KeepAlive",
    expected=2)

print("Applied four-step reasoning UI and local inference runtime profile.")

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
    };''n
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
    }+''
outprt
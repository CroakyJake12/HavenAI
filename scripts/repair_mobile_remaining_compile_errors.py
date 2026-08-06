from pathlib import Path
import re

model = Path("src/Haven.Android/ModelBrowserActivity.cs")
text = model.read_text()
text, count = re.subn(
    r'(?m)^(\s*)\.Select\(item => item\.FileName\)\n\1\.Where\(name => !string\.IsNullOrWhiteSpace\(name\)\n\1\s*&& name\.EndsWith\("\.gguf", StringComparison\.OrdinalIgnoreCase\)',
    r'\1.Select(item => item.FileName)\n\1.OfType<string>()\n\1.Where(name => name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)',
    text,
    count=1,
)
if count == 0 and ".OfType<string>()" not in text:
    raise SystemExit("GGUF filename nullability pattern was not found")
text = text.replace(
    "Android.Resource.Layout.SimpleSpinnerItem",
    "global::Android.Resource.Layout.SimpleSpinnerItem",
)
model.write_text(text)

launcher = Path("src/Haven.Android/HavenLauncherActivity.cs")
text = launcher.read_text()
text = text.replace(
    "Android.Resource.Drawable.IcMenuManage",
    'SystemDrawable("ic_menu_manage")',
)
launcher.write_text(text)

layout = Path("src/Haven.Android/MainView.Mobile.Layout.cs")
text = layout.read_text()
text = text.replace(
    'MobileButton(ModelNameText.Text ?? _preferences.DefaultModel ?? "Model", "haven", ShowModelSelector, 10)',
    'MobileButton(_preferences.DefaultModel ?? "Model", "haven", ShowModelSelector, 10)',
)
layout.write_text(text)

checks = {
    model: (
        ".OfType<string>()",
        "global::Android.Resource.Layout.SimpleSpinnerItem",
    ),
    launcher: ('SystemDrawable("ic_menu_manage")',),
    layout: ('MobileButton(_preferences.DefaultModel ?? "Model"',),
}
for path, required in checks.items():
    current = path.read_text()
    missing = [item for item in required if item not in current]
    if missing:
        raise SystemExit(f"{path}: missing expected repairs: {missing}")

print("Remaining Android compiler repairs applied.")

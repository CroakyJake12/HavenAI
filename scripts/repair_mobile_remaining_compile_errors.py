from pathlib import Path

model = Path("src/Haven.Android/ModelBrowserActivity.cs")
text = model.read_text()

while "global::global::" in text:
    text = text.replace("global::global::", "global::")

text = text.replace(
    ".Select(item => item.FileName)\n            .Where(name => !string.IsNullOrWhiteSpace(name)\n                       && name.EndsWith(\".gguf\", StringComparison.OrdinalIgnoreCase)\n                       && !name.Contains(\"mmproj\", StringComparison.OrdinalIgnoreCase))",
    ".Select(item => item.FileName)\n            .OfType<string>()\n            .Where(name => name.EndsWith(\".gguf\", StringComparison.OrdinalIgnoreCase)\n                       && !name.Contains(\"mmproj\", StringComparison.OrdinalIgnoreCase))",
)

text = text.replace(
    "Android.Resource.Layout.SimpleSpinnerItem",
    "global::Android.Resource.Layout.SimpleSpinnerItem",
)
while "global::global::" in text:
    text = text.replace("global::global::", "global::")

model.write_text(text)

launcher = Path("src/Haven.Android/HavenLauncherActivity.cs")
text = launcher.read_text().replace(
    "Android.Resource.Drawable.IcMenuManage",
    'SystemDrawable("ic_menu_manage")',
)
launcher.write_text(text)

layout = Path("src/Haven.Android/MainView.Mobile.Layout.cs")
text = layout.read_text().replace(
    'MobileButton(ModelNameText.Text ?? _preferences.DefaultModel ?? "Model", "haven", ShowModelSelector, 10)',
    'MobileButton(_preferences.DefaultModel ?? "Model", "haven", ShowModelSelector, 10)',
)
layout.write_text(text)

print("Applied Android compiler repairs.")

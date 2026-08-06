from pathlib import Path

model = Path("src/Haven.Android/ModelBrowserActivity.cs")
text = model.read_text()
text = text.replace(
    """            .Select(item => item.FileName)
            .Where(name => !string.IsNullOrWhiteSpace(name)
                       && name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)
                       && !name.Contains("mmproj", StringComparison.OrdinalIgnoreCase))""",
    """            .Select(item => item.FileName)
            .OfType<string>()
            .Where(name => name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)
                       && !name.Contains("mmproj", StringComparison.OrdinalIgnoreCase))""",
)
text = text.replace(
    "Android.Resource.Layout.SimpleSpinnerItem",
    "global::Android.Resource.Layout.SimpleSpinnerItem",
)
text = text.replace(
    "Android.Resource.Layout.SimpleSpinnerDropDownItem",
    "global::Android.Resource.Layout.SimpleSpinnerDropDownItem",
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

print("Applied remaining Android compiler substitutions.")

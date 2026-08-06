from pathlib import Path

def replace_once(path: Path, old: str, new: str, label: str) -> None:
    text = path.read_text()
    count = text.count(old)
    if count == 0 and new in text:
        print(f"{label}: already applied")
        return
    if count != 1:
        raise SystemExit(f"{label}: expected 1 occurrence, found {count}")
    path.write_text(text.replace(old, new, 1))
    print(f"{label}: applied")

model = Path("src/Haven.Android/ModelBrowserActivity.cs")
replace_once(
    model,
    """        var files = model.Siblings?
            .Select(item => item.FileName)
            .Where(name => !string.IsNullOrWhiteSpace(name)
                && name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("mmproj", StringComparison.OrdinalIgnoreCase))
            .ToArray() ?? [];
 """,
    """        var files = model.Siblings?
            .Select(item => item.FileName)
            .OfType<string>()
            .Where(name => name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("mmproj", StringComparison.OrdinalIgnoreCase))
            .ToArray() ?? [];
""",
    "non-null GGUF filenames",
)
replace_once(
    model,
    "Android.Resource.Layout.SimpleSpinnerItem",
    "global::Android.Resource.Layout.SimpleSpinnerItem",
    "globally qualified spinner layout",
)

launcher = Path("src/Haven.Android/HavenLauncherActivity.cs")
replace_once(
    launcher,
    "Android.Resource.Drawable.IcMenuManage",
    'SystemDrawable("ic_menu_manage"),'
    "launcher Haven icon",
)

layout = Path("src/Haven.Android/MainView.Mobile.Layout.cs")
replace_once(
    layout,
    'MobileButton(ModelNameText.Text ?? _preferences.DefaultModel ?? "Model", "haven", ShowModelSelector, 10)',
    'MobileButton(_preferences.DefaultModel ?? "Model", "haven", ShowModelSelector, 10)',
    "mobile header model label",
)

print("Remaining Android compile repairs applied.")

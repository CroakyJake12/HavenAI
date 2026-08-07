#!/usr/bin/env bash
set +e

OUTPUT_BRANCH="apk-output-20260806-2204"
RESULT_DIR="/tmp/haven-apk-result"
OUTPUT_WORKTREE="/tmp/haven-apk-output-worktree"

rm -rf "$RESULT_DIR" "$OUTPUT_WORKTREE"
mkdir -p "$RESULT_DIR"
exec > >(tee "$RESULT_DIR/build.log") 2>&1

status=0
source_sha="$(git rev-parse HEAD)"
echo "source=$source_sha"
echo "run=$GITHUB_RUN_ID"

python3 - <<'PY'
from pathlib import Path

patches = {
    Path("src/Haven.Android/HavenLauncherActivity.cs"): [
        (
            "Android.Resource.Drawable.IcMenuView",
            'SystemDrawable("ic_menu_view")',
            1,
        ),
        (
            "Android.Resource.Drawable.IcMenuManage",
            'SystemDrawable("ic_menu_manage")',
            1,
        ),
        (
            'Theme = "@style/Theme.AppCompat.Light.NoActionBar"',
            'Theme = "@android:style/Theme.Material.Light.NoActionBar"',
            1,
        ),
    ],
    Path("src/Haven.Android/HavenLauncherActivity.Settings.cs"): [
        (
            "typeof(MainActivity)",
            "typeof(AndroidBootstrapActivity)",
            2,
        ),
    ],
    Path("src/Haven.Android/ModelImportActivity.cs"): [
        (
            'Theme = "@style/Theme.AppCompat.Light.NoActionBar"',
            'Theme = "@android:style/Theme.Material.Light.NoActionBar"',
            1,
        ),
    ],
}

for path, replacements in patches.items():
    text = path.read_text(encoding="utf-8")
    original = text
    for old, new, expected in replacements:
        count = text.count(old)
        if count != expected:
            raise RuntimeError(
                f"{path}: expected {expected} occurrence(s) of {old!r}, found {count}"
            )
        text = text.replace(old, new)
    path.write_text(text, encoding="utf-8")
    print(f"Patched {path}")

print("Native Android compatibility patch applied.")
PY
if [ "$?" -ne 0 ]; then
  status=20
fi

dotnet --info

if [ "$status" -eq 0 ]; then
  dotnet workload install android || status=30
fi

if [ "$status" -eq 0 ]; then
  dotnet restore src/Haven.Android/Haven.Android.csproj || status=40
fi

if [ "$status" -eq 0 ]; then
  dotnet publish src/Haven.Android/Haven.Android.csproj \
    -c Debug \
    -f net10.0-android \
    --no-restore \
    -p:AndroidPackageFormat=apk \
    -p:AndroidKeyStore=false || status=50
fi

apk=""
if [ "$status" -eq 0 ]; then
  apk="$(find src/Haven.Android/bin/Debug/net10.0-android \
    -type f -name '*-Signed.apk' -print | head -n 1)"
  if [ -z "$apk" ]; then
    apk="$(find src/Haven.Android/bin/Debug/net10.0-android \
      -type f -name '*.apk' -print | head -n 1)"
  fi
  if [ -z "$apk" ]; then
    echo "Publish completed but no APK was found."
    status=60
  fi
fi

if [ "$status" -eq 0 ]; then
  cp "$apk" "$RESULT_DIR/Haven-Mobile.apk"
  unzip -t "$RESULT_DIR/Haven-Mobile.apk" || status=70
fi

if [ "$status" -eq 0 ]; then
  unzip -Z1 "$RESULT_DIR/Haven-Mobile.apk" \
    | grep -qx 'AndroidManifest.xml' || status=71
fi

if [ "$status" -eq 0 ]; then
  signer="$(find "${ANDROID_SDK_ROOT:-/usr/local/lib/android/sdk}/build-tools" \
    -type f -name apksigner -print | sort -V | tail -n 1)"
  if [ ! -x "$signer" ]; then
    echo "apksigner was not found."
    status=72
  else
    "$signer" verify --verbose "$RESULT_DIR/Haven-Mobile.apk" || status=73
  fi
fi

if [ "$status" -eq 0 ]; then
  sha256sum "$RESULT_DIR/Haven-Mobile.apk" > "$RESULT_DIR/SHA256SUMS.txt"
  stat --printf='%n %s bytes\n' "$RESULT_DIR/Haven-Mobile.apk" \
    > "$RESULT_DIR/SIZE.txt"
  base64 -w 0 "$RESULT_DIR/Haven-Mobile.apk" \
    | split -b 700000 -d -a 4 - "$RESULT_DIR/apk.b64.part-"
  echo "SUCCESS" > "$RESULT_DIR/STATUS.txt"
else
  echo "FAILED" > "$RESULT_DIR/STATUS.txt"
fi

printf 'status=%s\nsource=%s\nrun=%s\n' \
  "$status" "$source_sha" "$GITHUB_RUN_ID" > "$RESULT_DIR/RESULT.txt"

git config user.name "github-actions[bot]"
git config user.email "41898282+github-actions[bot]@users.noreply.github.com"

git fetch origin "$OUTPUT_BRANCH"
git worktree add --detach "$OUTPUT_WORKTREE" "origin/$OUTPUT_BRANCH"
rm -rf "$OUTPUT_WORKTREE/result"
mkdir -p "$OUTPUT_WORKTREE/result"
cp -a "$RESULT_DIR"/. "$OUTPUT_WORKTREE/result/"

git -C "$OUTPUT_WORKTREE" add -A
if ! git -C "$OUTPUT_WORKTREE" diff --cached --quiet; then
  git -C "$OUTPUT_WORKTREE" commit -m "Export Haven APK run $GITHUB_RUN_ID"
  git -C "$OUTPUT_WORKTREE" push origin "HEAD:refs/heads/$OUTPUT_BRANCH"
fi

exit "$status"

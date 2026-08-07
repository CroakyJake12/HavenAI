#!/usr/bin/env bash
set +e
OUT_BRANCH="apk-output-20260806-2204"
RESULT_DIR="/tmp/haven-apk-result"
mkdir -p "$RESULT_DIR"
exec > >(tee "$RESULT_DIR/build.log") 2>&1
status=0

echo "source=$(git rev-parse HEAD)"

python3 - <<'PY'
from pathlib import Path

replacements = {
    Path("src/Haven.Android/HavenLauncherActivity.cs"): [
        ('Android.Resource.Drawable.IcMenuView', 'SystemDrawable("ic_menu_view")'),
        ('Android.Resource.Drawable.IcMenuManage', 'SystemDrawable("ic_menu_manage")'),
    ],
    Path("src/Haven.Android/HavenLauncherActivity.Settings.cs"): [
        ('typeof(MainActivity)', 'typeof(AndroidBootstrapActivity)'),
    ],
}

for path, pairs in replacements.items():
    text = path.read_text(encoding="utf-8")
    original = text
    for old, new in pairs:
        count = text.count(old)
        expected = 2 if old == 'typeof(MainActivity)' else 1
        if count != expected:
            raise RuntimeError(f"{path}: expected {expected} occurrence(s) of {old!r}, found {count}")
        text = text.replace(old, new)
    if text != original:
        path.write_text(text, encoding="utf-8")
        print(f"Patched {path}")
PY
if [ "$?" -ne 0 ]; then status=20; fi

dotnet --info
if [ "$status" -eq 0 ]; then
  dotnet workload install android || status=30
fi
if [ "$status" -eq 0 ]; then
  dotnet restore src/Haven.Android/Haven.Android.csproj || status=40
fi
if [ "$status" -eq 0 ]; then
  dotnet publish src/Haven.Android/Haven.Android.csproj \
    -c Debug -f net10.0-android --no-restore \
    -p:AndroidPackageFormat=apk -p:AndroidKeyStore=false || status=50
fi

apk=""
if [ "$status" -eq 0 ]; then
  apk="$(find src/Haven.Android/bin/Debug/net10.0-android -type f -name '*-Signed.apk' | head -n 1)"
  [ -n "$apk" ] || apk="$(find src/Haven.Android/bin/Debug/net10.0-android -type f -name '*.apk' | head -n 1)"
  [ -n "$apk" ] || status=60
fi

if [ "$status" -eq 0 ]; then
  cp "$apk" "$RESULT_DIR/Haven-Mobile.apk"
  unzip -t "$RESULT_DIR/Haven-Mobile.apk" || status=70
fi
if [ "$status" -eq 0 ]; then
  unzip -Z1 "$RESULT_DIR/Haven-Mobile.apk" | grep -qx 'AndroidManifest.xml' || status=71
fi
if [ "$status" -eq 0 ]; then
  signer="$(find "${ANDROID_SDK_ROOT:-/usr/local/lib/android/sdk}/build-tools" -type f -name apksigner | sort -V | tail -n 1)"
  [ -x "$signer" ] || status=72
  if [ "$status" -eq 0 ]; then
    "$signer" verify --verbose "$RESULT_DIR/Haven-Mobile.apk" || status=73
  fi
fi

if [ "$status" -eq 0 ]; then
  sha256sum "$RESULT_DIR/Haven-Mobile.apk" > "$RESULT_DIR/SHA256SUMS.txt"
  stat --printf='%n %s bytes\n' "$RESULT_DIR/Haven-Mobile.apk" > "$RESULT_DIR/SIZE.txt"
  base64 -w 0 "$RESULT_DIR/Haven-Mobile.apk" \
    | split -b 700000 -d -a 4 - "$RESULT_DIR/apk.b64.part-"
  echo SUCCESS > "$RESULT_DIR/STATUS.txt"
else
  echo FAILED > "$RESULT_DIR/STATUS.txt"
fi
printf 'status=%s\nsource=%s\nrun=%s\n' \
  "$status" "$(git rev-parse HEAD)" "$GITHUB_RUN_ID" > "$RESULT_DIR/RESULT.txt"

git config user.name "github-actions[bot]"
git config user.email "41898282+github-actions[bot]@users.noreply.github.com"
git fetch origin "$OUT_BRANCH:$OUT_BRANCH"
git checkout "$OUT_BRANCH"
git rm -rf . >/dev/null 2>&1 || true
git clean -fdx
mkdir result
cp -a "$RESULT_DIR"/. result/
git add result
git commit -m "Export Haven APK run $GITHUB_RUN_ID"
git push origin "HEAD:refs/heads/$OUT_BRANCH"
exit "$status"

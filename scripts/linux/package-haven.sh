#!/usr/bin/env bash
set -euo pipefail

usage() {
    echo "Usage: $0 <published-linux-runtime> <output-directory> [version]" >&2
    exit 2
}

[[ $# -ge 2 && $# -le 3 ]] || usage
runtime_dir=$(realpath "$1")
output_dir=$(realpath -m "$2")
version="${3:-0.1.0}"
entrypoint="$runtime_dir/Haven.Desktop"

[[ -d "$runtime_dir" ]] || { echo "Published runtime directory is missing: $runtime_dir" >&2; exit 1; }
[[ -x "$entrypoint" ]] || { echo "Linux Haven entrypoint is missing or not executable: $entrypoint" >&2; exit 1; }
command -v dpkg-deb >/dev/null 2>&1 || { echo "Ubuntu packaging requires dpkg-deb." >&2; exit 1; }

mkdir -p "$output_dir"
stage="$output_dir/.haven-package-stage-$version-$$"
if [[ -e "$stage" ]]; then
    echo "Refusing to reuse an existing staging directory: $stage" >&2
    exit 1
fi
mkdir -p "$stage/DEBIAN" "$stage/usr/lib/haven" "$stage/usr/bin" "$stage/usr/share/applications"
cp -a "$runtime_dir"/. "$stage/usr/lib/haven/"

cat > "$stage/DEBIAN/control" <<CONTROL
Package: haven
Version: $version
Section: utils
Priority: optional
Architecture: amd64
Maintainer: Haven
Description: Haven desktop workspace
CONTROL

cat > "$stage/usr/bin/haven" <<'LAUNCHER'
#!/usr/bin/env bash
set -u
runtime=/usr/lib/haven/Haven.Desktop
if [[ ! -x "$runtime" ]]; then
    echo "Haven runtime unavailable; the launcher is leaving the session unchanged." >&2
    exit 127
fi
exec "$runtime" "$@"
LAUNCHER
chmod 0755 "$stage/usr/bin/haven"

cat > "$stage/usr/share/applications/haven.desktop" <<'DESKTOP'
[Desktop Entry]
Type=Application
Name=Haven
Comment=Open the Haven desktop workspace
TryExec=haven
Exec=haven %U
Terminal=false
Categories=Utility;Office;
DESKTOP

package="$output_dir/haven_${version}_amd64.deb"
dpkg-deb --build --root-owner-group "$stage" "$package"
echo "Created $package"
echo "Staging retained for review at $stage"

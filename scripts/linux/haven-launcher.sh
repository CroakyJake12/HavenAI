#!/usr/bin/env bash
set -u

runtime="${HAVEN_RUNTIME:-/usr/lib/haven/Haven.Desktop}"
if [[ ! -x "$runtime" ]]; then
    echo "Haven runtime unavailable; leaving the GNOME session unchanged." >&2
    exit 127
fi

exec "$runtime" "$@"

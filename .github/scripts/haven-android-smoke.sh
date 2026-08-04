#!/bin/sh
set -eu
mkdir -p artifacts/smoke

collect_evidence() {
  adb shell dumpsys activity activities > artifacts/smoke/activities.txt 2>&1 || true
  adb shell dumpsys package com.cakemods.haven > artifacts/smoke/package.txt 2>&1 || true
  adb logcat -d -v threadtime > artifacts/smoke/logcat.txt 2>&1 || true
  adb exec-out screencap -p > artifacts/smoke/haven-launch.png 2>/dev/null || true
  adb shell uiautomator dump /sdcard/haven-window.xml >/dev/null 2>&1 || true
  adb pull /sdcard/haven-window.xml artifacts/smoke/haven-window.xml >/dev/null 2>&1 || true
  adb shell pidof com.cakemods.haven > artifacts/smoke/haven.pid 2>&1 || true
}
trap collect_evidence EXIT

apk="$(find artifacts/android -type f -name '*-Signed.apk' -print -quit)"
if [ -z "$apk" ]; then
  apk="$(find artifacts/android -type f -name '*.apk' | sort | head -n 1)"
fi
test -n "$apk"

sha256sum "$apk" > artifacts/smoke/installed-apk.sha256
cat artifacts/smoke/installed-apk.sha256

adb install -r "$apk" > artifacts/smoke/adb-install.txt 2>&1
cat artifacts/smoke/adb-install.txt
grep -q '^Success$' artifacts/smoke/adb-install.txt

adb shell pm list packages > artifacts/smoke/package-presence.txt
grep -F 'package:com.cakemods.haven' artifacts/smoke/package-presence.txt

component="$(
  adb shell cmd package resolve-activity --brief \
    -a android.intent.action.MAIN \
    -c android.intent.category.LAUNCHER \
    com.cakemods.haven |
  tr -d '\r' |
  tail -n 1
)"
test -n "$component"
test "$component" != "No activity found"
printf '%s\n' "$component" > artifacts/smoke/launcher-component.txt
cat artifacts/smoke/launcher-component.txt

adb logcat -c
adb shell am force-stop com.cakemods.haven
adb shell am start -W -n "$component" > artifacts/smoke/activity-start.txt 2>&1
cat artifacts/smoke/activity-start.txt
sleep 30

collect_evidence

pid="$(tr -d '\r\n' < artifacts/smoke/haven.pid)"
if [ -z "$pid" ]; then
  echo "::error title=Haven Android runtime::Haven exited during startup."
  grep -E -A 40 -B 5 'FATAL EXCEPTION|AndroidRuntime|Haven Android runtime report' \
    artifacts/smoke/logcat.txt || true
  exit 1
fi

if grep -q 'Haven encountered an error' artifacts/smoke/haven-window.xml 2>/dev/null; then
  echo "::error title=Haven Android startup::The native recovery dialog reported an Avalonia startup failure."
  grep -E -A 60 -B 5 'Haven Android runtime report|FATAL EXCEPTION|AndroidRuntime' \
    artifacts/smoke/logcat.txt || true
  exit 1
fi

if grep -q 'FATAL EXCEPTION' artifacts/smoke/logcat.txt &&
   grep -q 'Process: com.cakemods.haven' artifacts/smoke/logcat.txt; then
  echo "::error title=Haven Android runtime::Haven emitted a fatal exception after launch."
  exit 1
fi

adb shell pidof com.cakemods.haven >/dev/null

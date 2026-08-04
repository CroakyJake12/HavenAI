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
  adb shell dumpsys activity exit-info com.cakemods.haven > artifacts/smoke/exit-info.txt 2>&1 || true
  adb shell dumpsys dropbox --print data_app_crash > artifacts/smoke/data-app-crash.txt 2>&1 || true
  adb shell dumpsys dropbox --print data_app_native_crash > artifacts/smoke/data-app-native-crash.txt 2>&1 || true
  adb shell pidof com.cakemods.haven > artifacts/smoke/haven.pid 2>&1 || true
}
trap collect_evidence EXIT

publish_startup_excerpt() {
  exit_file="artifacts/smoke/exit-info.txt"
  java_crash_file="artifacts/smoke/data-app-crash.txt"
  native_crash_file="artifacts/smoke/data-app-native-crash.txt"
  log_file="artifacts/smoke/logcat.txt"

  excerpt=""
  if [ -s "$exit_file" ]; then
    excerpt="PROCESS EXIT INFO:
$(tail -n 80 "$exit_file" 2>/dev/null || true)"
  fi

  native_excerpt="$(
    grep -E -A 140 -B 12 \
      'com\.cakemods\.haven|Cmdline: com\.cakemods\.haven|signal [0-9]+|Abort message|backtrace:|#0[0-9] pc|#00 pc' \
      "$native_crash_file" 2>/dev/null | tail -n 180 || true
  )"
  if [ -n "$native_excerpt" ]; then
    excerpt="$excerpt

NATIVE CRASH DROPBOX:
$native_excerpt"
  fi

  java_excerpt="$(
    grep -E -A 100 -B 8 \
      'com\.cakemods\.haven|Process: com\.cakemods\.haven|FATAL EXCEPTION' \
      "$java_crash_file" 2>/dev/null | tail -n 140 || true
  )"
  if [ -n "$java_excerpt" ]; then
    excerpt="$excerpt

JAVA CRASH DROPBOX:
$java_excerpt"
  fi

  log_excerpt="$(
    grep -E -A 100 -B 12 \
      'FATAL EXCEPTION|AndroidRuntime|Fatal signal|Abort message|SIGABRT|SIGSEGV|has died|Killing.*com\.cakemods\.haven|Unable to instantiate|ClassNotFoundException|NoClassDefFoundError|UnsatisfiedLinkError|Haven Android runtime report|mono-rt|debuggerd|tombstoned' \
      "$log_file" 2>/dev/null | tail -n 180 || true
  )"
  if [ -n "$log_excerpt" ]; then
    excerpt="$excerpt

MATCHING LOGCAT:
$log_excerpt"
  fi

  if [ -z "$excerpt" ]; then
    excerpt="$(
      grep -E 'com\.cakemods\.haven|Haven|mono|dotnet|libc' "$log_file" 2>/dev/null | tail -n 120 || true
    )"
  fi

  if [ -z "$excerpt" ]; then
    excerpt="No matching Haven process-exit or startup-crash information was captured."
  fi

  sanitized="$(
    printf '%s\n' "$excerpt" |
      sed -E \
        -e 's/([Bb]earer)[[:space:]]+[A-Za-z0-9._~+\/=-]+/\1 [redacted]/g' \
        -e 's/((api[_-]?key|access[_-]?token|refresh[_-]?token|token|password|secret|authorization)[[:space:]]*[:=][[:space:]]*)[^ ,;]+/\1[redacted]/Ig' |
      tr '\r\n' '  ' |
      cut -c 1-12000
  )"
  sanitized="$(printf '%s' "$sanitized" | sed 's/%/%25/g')"

  echo "::error title=Haven Android native-crash details::$sanitized"
}

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
adb shell am start -W -n "$component" > artifacts/smoke/actity-start.txt 2>&1
cat artifacts/smoke/actity-start.txt
sleep 30

collect_evidence

pid="$(tr -d '\r\n' < artifacts/smoke/haven.pid)"
if [ -z "$pid" ]; then
  publish_startup_excerpt
  echo "::error title=Haven Android runtime::Haven exited during startup."
  grep -E -A 40 -B 5 'FATAL EXCEPTION|AndroidRuntime|Haven Android runtime report' \
    artifacts/smoke/logcat.txt || true
  exit 1
fi

if grep -q 'Haven encountered an error' artifacts/smoke/haven-window.xml 2>/dev/null; then
  publish_startup_excerpt
  echo "::error title=Haven Android startup::The native recovery dialog reported an Avalonia startup failure."
  grep -E -A 60 -B 5 'Haven Android runtime report|FATAL EXCEPTION|AndroidRuntime' \
    artifacts/smoke/logcat.txt || true
  exit 1
fi

if grep -q 'FATAL EXCEPTION' artifacts/smoke/logcat.txt &&
   grep -q 'Process: com.cakemods.haven' artifacts/smoke/logcat.txt; then
  publish_startup_excerpt
  echo "::error title=Haven Android runtime::Haven emitted a fatal exception after launch."
  exit 1
fi

adb shell pidof com.cakemods.haven >/dev/null

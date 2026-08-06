from pathlib import Path

path = Path(".github/workflows/build-haven-android.yml")
text = path.read_text()

branch_old = "    branches:\n      - haven-android-cloud-build\n"
branch_new = "    branches:\n      - haven-android-cloud-build\n      - gpt/haven-mobile-repair-android\n"
if branch_old not in text and branch_new not in text:
    raise SystemExit("build branch anchor not found")
text = text.replace(branch_old, branch_new, 1)

publish_old = (
    "            -p:AndroidPackageFormat=apk \\\n"
    "            -p:AndroidKeyStore=false 2>&1 | tee artifacts/android/publish.log\n"
)
publish_new = (
    "            -p:AndroidPackageFormat=apk \\\n"
    "            -p:ApplicationVersion=\"$((300000 + GITHUB_RUN_NUMBER))\" \\\n"
    "            -p:ApplicationDisplayVersion=\"0.3.${GITHUB_RUN_NUMBER}-mobile-preview\" \\\n"
    "            -p:AndroidKeyStore=false 2>&1 | tee artifacts/android/publish.log\n"
)
if publish_old not in text and publish_new not in text:
    raise SystemExit("versioned publish anchor not found")
text = text.replace(publish_old, publish_new, 1)

if "          api-level: 35\n" in text:
    text = text.replace("          api-level: 35\n", "          api-level: 36\n", 1)
elif "          api-level: 36\n" not in text:
    raise SystemExit("emulator API anchor not found")

path.write_text(text)

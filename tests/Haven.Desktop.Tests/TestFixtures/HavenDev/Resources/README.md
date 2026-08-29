# Haven Dev SIMULATED workspace resources

Every resource in this directory is **SIMULATED — NOT REAL AOSP VALIDATION**.

These are inert desktop-test inputs. They must never be sent to Haven's production process, build, device, ADB, Git, or capability executors. The JSON fixture represents AOSP-looking virtual paths while keeping repository resources on neutral extensions. In particular, virtual `packages/apps/HelloHaven/Android.bp` is stored only as JSON text with resource name `android-build-blueprint.fixture.txt`; there is no real `Android.bp` file in this fixture.

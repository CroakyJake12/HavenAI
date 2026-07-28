"""Create Haven's packaged icon assets from one transparent master PNG."""

from __future__ import annotations

import shutil
import sys
from pathlib import Path

from PIL import Image


def main() -> int:
    if len(sys.argv) != 2:
        print("Usage: update-haven-icon.py <transparent-master.png>")
        return 2

    source = Path(sys.argv[1]).resolve()
    if not source.is_file():
        print(f"Icon source does not exist: {source}")
        return 2

    assets = Path(__file__).resolve().parents[1] / "src" / "Haven.Desktop" / "Assets"
    image = Image.open(source).convert("RGBA")
    if image.getbbox() is None:
        print("Icon source is fully transparent.")
        return 2

    # Keep the supplied artwork exactly as the editable master. LANCZOS is used
    # only for packaged scales, and transparent edge pixels remain transparent.
    shutil.copyfile(source, assets / "haven-1024.png")
    image.resize((192, 192), Image.Resampling.LANCZOS).save(assets / "haven-192.png", optimize=True)
    image.resize((32, 32), Image.Resampling.LANCZOS).save(assets / "haven-32.png", optimize=True)
    image.save(
        assets / "haven.ico",
        format="ICO",
        sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48),
               (64, 64), (96, 96), (128, 128), (256, 256)],
    )
    print(f"Updated Haven icon assets from {source.name}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

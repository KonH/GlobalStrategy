"""
Export TextureSources images into Assets/Textures at configured sizes.

Each subdirectory under TextureSources/ that contains an export.json is mirrored
to Assets/Textures/<same relative path>. export.json shape:

    { "width": 128, "height": 128 }

Images are scaled with LANCZOS to fit inside the target size (aspect preserved).
If the source aspect ratio does not match the target, the canvas is extended with
transparency and the content is centered.

Usage (from project root):
    .venv\\Scripts\\python.exe scripts\\utils\\export_textures.py
    .venv\\Scripts\\python.exe scripts\\utils\\export_textures.py --dry-run
    .venv\\Scripts\\python.exe scripts\\utils\\export_textures.py --only Flags/Countries
    .venv\\Scripts\\python.exe scripts\\utils\\export_textures.py --only Icons
    .venv\\Scripts\\python.exe scripts\\utils\\export_textures.py -v --dry-run
    ./scripts/utils/export_textures.sh
    .\\scripts\\utils\\export_textures.ps1
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from PIL import Image, ImageOps

REPO_ROOT = Path(__file__).resolve().parents[2]
SOURCES_ROOT = REPO_ROOT / "TextureSources"
DEST_ROOT = REPO_ROOT / "Assets" / "Textures"
EXPORT_CONFIG_NAME = "export.json"
IMAGE_SUFFIXES = {".png", ".jpg", ".jpeg", ".webp", ".tif", ".tiff"}


def load_export_size(config_path: Path) -> tuple[int, int]:
    with config_path.open(encoding="utf-8") as f:
        data = json.load(f)

    try:
        width = int(data["width"])
        height = int(data["height"])
    except (KeyError, TypeError, ValueError) as exc:
        raise ValueError(f"{config_path}: expected integer width/height") from exc

    if width <= 0 or height <= 0:
        raise ValueError(f"{config_path}: width/height must be positive")

    return width, height


def ensure_rgba(image: Image.Image) -> Image.Image:
    if image.mode == "RGBA":
        return image
    return image.convert("RGBA")


def resize_to_target(image: Image.Image, width: int, height: int) -> Image.Image:
    """Fit image into width x height; pad with transparency and center if needed."""
    rgba = ensure_rgba(image)
    return ImageOps.pad(
        rgba,
        (width, height),
        method=Image.Resampling.LANCZOS,
        color=(0, 0, 0, 0),
        centering=(0.5, 0.5),
    )


def all_export_dirs() -> list[Path]:
    return sorted(
        path.parent
        for path in SOURCES_ROOT.rglob(EXPORT_CONFIG_NAME)
        if path.is_file()
    )


def iter_export_dirs(only: str | None) -> list[Path]:
    """Return export folders, optionally filtered by a TextureSources-relative path.

    ``only`` may be a leaf folder with export.json (e.g. Flags/Countries) or a
    parent path that contains nested export folders (e.g. Icons, Flags).
    """
    if not only:
        return all_export_dirs()

    rel = Path(only.replace("\\", "/"))
    candidate = (SOURCES_ROOT / rel).resolve()
    sources_root = SOURCES_ROOT.resolve()
    try:
        candidate.relative_to(sources_root)
    except ValueError as exc:
        raise FileNotFoundError(
            f"Filter path must be under TextureSources/: {only}"
        ) from exc

    if not candidate.is_dir():
        raise FileNotFoundError(f"Source directory not found: {candidate}")

    matched = [
        path
        for path in all_export_dirs()
        if path.resolve() == candidate
        or candidate in path.resolve().parents
    ]
    if not matched:
        raise FileNotFoundError(
            f"No export.json folders under TextureSources/{rel.as_posix()}"
        )
    return matched


def export_dir(source_dir: Path, dry_run: bool, verbose: bool) -> int:
    """Export one TextureSources folder. Returns exported count."""
    width, height = load_export_size(source_dir / EXPORT_CONFIG_NAME)
    rel = source_dir.relative_to(SOURCES_ROOT)
    dest_dir = DEST_ROOT / rel

    images = sorted(
        path
        for path in source_dir.iterdir()
        if path.is_file() and path.suffix.lower() in IMAGE_SUFFIXES
    )

    if not images:
        print(f"  {rel.as_posix()}: no images ({width}x{height})")
        return 0

    if not dry_run:
        dest_dir.mkdir(parents=True, exist_ok=True)

    exported = 0
    for source_path in images:
        dest_path = dest_dir / f"{source_path.stem}.png"

        with Image.open(source_path) as image:
            source_size = image.size
            exact = source_size == (width, height) and image.mode == "RGBA"
            result = ensure_rgba(image) if exact else resize_to_target(image, width, height)

            if verbose:
                action = "keep" if exact else "fit+pad"
                print(
                    f"    {source_path.name} "
                    f"{source_size[0]}x{source_size[1]} -> {width}x{height} ({action})"
                )

            if dry_run:
                exported += 1
                continue

            result.save(dest_path, format="PNG", optimize=True)
            exported += 1

    prefix = "would export" if dry_run else "exported"
    print(f"  {rel.as_posix()}: {prefix} {exported} file(s) -> {width}x{height}")
    return exported


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Resize/export TextureSources into Assets/Textures."
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Print planned exports without writing files",
    )
    parser.add_argument(
        "--only",
        metavar="REL_PATH",
        help=(
            "Optional directory filter under TextureSources/ "
            "(leaf folder or parent, e.g. Flags/Countries or Icons)"
        ),
    )
    parser.add_argument(
        "--verbose",
        "-v",
        action="store_true",
        help="Print each source file and its source size",
    )
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv if argv is not None else sys.argv[1:])

    if not SOURCES_ROOT.is_dir():
        print(f"ERROR: missing sources root: {SOURCES_ROOT}", file=sys.stderr)
        return 1

    try:
        export_dirs = iter_export_dirs(args.only)
    except (FileNotFoundError, ValueError) as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1

    if not export_dirs:
        print("No export.json folders found under TextureSources/")
        return 0

    mode = "DRY-RUN " if args.dry_run else ""
    print(f"{mode}Exporting {len(export_dirs)} folder(s) from TextureSources/ -> Assets/Textures/")

    total = 0
    for source_dir in export_dirs:
        try:
            total += export_dir(source_dir, dry_run=args.dry_run, verbose=args.verbose)
        except (OSError, ValueError) as exc:
            print(f"ERROR: {exc}", file=sys.stderr)
            return 1

    print(f"Done: {total} image(s){' planned' if args.dry_run else ' exported'}.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

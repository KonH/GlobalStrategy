"""
Export TextureSources images into Assets/Textures at configured sizes.

Each subdirectory under TextureSources/ that contains an export.json is mirrored
to Assets/Textures/<same relative path>. export.json shape:

    { "width": 128, "height": 128 }

Images are scaled with LANCZOS to fit inside the target size (aspect preserved).
If the source aspect ratio does not match the target, the canvas is extended with
transparency and the content is centered.

Matching Unity .meta sprite rects/borders are remapped with the same fit+pad
transform so sliced sprites stay valid after resize.

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
import re
import sys
from pathlib import Path

from PIL import Image, ImageOps

REPO_ROOT = Path(__file__).resolve().parents[2]
SOURCES_ROOT = REPO_ROOT / "TextureSources"
DEST_ROOT = REPO_ROOT / "Assets" / "Textures"
EXPORT_CONFIG_NAME = "export.json"
IMAGE_SUFFIXES = {".png", ".jpg", ".jpeg", ".webp", ".tif", ".tiff"}

RECT_RE = re.compile(
    r"(rect:\n"
    r"\s+serializedVersion: \d+\n"
    r"\s+x: )([-.\d]+)(\n"
    r"\s+y: )([-.\d]+)(\n"
    r"\s+width: )([-.\d]+)(\n"
    r"\s+height: )([-.\d]+)"
)
BORDER_RE = re.compile(
    r"(border: \{x: )([-.\d]+)(, y: )([-.\d]+)(, z: )([-.\d]+)(, w: )([-.\d]+)(\})"
)
SPRITE_BORDER_RE = re.compile(
    r"(spriteBorder: \{x: )([-.\d]+)(, y: )([-.\d]+)(, z: )([-.\d]+)(, w: )([-.\d]+)(\})"
)


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


def pad_transform(
    src_w: int,
    src_h: int,
    dst_w: int,
    dst_h: int,
    centering: tuple[float, float] = (0.5, 0.5),
) -> tuple[float, float, float]:
    """Return (scale, pad_x, pad_y) matching PIL ImageOps.pad centering."""
    if src_w <= 0 or src_h <= 0:
        raise ValueError("source size must be positive")
    scale = min(dst_w / src_w, dst_h / src_h)
    fitted_w = src_w * scale
    fitted_h = src_h * scale
    pad_x = (dst_w - fitted_w) * centering[0]
    pad_y = (dst_h - fitted_h) * centering[1]
    return scale, pad_x, pad_y


def _clamp_rect(
    x: float, y: float, w: float, h: float, dst_w: int, dst_h: int
) -> tuple[int, int, int, int]:
    ix = int(round(x))
    iy = int(round(y))
    iw = int(round(w))
    ih = int(round(h))
    ix = max(0, min(ix, max(dst_w - 1, 0)))
    iy = max(0, min(iy, max(dst_h - 1, 0)))
    iw = max(1, min(iw, dst_w - ix))
    ih = max(1, min(ih, dst_h - iy))
    return ix, iy, iw, ih


def _parse_rects(meta_text: str) -> list[tuple[float, float, float, float]]:
    return [
        (float(m.group(2)), float(m.group(4)), float(m.group(6)), float(m.group(8)))
        for m in RECT_RE.finditer(meta_text)
    ]


def _authoring_size(
    rects: list[tuple[float, float, float, float]],
    source_size: tuple[int, int],
    dest_size: tuple[int, int],
    prev_asset_size: tuple[int, int] | None,
) -> tuple[int, int] | None:
    """Infer the pixel space sprite rects are authored in, or None to skip."""
    if not rects:
        return None

    src_w, src_h = source_size
    dst_w, dst_h = dest_size
    max_r = max(x + w for x, _y, w, _h in rects)
    max_t = max(y + h for _x, y, _w, h in rects)
    any_oob = any(
        x < -0.01 or y < -0.01 or x + w > dst_w + 0.01 or y + h > dst_h + 0.01
        for x, y, w, h in rects
    )

    matches_source = abs(max_r - src_w) <= 1 and abs(max_t - src_h) <= 1
    within_source = max_r <= src_w + 1 and max_t <= src_h + 1

    if matches_source:
        return source_size
    if any_oob:
        if within_source:
            return source_size
        if prev_asset_size and prev_asset_size != dest_size:
            return prev_asset_size
        return (int(round(max_r)), int(round(max_t)))

    # In-bounds but still clearly source-authored (e.g. 256x384 rect inside 480x600).
    if within_source and (src_w, src_h) != (dst_w, dst_h):
        if abs(max_r - src_w) <= 1 or abs(max_t - src_h) <= 1:
            return source_size
        # Cropped source-space rects: common when max extent is well below dest
        # and close to a source-sized crop canvas.
        if max_r > dst_w or max_t > dst_h:
            return source_size
        if max_r <= src_w and max_t <= src_h and (
            max_r < dst_w * 0.95 or max_t < dst_h * 0.95
        ):
            # Prefer remapping when content was clearly authored on a different canvas.
            # Skip only when extents already fill the destination (already converted).
            if abs(max_r - dst_w) > 1 or abs(max_t - dst_h) > 1:
                return source_size

    return None


def _normalize_stale_fullframe_rects(
    rects: list[tuple[float, float, float, float]],
    source_size: tuple[int, int],
) -> list[tuple[float, float, float, float]]:
    """Treat a single stale origin rect as covering the whole source texture.

    Example: coin icons still sliced as 128x128 after the source became 512x512.
    """
    if len(rects) != 1:
        return rects
    x, y, w, h = rects[0]
    src_w, src_h = source_size
    if x == 0 and y == 0 and (abs(w - src_w) > 1 or abs(h - src_h) > 1):
        return [(0.0, 0.0, float(src_w), float(src_h))]
    return rects


def update_meta_sprites(
    meta_path: Path,
    source_size: tuple[int, int],
    dest_size: tuple[int, int],
    prev_asset_size: tuple[int, int] | None,
) -> bool:
    """Remap .meta sprite rects/borders from authoring space to dest size."""
    if not meta_path.is_file():
        return False

    text = meta_path.read_text(encoding="utf-8")
    rects = _parse_rects(text)
    normalized = _normalize_stale_fullframe_rects(rects, source_size)
    from_size = _authoring_size(normalized, source_size, dest_size, prev_asset_size)
    if from_size is None:
        return False

    if from_size == dest_size:
        return False

    scale, pad_x, pad_y = pad_transform(
        from_size[0], from_size[1], dest_size[0], dest_size[1]
    )
    dst_w, dst_h = dest_size
    use_normalized_fullframe = normalized != rects

    def repl_rect(match: re.Match[str]) -> str:
        if use_normalized_fullframe:
            x, y, w, h = normalized[0]
        else:
            x, y, w, h = map(float, match.group(2, 4, 6, 8))
        ix, iy, iw, ih = _clamp_rect(
            x * scale + pad_x,
            y * scale + pad_y,
            w * scale,
            h * scale,
            dst_w,
            dst_h,
        )
        return (
            f"{match.group(1)}{ix}{match.group(3)}{iy}"
            f"{match.group(5)}{iw}{match.group(7)}{ih}"
        )

    def repl_border(match: re.Match[str]) -> str:
        left, bottom, right, top = map(float, match.group(2, 4, 6, 8))
        return (
            f"{match.group(1)}{int(round(left * scale))}"
            f"{match.group(3)}{int(round(bottom * scale))}"
            f"{match.group(5)}{int(round(right * scale))}"
            f"{match.group(7)}{int(round(top * scale))}"
            f"{match.group(9)}"
        )

    new_text = RECT_RE.sub(repl_rect, text)
    new_text = BORDER_RE.sub(repl_border, new_text)
    new_text = SPRITE_BORDER_RE.sub(repl_border, new_text)

    if new_text == text:
        return False

    meta_path.write_text(new_text, encoding="utf-8", newline="\n")
    return True


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
    metas_updated = 0
    for source_path in images:
        dest_path = dest_dir / f"{source_path.stem}.png"
        meta_path = Path(str(dest_path) + ".meta")
        prev_asset_size = None
        if dest_path.is_file():
            with Image.open(dest_path) as prev:
                prev_asset_size = prev.size

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
                if meta_path.is_file():
                    text = meta_path.read_text(encoding="utf-8")
                    rects = _normalize_stale_fullframe_rects(
                        _parse_rects(text), source_size
                    )
                    from_size = _authoring_size(
                        rects, source_size, (width, height), prev_asset_size
                    )
                    if from_size is not None and from_size != (width, height):
                        metas_updated += 1
                        if verbose:
                            print(
                                f"      meta rects {from_size[0]}x{from_size[1]} "
                                f"-> {width}x{height}"
                            )
                exported += 1
                continue

            result.save(dest_path, format="PNG", optimize=True)
            if update_meta_sprites(
                meta_path, source_size, (width, height), prev_asset_size
            ):
                metas_updated += 1
            exported += 1

    prefix = "would export" if dry_run else "exported"
    meta_note = f", {metas_updated} meta(s)" if metas_updated else ""
    print(
        f"  {rel.as_posix()}: {prefix} {exported} file(s) -> {width}x{height}{meta_note}"
    )
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
        print("No export.json directories found under TextureSources/")
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

"""
Convert an SVG file or URL to a PNG file.

Usage:
    python .claude/svg_to_png.py input.svg output.png [--width 256]
    python .claude/svg_to_png.py https://example.com/flag.svg output.png --width 256

Backends (tried in order):
    1. cairosvg   — best quality; requires libcairo-2.dll on Windows
                    (install via GTK for Windows or msys2/mingw)
    2. svglib + reportlab — pure Python, no system DLLs required
                    pip install svglib reportlab

Install both for best results:
    pip install cairosvg svglib reportlab
"""

import sys
import os
import io
import argparse


def _try_cairosvg(svg_data: bytes, width: int) -> bytes:
    import cairosvg
    return cairosvg.svg2png(bytestring=svg_data, output_width=width)


def _try_svglib(svg_data: bytes, width: int) -> bytes:
    import tempfile
    from svglib.svglib import svg2rlg
    from reportlab.graphics import renderPM

    with tempfile.NamedTemporaryFile(suffix=".svg", delete=False) as f:
        f.write(svg_data)
        tmp = f.name
    try:
        drawing = svg2rlg(tmp)
        if drawing is None:
            raise ValueError("svglib could not parse SVG")
        if drawing.width and drawing.width != width:
            scale = width / drawing.width
            drawing.width = width
            drawing.height = drawing.height * scale
            drawing.transform = (scale, 0, 0, scale, 0, 0)
        buf = io.BytesIO()
        renderPM.drawToFile(drawing, buf, fmt="PNG")
        return buf.getvalue()
    finally:
        os.unlink(tmp)


def svg_bytes_to_png(svg_data: bytes, width: int = 256) -> bytes:
    """Convert raw SVG bytes to PNG bytes. Tries cairosvg then svglib."""
    try:
        return _try_cairosvg(svg_data, width)
    except Exception:
        pass
    return _try_svglib(svg_data, width)


def load_svg(source: str, session=None) -> bytes:
    """Load SVG content from a file path or URL."""
    if source.startswith("http://") or source.startswith("https://"):
        import requests
        s = session or requests.Session()
        resp = s.get(source, timeout=30)
        resp.raise_for_status()
        return resp.content
    with open(source, "rb") as f:
        return f.read()


def convert(source: str, dest: str, width: int = 256, session=None) -> None:
    """Convert SVG at source (path or URL) to PNG at dest."""
    svg_data = load_svg(source, session=session)
    png_data = svg_bytes_to_png(svg_data, width=width)
    dest_dir = os.path.dirname(dest)
    if dest_dir:
        os.makedirs(dest_dir, exist_ok=True)
    with open(dest, "wb") as f:
        f.write(png_data)


def main():
    parser = argparse.ArgumentParser(description="Convert SVG to PNG")
    parser.add_argument("source", help="SVG file path or URL")
    parser.add_argument("dest", help="Output PNG file path")
    parser.add_argument("--width", type=int, default=256, help="Output width in pixels (default: 256)")
    args = parser.parse_args()
    convert(args.source, args.dest, width=args.width)
    print(f"OK: {args.dest}")


if __name__ == "__main__":
    main()

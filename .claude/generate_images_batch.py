"""
Batch image generation via ComfyUI.

Usage: .venv\Scripts\python.exe .claude\generate_images_batch.py <config.json>

Config file format (.tmp/images.json):
[
  {"output": "Assets/UI/Icons/hero.png", "size": "512x512", "prompt": "medieval knight portrait"},
  {"output": "Assets/UI/Icons/gold.png", "size": "256x256", "prompt": "gold coin icon, white background"}
]

ComfyUI must be running at http://127.0.0.1:8188.
"""

import json, subprocess, sys
from pathlib import Path

if len(sys.argv) < 2:
    print("Usage: generate_images_batch.py <config.json>")
    sys.exit(1)

config_path = Path(sys.argv[1])
if not config_path.exists():
    print(f"Config file not found: {config_path}")
    sys.exit(1)

images = json.loads(config_path.read_text(encoding="utf-8"))
if not images:
    print("No images defined in config.")
    sys.exit(0)

script = Path(__file__).parent / "generate_image.py"
python = Path(__file__).parent.parent / ".venv" / "Scripts" / "python.exe"

print(f"Generating {len(images)} image(s) from {config_path}\n")
failed = []

for i, entry in enumerate(images, 1):
    output_path = entry["output"]
    size = entry["size"]
    prompt = entry["prompt"]
    print(f"[{i}/{len(images)}] {output_path}  ({size})")
    print(f"  Prompt: {prompt}")
    result = subprocess.run([str(python), str(script), output_path, size, prompt])
    if result.returncode != 0:
        print(f"  FAILED (exit {result.returncode})\n")
        failed.append(output_path)
    else:
        print()

print("─" * 60)
if failed:
    print(f"Done. {len(images) - len(failed)}/{len(images)} succeeded.")
    print("Failed:")
    for f in failed:
        print(f"  {f}")
    sys.exit(1)
else:
    print(f"Done. All {len(images)} image(s) saved.")

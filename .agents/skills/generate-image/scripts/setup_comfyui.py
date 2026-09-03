"""Install the portable ComfyUI release and the local FLUX.1-schnell model set."""
import argparse
import sys
import time
import urllib.error
import urllib.request
import subprocess
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--project-root", default=Path.cwd())
args = parser.parse_args()
PROJECT_ROOT = Path(args.project_root).resolve()
COMFYUI_DIR = PROJECT_ROOT / "ComfyUI"
ARCHIVE_PATH = PROJECT_ROOT / ".tmp" / "ComfyUI_windows_portable_nvidia.7z"
SEVENZ_PATH = PROJECT_ROOT / ".tmp" / "7zr.exe"

COMFYUI_URL = (
	"https://github.com/Comfy-Org/ComfyUI/releases/download/v0.25.1/"
	"ComfyUI_windows_portable_nvidia.7z"
)
SEVENZ_URL = "https://www.7-zip.org/a/7zr.exe"
MODEL_FILES = [
	(
		"checkpoints/flux1-schnell-fp8.safetensors",
		"https://huggingface.co/Comfy-Org/flux1-schnell/resolve/main/flux1-schnell-fp8.safetensors",
	),
	(
		"vae/ae.safetensors",
		"https://huggingface.co/Comfy-Org/Lumina_Image_2.0_Repackaged/resolve/main/split_files/vae/ae.safetensors",
	),
	(
		"clip/clip_l.safetensors",
		"https://huggingface.co/comfyanonymous/flux_text_encoders/resolve/main/clip_l.safetensors",
	),
	(
		"clip/t5xxl_fp8_e4m3fn.safetensors",
		"https://huggingface.co/comfyanonymous/flux_text_encoders/resolve/main/t5xxl_fp8_e4m3fn.safetensors",
	),
]


def download(url, destination, label):
    destination.parent.mkdir(parents=True, exist_ok=True)
    for attempt in range(1, 4):
        existing = destination.stat().st_size if destination.exists() else 0
        request = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
        if existing:
            request.add_header("Range", f"bytes={existing}-")
        try:
            with urllib.request.urlopen(request, timeout=60) as response:
                content_length = int(response.headers.get("Content-Length") or 0)
                total = existing + content_length
                downloaded = existing
                last_log = time.time() - 11
                with open(destination, "ab" if existing else "wb") as output:
                    while data := response.read(1024 * 1024):
                        output.write(data)
                        downloaded += len(data)
                        if time.time() - last_log >= 10:
                            if total:
                                print(f"[{label}] {downloaded // (1024 * 1024)} / {total // (1024 * 1024)} MB ({downloaded / total * 100:.1f}%)", flush=True)
                            else:
                                print(f"[{label}] {downloaded // (1024 * 1024)} MB", flush=True)
                            last_log = time.time()
            print(f"[DONE] {label}", flush=True)
            return
        except urllib.error.HTTPError as error:
            if error.code == 416:
                print(f"[SKIP] {label} — already complete", flush=True)
                return
            failure = error
        except (OSError, TimeoutError, urllib.error.URLError) as error:
            failure = error
        if attempt == 3:
            raise failure
        print(f"[RETRY] {label} attempt {attempt}/3 failed: {failure}", flush=True)
        time.sleep(5)


if not COMFYUI_DIR.exists():
	if not ARCHIVE_PATH.exists() or ARCHIVE_PATH.stat().st_size < 1024 * 1024:
		print("=== Downloading ComfyUI portable ===", flush=True)
		download(COMFYUI_URL, ARCHIVE_PATH, "ComfyUI portable")
	else:
		print(f"[SKIP] Archive already present ({ARCHIVE_PATH.stat().st_size // (1024 * 1024)} MB)", flush=True)
	if not SEVENZ_PATH.exists():
		print("=== Downloading 7zr.exe ===", flush=True)
		download(SEVENZ_URL, SEVENZ_PATH, "7zr.exe")
	print("=== Extracting ===", flush=True)
	result = subprocess.run([str(SEVENZ_PATH), "x", str(ARCHIVE_PATH), f"-o{PROJECT_ROOT}", "-y"], capture_output=True, text=True)
	if result.returncode:
		print(f"[ERROR] Extraction failed:\n{result.stdout}\n{result.stderr}", flush=True)
		sys.exit(1)
	for item in PROJECT_ROOT.iterdir():
		if item.is_dir() and item != COMFYUI_DIR and (item / "run_nvidia_gpu.bat").exists():
			item.rename(COMFYUI_DIR)
			print(f"[RENAME] {item.name} -> ComfyUI/", flush=True)
			break
	else:
		print("[ERROR] Could not find extracted folder with run_nvidia_gpu.bat", flush=True)
		sys.exit(1)
	ARCHIVE_PATH.unlink(missing_ok=True)
	SEVENZ_PATH.unlink(missing_ok=True)

models_base = COMFYUI_DIR / "ComfyUI" / "models"
print("=== Downloading models ===", flush=True)
for relative_path, url in MODEL_FILES:
	print(f"--- {Path(relative_path).name} ---", flush=True)
	download(url, models_base / relative_path, Path(relative_path).name)
print("=== All done ===", flush=True)
print("Start ComfyUI: ComfyUI\\run_nvidia_gpu.bat", flush=True)

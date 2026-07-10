Check whether ComfyUI is installed and running; start it if installed but idle; provide setup instructions if not installed.

## Step 1 — Detect if ComfyUI is reachable

Write `.tmp/run.py`:
```python
import urllib.request
try:
    urllib.request.urlopen("http://127.0.0.1:8188/system_stats", timeout=3)
    print("RUNNING")
except Exception:
    print("NOT_RUNNING")
```
Run: `& "scripts\run.ps1"`
Delete `.tmp/run.py`.

If output is `RUNNING` → report "ComfyUI is already running at http://127.0.0.1:8188" and stop.

## Step 2 — Check if ComfyUI is installed

Use Glob to check whether `ComfyUI/run_nvidia_gpu.bat` exists.

### If the file exists (installed, not running)

1. Start ComfyUI in a detached window:
   PowerShell: `Start-Process -FilePath "ComfyUI\run_nvidia_gpu.bat" -WorkingDirectory "ComfyUI"`
2. Poll until ready — write `.tmp/run.py`:
   ```python
   import urllib.request, time
   for i in range(24):
       try:
           urllib.request.urlopen("http://127.0.0.1:8188/system_stats", timeout=3)
           print("READY")
           break
       except Exception:
           time.sleep(5)
   else:
       print("TIMEOUT")
   ```
   Run: `& "scripts\run.ps1"`
   Delete `.tmp/run.py`.
3. If `READY` → report "ComfyUI started and is ready."
4. If `TIMEOUT` → report "ComfyUI was started but is not responding after 2 minutes. Check the console window for errors."

### If the file does NOT exist (not installed)

Run the automated fresh install:

1. Write `.tmp/comfyui_setup.py` with the content below and run it in background:
   PowerShell: `Start-Process -FilePath ".venv\Scripts\python.exe" -ArgumentList ".tmp\comfyui_setup.py" -NoNewWindow -RedirectStandardOutput ".tmp\comfyui_setup.log" -RedirectStandardError ".tmp\comfyui_setup.err"`

2. Monitor progress by reading `.tmp/comfyui_setup.log` periodically with the Read tool.

3. When the log shows `=== All done ===`, proceed to Step 1 again to verify ComfyUI is running.

**Script content for `.tmp/comfyui_setup.py`:**
```python
"""
ComfyUI fresh install: download portable release + FLUX.1-schnell models.
Run from project root. Progress is printed every 10 seconds.
Supports resume: partial files are continued via HTTP Range requests.
Uses 7zr.exe (standalone 7-Zip CLI) downloaded automatically for extraction.
"""
import sys, time, urllib.request, urllib.error, subprocess
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parent.parent
COMFYUI_DIR  = PROJECT_ROOT / "ComfyUI"
ARCHIVE_PATH = PROJECT_ROOT / ".tmp" / "ComfyUI_windows_portable_nvidia.7z"
SEVENZ_PATH  = PROJECT_ROOT / ".tmp" / "7zr.exe"

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


def download(url, dest, label):
    dest = Path(dest)
    dest.parent.mkdir(parents=True, exist_ok=True)
    existing = dest.stat().st_size if dest.exists() else 0

    req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
    if existing:
        req.add_header("Range", f"bytes={existing}-")

    try:
        resp = urllib.request.urlopen(req, timeout=60)
    except urllib.error.HTTPError as e:
        if e.code == 416:
            print(f"[SKIP] {label} — already complete", flush=True)
            return
        raise

    content_length = int(resp.headers.get("Content-Length") or 0)
    total = existing + content_length
    downloaded = existing
    mode = "ab" if existing else "wb"
    chunk = 1024 * 1024
    last_log = time.time() - 11

    with open(dest, mode) as f:
        while True:
            data = resp.read(chunk)
            if not data:
                break
            f.write(data)
            downloaded += len(data)
            if time.time() - last_log >= 10:
                if total:
                    pct = downloaded / total * 100
                    mb = downloaded // (1024 * 1024)
                    total_mb = total // (1024 * 1024)
                    print(f"[{label}] {mb} / {total_mb} MB ({pct:.1f}%)", flush=True)
                else:
                    print(f"[{label}] {downloaded // (1024*1024)} MB", flush=True)
                last_log = time.time()

    print(f"[DONE] {label}", flush=True)


# Step 1: ComfyUI portable
if COMFYUI_DIR.exists():
    print("[SKIP] ComfyUI/ already exists", flush=True)
else:
    if not ARCHIVE_PATH.exists() or ARCHIVE_PATH.stat().st_size < 1024 * 1024:
        print("=== Downloading ComfyUI portable ===", flush=True)
        download(COMFYUI_URL, ARCHIVE_PATH, "ComfyUI portable")
    else:
        print(f"[SKIP] Archive already present ({ARCHIVE_PATH.stat().st_size // (1024*1024)} MB)", flush=True)

    if not SEVENZ_PATH.exists():
        print("=== Downloading 7zr.exe ===", flush=True)
        download(SEVENZ_URL, SEVENZ_PATH, "7zr.exe")

    print("=== Extracting (this may take a few minutes) ===", flush=True)
    result = subprocess.run(
        [str(SEVENZ_PATH), "x", str(ARCHIVE_PATH), f"-o{PROJECT_ROOT}", "-y"],
        capture_output=True, text=True
    )
    if result.returncode != 0:
        print(f"[ERROR] Extraction failed:\n{result.stdout}\n{result.stderr}", flush=True)
        sys.exit(1)

    for item in PROJECT_ROOT.iterdir():
        if (
            item.is_dir()
            and item != COMFYUI_DIR
            and (item / "run_nvidia_gpu.bat").exists()
        ):
            item.rename(COMFYUI_DIR)
            print(f"[RENAME] {item.name} -> ComfyUI/", flush=True)
            break
    else:
        print("[ERROR] Could not find extracted folder with run_nvidia_gpu.bat", flush=True)
        sys.exit(1)

    ARCHIVE_PATH.unlink(missing_ok=True)
    SEVENZ_PATH.unlink(missing_ok=True)
    print("=== ComfyUI extracted ===", flush=True)

# Step 2: Models
models_base = COMFYUI_DIR / "ComfyUI" / "models"
print("=== Downloading models ===", flush=True)
for rel_path, url in MODEL_FILES:
    dest = models_base / rel_path
    label = Path(rel_path).name
    print(f"--- {label} ---", flush=True)
    download(url, dest, label)

print("=== All done ===", flush=True)
print("Start ComfyUI: ComfyUI\\run_nvidia_gpu.bat", flush=True)
```

> **Note on versions:** The script pins ComfyUI v0.25.1. Update the `COMFYUI_URL` version tag when a newer release is desired.
> All model sources are non-gated (no HuggingFace token required).
> If the download is interrupted, re-running the script resumes from where it stopped.

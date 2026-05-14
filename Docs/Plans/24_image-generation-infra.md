# Plan 24: Local Image Generation Infrastructure

## Goal

Set up a local, GPU-accelerated image generation pipeline on Windows that can be invoked from Python scripts, then expose it as a Claude Code skill (`/generate-image`) for use during development.

## Approach

Use **ComfyUI** as the inference backend with a **FLUX.1-schnell** (or FLUX.1-dev) model. ComfyUI:
- Runs natively on Windows with CUDA (NVIDIA GPU) or DirectML (AMD/Intel)
- Exposes a local HTTP API (`http://127.0.0.1:8188`) that Python can call directly
- Produces state-of-the-art image quality suitable for game art
- Does not require WSL

The skill writes a temp Python script that uses only stdlib (`urllib`, `json`, `pathlib`) to call the ComfyUI API, poll for completion, and save the result to the requested output path.

## Steps

### 1. Install ComfyUI (one-time setup)

1. Download the ComfyUI portable Windows release from:
   `https://github.com/comfyanonymous/ComfyUI/releases`
   Choose the CUDA variant (`ComfyUI_windows_portable_nvidia.7z`) for NVIDIA GPUs,
   or the DirectML variant for AMD/Intel.

2. Extract into the project root as `ComfyUI/` (e.g. `F:\KonH\Work\Git\GlobalStrategy\ComfyUI\`).

3. `ComfyUI/` is already listed in `.gitignore` — no action needed.

4. Verify the installation launches:
   ```
   ComfyUI\run_nvidia_gpu.bat
   ```
   The UI should be accessible at `http://127.0.0.1:8188`.

### 2. Download a base model

FLUX.1 uses separate model files — not a single all-in-one checkpoint. Download all three components:

1. **UNet** — `flux1-schnell-fp8.safetensors` (~12 GB, quantised) or `flux1-schnell.safetensors` (~23 GB, full):
   `https://huggingface.co/black-forest-labs/FLUX.1-schnell`
   Place in: `ComfyUI\ComfyUI\models\unet\`

2. **VAE** — `ae.safetensors`:
   `https://huggingface.co/black-forest-labs/FLUX.1-schnell` (same repo, `vae/` folder)
   Place in: `ComfyUI\ComfyUI\models\vae\`

3. **CLIP encoders** — `t5xxl_fp8_e4m3fn.safetensors` and `clip_l.safetensors`:
   Available from the ComfyUI wiki or `https://huggingface.co/comfyanonymous/flux_text_encoders`
   Place in: `ComfyUI\ComfyUI\models\clip\`

### 3. No extra Python packages needed

The skill script uses only stdlib (`urllib`, `json`, `pathlib`, `random`, `time`). No pip install step required.

If image post-processing is needed in the future: `.venv\Scripts\pip.exe install pillow`

### 4. ComfyUI API workflow template

The Python call pattern the skill will embed (FLUX-specific node graph):

```python
import json, urllib.request, random, time, sys
from pathlib import Path

COMFY_URL = "http://127.0.0.1:8188"
PROMPT_PAYLOAD = {
    "3": {"class_type": "KSampler", "inputs": {
        "seed": random.randint(0, 2**32),
        "steps": 4, "cfg": 1.0, "sampler_name": "euler",
        "scheduler": "simple", "denoise": 1.0,
        "model": ["4", 0], "positive": ["6", 0],
        "negative": ["7", 0], "latent_image": ["5", 0]
    }},
    "4":  {"class_type": "UNETLoader",     "inputs": {"unet_name": "flux1-schnell-fp8.safetensors", "weight_dtype": "fp8_e4m3fn"}},
    "5":  {"class_type": "EmptyLatentImage","inputs": {"width": WIDTH, "height": HEIGHT, "batch_size": 1}},
    "6":  {"class_type": "CLIPTextEncode", "inputs": {"text": PROMPT_TEXT, "clip": ["10", 0]}},
    "7":  {"class_type": "CLIPTextEncode", "inputs": {"text": "", "clip": ["10", 0]}},
    "8":  {"class_type": "VAEDecode",      "inputs": {"samples": ["3", 0], "vae": ["11", 0]}},
    "9":  {"class_type": "SaveImage",      "inputs": {"filename_prefix": "output", "images": ["8", 0]}},
    "10": {"class_type": "DualCLIPLoader", "inputs": {"clip_name1": "t5xxl_fp8_e4m3fn.safetensors", "clip_name2": "clip_l.safetensors", "type": "flux"}},
    "11": {"class_type": "VAELoader",      "inputs": {"vae_name": "ae.safetensors"}}
}
# POST to /prompt → get prompt_id
# Poll /history/<prompt_id> with max 120 retries (1 s apart); error if exceeded
# Read filename/subfolder/type from history[id]["outputs"]["9"]["images"][0]
# Fetch image via /view?filename=<filename>&subfolder=<subfolder>&type=<type>
# Write PNG bytes to outputPath
```

The skill substitutes `WIDTH`, `HEIGHT`, and `PROMPT_TEXT` before posting.

### 5. Create the Claude Code skill

Create `.claude/commands/generate-image.md` with the following specification:

**Skill contract:**
- Arguments (passed via `$ARGUMENTS`): `outputPath size prompt`
  - `outputPath` — destination path relative to project root (e.g. `Assets/UI/Icons/hero.png`)
  - `size` — `WxH` string (e.g. `512x512`, `1024x1024`)
  - `prompt` — text description of the desired image (may contain spaces)

**Skill behaviour:**
1. Parse `$ARGUMENTS` by splitting on whitespace with `maxsplit=2`: `parts = args.split(None, 2)` — `outputPath = parts[0]`, `size = parts[1]`, `prompt = parts[2]`. If fewer than 3 parts, report an error and stop.
2. Write a self-contained Python script to `.tmp/run.py` that:
   - Splits `size` on `x` to get `width` and `height`.
   - Builds the ComfyUI workflow JSON (FLUX node graph from Step 4) with the given prompt and dimensions.
   - POSTs the workflow to `http://127.0.0.1:8188/prompt`.
   - Polls `http://127.0.0.1:8188/history/<prompt_id>` with a maximum of 120 retries (1 second apart); exits with an error message if the limit is exceeded.
   - Reads `filename`, `subfolder`, and `type` from `history[prompt_id]["outputs"]["9"]["images"][0]`.
   - Fetches the image via `http://127.0.0.1:8188/view?filename=<filename>&subfolder=<subfolder>&type=<type>`.
   - Writes the PNG bytes to `outputPath`, creating parent directories as needed.
   - Prints the resolved output path on success.
3. Run the script via `.claude\run.ps1`.
4. Delete `.tmp/run.py`.

**Skill file contents** (to be created at `.claude/commands/generate-image.md`):

```markdown
Generate an image using the local ComfyUI backend and save it to the given path.

Arguments (space-separated): outputPath size prompt
Example: Assets/UI/Icons/hero.png 512x512 medieval knight portrait, game art style

Steps:
1. Parse $ARGUMENTS by splitting on whitespace with maxsplit=2:
   parts = "$ARGUMENTS".split(None, 2)
   outputPath = parts[0], size = parts[1], prompt = parts[2] (everything after the second space).
   If fewer than 3 parts, report an error and stop.
2. Write a Python script to `.tmp/run.py` using only stdlib (urllib, json, pathlib, random, time):
   - Split size on "x" to get width and height.
   - Build a ComfyUI FLUX workflow JSON (UNETLoader + DualCLIPLoader + VAELoader + KSampler + SaveImage).
   - POST to http://127.0.0.1:8188/prompt and read prompt_id from the response.
   - Poll http://127.0.0.1:8188/history/<prompt_id> with max 120 retries (1 s apart); print error and exit if exceeded.
   - Read filename/subfolder/type from history[prompt_id]["outputs"]["9"]["images"][0].
   - Fetch image via http://127.0.0.1:8188/view?filename=<f>&subfolder=<s>&type=<t>.
   - Write PNG bytes to outputPath (create parent dirs if needed).
   - Print the saved path on success.
3. Run: PowerShell `& ".claude\run.ps1"`
4. Delete: PowerShell `Remove-Item .tmp\run.py`
5. Report the saved path and any console output.
```

### 6. Verify end-to-end

After setup, test with a small prompt:
```
/generate-image Assets/Textures/test_output.png 512x512 simple red circle on white background
```

Expected result: `Assets/Textures/test_output.png` written, readable in Unity after `refresh_unity`.

## Notes

- ComfyUI lives at `ComfyUI/` in the project root and is excluded from git via `.gitignore`. Start it with `ComfyUI\run_nvidia_gpu.bat` before invoking the skill — this is a manual one-time step per session.
- Model swaps (e.g. switching to FLUX.1-dev) only require changing `unet_name` in node `"4"` of the workflow.
- The FLUX.1-schnell workflow uses `cfg=1.0` and only 4 steps — fast but high quality. Increase steps to 8–20 for FLUX.1-dev.
- For icons and UI art, add style tags to the prompt: `flat icon, vector style, game UI, white background`.

Use /implement to start working on the plan or request changes.

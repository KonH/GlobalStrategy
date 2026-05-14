# Image Generation (ComfyUI + FLUX)

## Folder structure

The portable Windows release unpacks as:
```
ComfyUI/                    ← outer folder (gitignored)
├── run_nvidia_gpu.bat
├── python_embeded/
└── ComfyUI/                ← inner folder — the actual app
    └── models/
        ├── unet/           ← flux1-schnell-fp8.safetensors
        ├── vae/            ← ae.safetensors
        └── clip/           ← clip_l.safetensors, t5xxl_fp8_e4m3fn.safetensors
```

Models must go in `ComfyUI/ComfyUI/models/`, not `ComfyUI/models/`.
If ComfyUI returns 400 with empty node lists, the models are in the wrong folder.

## Scripts

- `.claude/generate_image.py` — single image, CLI args: `outputPath WxH prompt`
- `.claude/generate_images_batch.py` — reads `.tmp/images.json`, calls the above per entry

Run: `.venv\Scripts\python.exe .claude\generate_images_batch.py .tmp\images.json`
ComfyUI must be running at `http://127.0.0.1:8188` before invoking either script.

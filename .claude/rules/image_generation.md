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

## Windows Unicode encoding

Prompts that contain non-ASCII characters (e.g. accented names from localization) crash the batch script on Windows due to the cp1251 console codepage. Always prefix the run command with `$env:PYTHONUTF8 = '1'`:

```powershell
$env:PYTHONUTF8 = '1'; & ".venv\Scripts\python.exe" ".claude\generate_images_batch.py" ".tmp\images.json"
```

## Character portrait recipe

- **Output path:** `Assets/Textures/Characters/{characterId}.png`
- **Size:** `512x512`
- **Prompt template:**
  ```
  portrait of {name}, {regional style} {role description},
  19th century, historical oil painting style,
  formal attire, serious dignified expression,
  bust portrait, dark background, highly detailed, realistic painting
  ```

Regional style examples: `Argentine, Latin American, Spanish heritage` / `Japanese, East Asian, Meiji era` / `Ethiopian, East African` / `British, Victorian era`

Role descriptions: ruler → `statesman, ruler, head of state` · military → `military general, military officer` · diplomacy → `diplomat, foreign minister` · economic → `financier, economist, businessman` · secret → `politician, statesman, advisor`

Names and country pools come from `character_config.json` + `Assets/Localization/en.asset` (key prefix `character.name.part.*`).

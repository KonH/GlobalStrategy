---
name: generate-image
description: Generate project-bound raster assets with this repository's local ComfyUI and FLUX setup. Use when asked to create game icons, character portraits, card art, map thumbnails, or other PNG assets that should be saved in the Unity project rather than merely previewed in chat.
---

# Generate Game Image

Use the local ComfyUI server and the repository's reusable scripts. This skill is the canonical Codex workflow for the embedded image-generation setup.

## Environment rules

- Do not use this skill from a Ralph `code-only` or `full-env-headless` run. Those environments must leave Unity and image work untouched.
- Keep generated project assets under their intended `Assets/` path and do not overwrite an existing asset unless the request explicitly calls for it.
- Never substitute a hosted generator, placeholder service, stock-image URL, or emoji glyph for a required game asset.

## Ensure ComfyUI is ready

1. Check `http://127.0.0.1:8188/system_stats`.
2. If it is unavailable and `ComfyUI/run_nvidia_gpu.bat` exists, start it in a hidden detached process, then poll the endpoint for up to two minutes.
3. If it is unavailable and ComfyUI is not installed, run the bundled resumable installer in the background:

   ```powershell
   Start-Process -FilePath ".venv\Scripts\python.exe" `
     -ArgumentList ".codex\skills\generate-image\scripts\setup_comfyui.py --project-root ." `
     -WorkingDirectory (Get-Location) -WindowStyle Hidden `
     -RedirectStandardOutput ".tmp\comfyui_setup.log" `
     -RedirectStandardError ".tmp\comfyui_setup.err"
   ```

   Read the log periodically. The installer downloads the portable NVIDIA build and the non-gated FLUX.1-schnell checkpoint, VAE, and text encoders; it resumes partial downloads. When it reports `=== All done ===`, start ComfyUI and repeat the endpoint check.

4. If startup or installation fails, report the log error rather than silently switching tools.

## Generate one asset

1. Choose an output path and a size appropriate for the Unity use case. Use `512x512` for character portraits unless the request says otherwise.
2. Run the existing script with the project virtual environment. Prefix the command with `PYTHONUTF8=1` when the prompt contains non-ASCII text.

   ```powershell
   $env:PYTHONUTF8 = '1'
   & ".venv\Scripts\python.exe" "scripts\generate_image.py" "<output-path>" "<width>x<height>" "<prompt>"
   ```

3. Inspect the generated PNG before reporting it. For a Unity-bound asset, follow the relevant Unity import and `.meta` rules, then refresh Unity and check the console when an interactive Editor is available.

## Generate several assets

For a batch, create `.tmp/images.json` with entries shaped as `{ "output", "size", "prompt" }`, then run:

```powershell
$env:PYTHONUTF8 = '1'
& ".venv\Scripts\python.exe" "scripts\generate_images_batch.py" ".tmp\images.json"
```

The batch stops with an error when any entry fails. Report successful and failed paths separately; do not claim a partial batch is complete.

## Prompt and asset guidance

- State the subject, intended in-game use, visual style, composition, palette, and exclusions. Require no text or watermark unless text is explicitly needed.
- For icons, ask for a simple high-contrast silhouette that remains legible at its rendered size.
- For portrait prompts and regional/role wording, read [portrait-recipes.md](references/portrait-recipes.md).

Generate an image using the local ComfyUI backend and save it to the given path.

Arguments (space-separated): outputPath size prompt
Example: Assets/UI/Icons/hero.png 512x512 medieval knight portrait, game art style

Steps:
1. Parse $ARGUMENTS by splitting on whitespace with maxsplit=2:
   parts = "$ARGUMENTS".split(None, 2)
   outputPath = parts[0], size = parts[1], prompt = parts[2] (everything after the second space).
   If fewer than 3 parts, report an error and stop.
2. Check that ComfyUI is running — follow all steps in `/setup-comfy-ui`.
   If setup reports that ComfyUI is not installed or could not start, stop here with that message.
3. Run the reusable script via PowerShell, passing the three arguments:
   & ".venv\Scripts\python.exe" ".claude\generate_image.py" "<outputPath>" "<size>" "<prompt>"
4. Report the saved path and any console output.

"""
Usage: python generate_image.py <outputPath> <WxH> <prompt text>
Requires ComfyUI running at http://127.0.0.1:8188 with FLUX.1-schnell models loaded.
"""
import json, urllib.request, random, time, sys
from pathlib import Path

def main():
    if len(sys.argv) < 4:
        print("Usage: generate_image.py <outputPath> <WxH> <prompt>")
        sys.exit(1)

    output_path = Path(sys.argv[1])
    width, height = (int(x) for x in sys.argv[2].split("x"))
    prompt_text = sys.argv[3]

    COMFY_URL = "http://127.0.0.1:8188"

    workflow = {
        "3": {"class_type": "KSampler", "inputs": {
            "seed": random.randint(0, 2**32),
            "steps": 4, "cfg": 1.0, "sampler_name": "euler",
            "scheduler": "simple", "denoise": 1.0,
            "model": ["4", 0], "positive": ["6", 0],
            "negative": ["7", 0], "latent_image": ["5", 0]
        }},
        "4":  {"class_type": "UNETLoader",      "inputs": {"unet_name": "flux1-schnell-fp8.safetensors", "weight_dtype": "fp8_e4m3fn"}},
        "5":  {"class_type": "EmptyLatentImage", "inputs": {"width": width, "height": height, "batch_size": 1}},
        "6":  {"class_type": "CLIPTextEncode",  "inputs": {"text": prompt_text, "clip": ["10", 0]}},
        "7":  {"class_type": "CLIPTextEncode",  "inputs": {"text": "", "clip": ["10", 0]}},
        "8":  {"class_type": "VAEDecode",        "inputs": {"samples": ["3", 0], "vae": ["11", 0]}},
        "9":  {"class_type": "SaveImage",        "inputs": {"filename_prefix": "output", "images": ["8", 0]}},
        "10": {"class_type": "DualCLIPLoader",  "inputs": {"clip_name1": "t5xxl_fp8_e4m3fn.safetensors", "clip_name2": "clip_l.safetensors", "type": "flux"}},
        "11": {"class_type": "VAELoader",        "inputs": {"vae_name": "ae.safetensors"}}
    }

    payload = json.dumps({"prompt": workflow}).encode()
    req = urllib.request.Request(
        f"{COMFY_URL}/prompt", data=payload,
        headers={"Content-Type": "application/json"}
    )
    with urllib.request.urlopen(req) as resp:
        result = json.loads(resp.read())
    prompt_id = result["prompt_id"]
    print(f"Queued prompt_id: {prompt_id}")

    for i in range(120):
        time.sleep(1)
        with urllib.request.urlopen(f"{COMFY_URL}/history/{prompt_id}") as resp:
            history = json.loads(resp.read())
        if prompt_id in history:
            img_info = history[prompt_id]["outputs"]["9"]["images"][0]
            filename  = img_info["filename"]
            subfolder = img_info["subfolder"]
            img_type  = img_info["type"]
            url = f"{COMFY_URL}/view?filename={filename}&subfolder={subfolder}&type={img_type}"
            with urllib.request.urlopen(url) as resp:
                png_bytes = resp.read()
            output_path.parent.mkdir(parents=True, exist_ok=True)
            output_path.write_bytes(png_bytes)
            print(f"Saved: {output_path}")
            sys.exit(0)
        if i % 10 == 0:
            print(f"Waiting... ({i}s)")

    print("ERROR: Timed out after 120 retries.")
    sys.exit(1)

if __name__ == "__main__":
    main()

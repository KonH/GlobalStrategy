#!/usr/bin/env python3
"""Upload a Unity WebGL build folder to Unity Play.

Mirrors the WebGL Publisher package (com.unity.connect.share) flow:
1. Authenticate against Unity ID
2. Zip the build directory (200 MB limit)
3. POST multipart to https://play.unity.com/api/webgl/upload
4. Poll https://play.unity.com/api/webgl/progress until complete

Auth uses the same developer login endpoint documented by Unity Automated QA:
  POST https://api.unity.com/v1/core/api/login
  {"username","password","grant_type":"PASSWORD"}
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import time
import uuid
import zipfile
from pathlib import Path
from typing import Any
from urllib import error, request

LOGIN_URL = "https://api.unity.com/v1/core/api/login"
UPLOAD_URL = "https://play.unity.com/api/webgl/upload"
PROGRESS_URL = "https://play.unity.com/api/webgl/progress"
ZIP_LIMIT_BYTES = 200 * 1024 * 1024
DEFAULT_TITLE = "Global Strategy"
# Existing Unity Play game from README / live demo URL.
DEFAULT_PROJECT_ID = "790490b4-2b09-4c1c-8a2e-df23f6b43b47"


def _die(message: str, code: int = 1) -> None:
    print(f"error: {message}", file=sys.stderr)
    raise SystemExit(code)


def _json_request(
    url: str,
    *,
    method: str = "GET",
    headers: dict[str, str] | None = None,
    body: bytes | None = None,
    timeout: float = 120,
) -> Any:
    req = request.Request(url, data=body, method=method)
    for key, value in (headers or {}).items():
        req.add_header(key, value)
    try:
        with request.urlopen(req, timeout=timeout) as resp:
            raw = resp.read().decode("utf-8")
            return json.loads(raw) if raw else {}
    except error.HTTPError as exc:
        detail = exc.read().decode("utf-8", errors="replace")
        _die(f"HTTP {exc.code} from {url}: {detail}")
    except error.URLError as exc:
        _die(f"request failed for {url}: {exc}")


def login(email: str, password: str) -> str:
    payload = json.dumps(
        {
            "username": email,
            "password": password,
            "grant_type": "PASSWORD",
        }
    ).encode("utf-8")
    data = _json_request(
        LOGIN_URL,
        method="POST",
        headers={"Content-Type": "application/json"},
        body=payload,
        timeout=60,
    )
    token = data.get("access_token")
    if not token:
        _die(f"login response missing access_token: {data}")
    return str(token)


def resolve_build_dir(build_dir: Path) -> Path:
    """Return the directory that actually holds index.html.

    unity-builder writes the player to <buildsPath>/<targetPlatform>/<buildName>,
    so a caller pointing at build/WebGL can end up one level above the real root.
    Accept that by descending to the shallowest index.html below the given path.
    """
    if not build_dir.is_dir():
        _die(f"build directory not found: {build_dir}")
    if (build_dir / "index.html").is_file():
        return build_dir

    candidates = sorted(
        (p.parent for p in build_dir.glob("*/index.html") if p.is_file()),
        key=lambda p: p.as_posix(),
    )
    if len(candidates) == 1:
        print(f"note: using nested build directory {candidates[0]}")
        return candidates[0]
    if len(candidates) > 1:
        listed = ", ".join(p.as_posix() for p in candidates)
        _die(f"multiple nested build directories with index.html under {build_dir}: {listed}")

    contents = ", ".join(sorted(p.name for p in build_dir.iterdir())) or "<empty>"
    _die(f"build directory is missing index.html: {build_dir} (contains: {contents})")
    raise AssertionError("unreachable")


def zip_build(build_dir: Path, zip_path: Path) -> None:
    if zip_path.exists():
        zip_path.unlink()

    with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED) as zf:
        for path in sorted(build_dir.rglob("*")):
            if path.is_file():
                zf.write(path, path.relative_to(build_dir).as_posix())

    size = zip_path.stat().st_size
    print(f"zipped {build_dir} -> {zip_path} ({size} bytes)")
    if size > ZIP_LIMIT_BYTES:
        _die(
            f"zip is {size} bytes; Unity Play limit is {ZIP_LIMIT_BYTES} bytes "
            "(same as WebGL Publisher)"
        )


def _multipart_body(
    fields: dict[str, str],
    file_field: str,
    file_name: str,
    file_bytes: bytes,
    content_type: str,
) -> tuple[bytes, str]:
    boundary = f"----UnityPlayBoundary{uuid.uuid4().hex}"
    lines: list[bytes] = []

    for name, value in fields.items():
        lines.append(f"--{boundary}".encode())
        lines.append(f'Content-Disposition: form-data; name="{name}"'.encode())
        lines.append(b"")
        lines.append(value.encode("utf-8"))

    lines.append(f"--{boundary}".encode())
    lines.append(
        (
            f'Content-Disposition: form-data; name="{file_field}"; '
            f'filename="{file_name}"'
        ).encode()
    )
    lines.append(f"Content-Type: {content_type}".encode())
    lines.append(b"")
    lines.append(file_bytes)
    lines.append(f"--{boundary}--".encode())
    lines.append(b"")
    return b"\r\n".join(lines), boundary


def upload(
    token: str,
    zip_path: Path,
    *,
    title: str,
    project_id: str,
    build_guid: str,
) -> str:
    # Unity Play rejects the upload with HTTP 422 ("buildGUID must be a string")
    # when the field is absent, so always send one. The Publisher package sends
    # Application.buildGUID (32 hex chars); a fresh GUID per deploy matches that
    # shape and keeps each upload distinct.
    if not build_guid:
        build_guid = uuid.uuid4().hex
        print(f"no build GUID supplied; generated {build_guid}")

    fields = {"title": title, "buildGUID": build_guid}
    if project_id:
        fields["projectId"] = project_id

    body, boundary = _multipart_body(
        fields,
        file_field="file",
        file_name=zip_path.name,
        file_bytes=zip_path.read_bytes(),
        content_type="application/zip",
    )
    print(f"uploading {zip_path.name} to Unity Play (title={title!r}, projectId={project_id or 'new'})")
    data = _json_request(
        UPLOAD_URL,
        method="POST",
        headers={
            "Authorization": f"Bearer {token}",
            "X-Requested-With": "XMLHTTPREQUEST",
            "Content-Type": f"multipart/form-data; boundary={boundary}",
        },
        body=body,
        timeout=600,
    )
    key = data.get("key")
    if not key:
        _die(f"upload response missing key: {data}")
    print(f"upload accepted; processing key={key}")
    return str(key)


def wait_for_progress(token: str, key: str, *, poll_seconds: float = 1.5, timeout_seconds: float = 1800) -> dict[str, Any]:
    started = time.time()
    while True:
        if time.time() - started > timeout_seconds:
            _die(f"timed out waiting for Unity Play processing (key={key})")

        data = _json_request(
            f"{PROGRESS_URL}?key={key}",
            headers={
                "Authorization": f"Bearer {token}",
                "X-Requested-With": "XMLHTTPREQUEST",
            },
            timeout=60,
        )
        progress = int(data.get("progress") or 0)
        err = data.get("error") or ""
        url = data.get("url") or ""
        project_id = data.get("projectId") or ""
        print(f"processing progress={progress}% projectId={project_id or '-'} url={url or '-'}")
        if err:
            _die(f"Unity Play processing failed: {err}")
        if progress >= 100:
            return data
        time.sleep(poll_seconds)


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--build-dir",
        type=Path,
        default=Path("build/WebGL"),
        help="Path to the WebGL build output directory (must contain index.html)",
    )
    parser.add_argument(
        "--title",
        default=os.environ.get("UNITY_PLAY_TITLE", DEFAULT_TITLE),
        help="Unity Play game title",
    )
    parser.add_argument(
        "--project-id",
        default=os.environ.get("UNITY_PLAY_PROJECT_ID", DEFAULT_PROJECT_ID),
        help="Existing Unity Play project/game id (omit/empty to create a new game)",
    )
    parser.add_argument(
        "--build-guid",
        default=os.environ.get("UNITY_PLAY_BUILD_GUID", ""),
        help="Optional build GUID (GUID.txt contents from a Publisher build)",
    )
    parser.add_argument(
        "--zip-path",
        type=Path,
        default=Path("connectwebgl.zip"),
        help="Temporary zip path (deleted after upload unless --keep-zip)",
    )
    parser.add_argument("--keep-zip", action="store_true", help="Keep the zip after upload")
    parser.add_argument(
        "--email",
        default=os.environ.get("UNITY_EMAIL", ""),
        help="Unity account email (or set UNITY_EMAIL)",
    )
    parser.add_argument(
        "--password",
        default=os.environ.get("UNITY_PASSWORD", ""),
        help="Unity account password (or set UNITY_PASSWORD)",
    )
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    # Keep progress prints interleaved with stderr correctly in CI logs.
    sys.stdout.reconfigure(line_buffering=True)
    args = parse_args(argv)
    email = args.email.strip()
    password = args.password
    if not email or not password:
        _die("UNITY_EMAIL and UNITY_PASSWORD are required")

    build_dir = resolve_build_dir(args.build_dir)
    zip_build(build_dir, args.zip_path)
    token = login(email, password)
    key = upload(
        token,
        args.zip_path,
        title=args.title,
        project_id=(args.project_id or "").strip(),
        build_guid=(args.build_guid or "").strip(),
    )
    result = wait_for_progress(token, key)
    url = result.get("url") or ""
    project_id = result.get("projectId") or ""
    print("Unity Play deploy complete")
    if project_id:
        print(f"projectId={project_id}")
    if url:
        print(f"url={url}")
    else:
        # Stable public URL for known project ids.
        if project_id:
            print(f"url=https://play.unity.com/en/games/{project_id}/global-strategy")

    if not args.keep_zip and args.zip_path.exists():
        args.zip_path.unlink()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

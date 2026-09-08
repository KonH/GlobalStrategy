#!/usr/bin/env python3
"""Ensure Assets/Plugins/Core plugin DLLs exist and are not older than src/.

Plugin DLLs are gitignored. Unity and agents regenerate them with
`dotnet build src/GlobalStrategy.Core.sln -c Release`. This script is the
shared check+rebuild entry point for Claude/Codex/Cursor skills.

Keep WATCH_PREFIXES in sync with Assets/Scripts/Editor/PluginDlls/PluginDllRegenerator.cs.
"""

from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path

PLUGINS_DIR = Path("Assets/Plugins/Core")
SOLUTION = Path("src/GlobalStrategy.Core.sln")
LOG_PATH = Path(".tmp/dotnet-build.log")

# Projects MSBuild outputs into Assets/Plugins/Core on Release, plus shared
# compile settings. A change under any of these can require a rebuild.
WATCH_PREFIXES = [
    "src/Core.Configs/",
    "src/Core.Map/",
    "src/ECS.Core/",
    "src/ECS.Core.Extensions/",
    "src/ECS.Viewer/",
    "src/Game.Bots/",
    "src/Game.Commands/",
    "src/Game.Commands.Text/",
    "src/Game.Common/",
    "src/Game.Components/",
    "src/Game.Configs/",
    "src/Game.E2E/",
    "src/Game.Main/",
    "src/Game.Systems/",
    "src/Directory.Build.props",
]

SKIP_DIR_NAMES = {"bin", "obj"}


def expected_dlls(plugins_dir: Path = PLUGINS_DIR) -> list[Path]:
    """DLL paths implied by tracked `*.dll.meta` sidecars."""
    dlls: list[Path] = []
    if not plugins_dir.is_dir():
        return dlls
    for meta in sorted(plugins_dir.glob("*.dll.meta")):
        dlls.append(meta.with_name(meta.name[: -len(".meta")]))
    return dlls


def _is_skipped_dir(path: Path, root: Path) -> bool:
    try:
        parts = path.relative_to(root).parts
    except ValueError:
        return True
    return any(part in SKIP_DIR_NAMES for part in parts)


def newest_watch_mtime(repo_root: Path) -> float | None:
    newest: float | None = None
    for prefix in WATCH_PREFIXES:
        target = repo_root / prefix
        if target.is_file():
            mtime = target.stat().st_mtime
            newest = mtime if newest is None else max(newest, mtime)
            continue
        if not target.is_dir():
            continue
        for path in target.rglob("*"):
            if not path.is_file():
                continue
            if _is_skipped_dir(path, target):
                continue
            if path.suffix.lower() not in {".cs", ".csproj"} and path.name != "Directory.Build.props":
                continue
            mtime = path.stat().st_mtime
            newest = mtime if newest is None else max(newest, mtime)
    return newest


def missing_dlls(dlls: list[Path]) -> list[Path]:
    return [dll for dll in dlls if not dll.is_file()]


def needs_rebuild(repo_root: Path, plugins_dir: Path = PLUGINS_DIR) -> tuple[bool, str]:
    dlls = expected_dlls(plugins_dir)
    if not dlls:
        return True, f"no *.dll.meta files found under {plugins_dir.as_posix()}"

    missing = missing_dlls(dlls)
    if missing:
        names = ", ".join(dll.name for dll in missing)
        return True, f"missing plugin DLL(s): {names}"

    src_mtime = newest_watch_mtime(repo_root)
    if src_mtime is None:
        return False, "no watched src/ files; existing DLLs left as-is"

    oldest_dll_mtime = min(dll.stat().st_mtime for dll in dlls)
    if src_mtime > oldest_dll_mtime:
        return True, "src/ is newer than Assets/Plugins/Core DLLs"
    return False, "plugin DLLs are up to date"


def run_release_build(repo_root: Path) -> int:
    log_path = repo_root / LOG_PATH
    log_path.parent.mkdir(parents=True, exist_ok=True)
    solution = repo_root / SOLUTION
    with log_path.open("w", encoding="utf-8") as log_file:
        result = subprocess.run(
            ["dotnet", "build", str(solution), "-c", "Release"],
            cwd=repo_root,
            stdout=log_file,
            stderr=subprocess.STDOUT,
            check=False,
        )
    return result.returncode


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--check",
        action="store_true",
        help="Exit 0 if up to date, 1 if a rebuild is needed; never build.",
    )
    parser.add_argument(
        "--force",
        action="store_true",
        help="Always run a Release build, even if DLLs look current.",
    )
    args = parser.parse_args(argv)

    repo_root = Path.cwd()
    plugins_dir = repo_root / PLUGINS_DIR
    rebuild, reason = needs_rebuild(repo_root, plugins_dir)

    if args.check:
        print(reason)
        return 1 if rebuild else 0

    if not args.force and not rebuild:
        print(f"UP_TO_DATE: {reason}")
        return 0

    print(f"REBUILDING: {reason}")
    code = run_release_build(repo_root)
    if code != 0:
        print(
            f"Release build failed (exit {code}); see {LOG_PATH.as_posix()}",
            file=sys.stderr,
        )
        return code

    still_missing = missing_dlls(expected_dlls(plugins_dir))
    if still_missing:
        names = ", ".join(dll.name for dll in still_missing)
        print(
            f"Release build succeeded but plugin DLL(s) still missing: {names}",
            file=sys.stderr,
        )
        return 1

    print("REBUILT: Assets/Plugins/Core plugin DLLs are ready")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3
"""Fail a PR that changes a Plugins-bound src/ project without rebuilding DLLs.

`src/GlobalStrategy.Core.sln -c Release` writes several projects' output straight
into `Assets/Plugins/Core/` (see `.claude/rules/unity/plugins.md`), which Unity then
consumes. If a PR edits one of those projects' sources but never re-runs the Release
build and commits the refreshed DLLs, Unity keeps building against stale code.

This walks the PR's commits (oldest to newest) and checks that the last commit
touching a Plugins-bound project is not *after* the last commit touching
`Assets/Plugins/Core/` — i.e. the DLLs were rebuilt in the same commit as the source
change, or in a later one.
"""

from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path

# Projects MSBuild outputs (directly via <OutputPath>, or transitively via a
# ProjectReference copy) into Assets/Plugins/Core when building
# src/GlobalStrategy.Core.sln -c Release. Keep in sync with
# .claude/rules/unity/plugins.md and the <OutputPath>Assets/Plugins/Core</OutputPath>
# csproj entries.
PLUGIN_SRC_PATHS = [
    "src/Core.Configs/",
    "src/Core.Map/",
    "src/ECS.Core/",
    "src/ECS.Core.Extensions/",
    "src/ECS.Viewer/",
    "src/Game.Bots/",
    "src/Game.Commands/",
    "src/Game.Common/",
    "src/Game.Components/",
    "src/Game.Configs/",
    "src/Game.Main/",
    "src/Game.Systems/",
    # Applies to every project's compile settings (e.g. TreatWarningsAsErrors),
    # so treat it the same as a change to any one of them.
    "src/Directory.Build.props",
]

PLUGINS_OUTPUT_PATH = "Assets/Plugins/Core/"


def _run_git(args: list[str]) -> str:
    result = subprocess.run(
        ["git", *args], capture_output=True, text=True, check=True
    )
    return result.stdout


def commits_between(base_sha: str, head_sha: str) -> list[str]:
    out = _run_git(["rev-list", "--reverse", f"{base_sha}..{head_sha}"])
    return [line for line in out.splitlines() if line]


def changed_files(sha: str) -> list[str]:
    out = _run_git(["diff-tree", "--no-commit-id", "--name-only", "-r", sha])
    return [line for line in out.splitlines() if line]


def touches(files: list[str], prefixes: list[str]) -> bool:
    return any(f.startswith(prefix) for f in files for prefix in prefixes)


def find_last_index(commits: list[str], prefixes: list[str]) -> int:
    """Return the index (in `commits`) of the last commit touching `prefixes`, or -1."""
    last = -1
    for i, sha in enumerate(commits):
        if touches(changed_files(sha), prefixes):
            last = i
    return last


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-sha", required=True, help="PR base commit SHA")
    parser.add_argument("--head-sha", required=True, help="PR head commit SHA")
    args = parser.parse_args(argv)

    commits = commits_between(args.base_sha, args.head_sha)
    if not commits:
        print("No commits in range; nothing to check.")
        return 0

    last_src_index = find_last_index(commits, PLUGIN_SRC_PATHS)
    if last_src_index == -1:
        print("No changes to Plugins-bound src/ projects; nothing to check.")
        return 0

    last_plugins_index = find_last_index(commits, [PLUGINS_OUTPUT_PATH])
    last_src_sha = commits[last_src_index]

    if last_plugins_index >= last_src_index:
        print(
            f"OK: {PLUGINS_OUTPUT_PATH} was rebuilt at or after the last relevant "
            f"src/ change ({last_src_sha[:12]})."
        )
        return 0

    print(
        f"::error::src/ changed in commit {last_src_sha} (a project that builds "
        f"into {PLUGINS_OUTPUT_PATH}), but no later commit in this PR updated "
        f"{PLUGINS_OUTPUT_PATH}.",
        file=sys.stderr,
    )
    print(
        "Run `dotnet build src/GlobalStrategy.Core.sln -c Release` and commit the "
        "refreshed DLLs under Assets/Plugins/Core/ (see .claude/rules/unity/plugins.md).",
        file=sys.stderr,
    )
    return 1


if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3
# Keeps the ClaudeTools submodule populated so the k@claude-tools plugin resolves
# in every session type (local, desktop, cloud) without depending on a remote
# marketplace fetch at plugin-load time. Cross-platform (Windows/Linux) since
# SessionStart hooks can't rely on a POSIX shell being available.
import os
import subprocess
import sys

project_dir = os.environ.get("CLAUDE_PROJECT_DIR", os.getcwd())

try:
    subprocess.run(
        ["git", "submodule", "update", "--init", "--recursive", "--", ".claude/vendor/claude-tools"],
        cwd=project_dir,
        check=False,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )
except OSError:
    pass

sys.exit(0)

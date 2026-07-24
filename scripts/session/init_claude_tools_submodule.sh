#!/usr/bin/env bash
# Keeps the ClaudeTools submodule populated so the k@claude-tools plugin resolves
# in every session type (local, desktop, cloud) without depending on a remote
# marketplace fetch at plugin-load time.
set -uo pipefail

cd "${CLAUDE_PROJECT_DIR:-.}" || exit 0

git submodule update --init --recursive -- .claude/vendor/claude-tools >/dev/null 2>&1 || true

exit 0

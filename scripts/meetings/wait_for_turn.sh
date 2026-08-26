#!/usr/bin/env bash
exec python3 "$(dirname "$0")/wait_for_turn.py" "$@"

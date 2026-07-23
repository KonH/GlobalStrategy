"""Segments a single Claude Code session transcript (.jsonl) into stage / sub-stage
rows. See Docs/Specs/26_07_22_17_spec-dev-stats/plan.md section 3 for the segmentation
rules this implements.
"""

import json
import re
from pathlib import Path

from scripts.stats.segmentation import split_into_substages

COMMAND_STAGE_RE = re.compile(r"<command-name>/(specify|plan|implement)</command-name>")
COMMAND_NAME_RE = re.compile(r"<command-name>")


def _stage_from_command(text):
    match = COMMAND_STAGE_RE.search(text)
    if match is None:
        return None
    name = match.group(1)
    return "spec" if name == "specify" else name


def _message_text(message):
    content = message.get("content")
    if isinstance(content, str):
        return content
    if isinstance(content, list):
        return "".join(
            block.get("text", "") for block in content if isinstance(block, dict) and block.get("type") == "text"
        )
    return ""


def _is_tool_result_only(message):
    content = message.get("content")
    if not isinstance(content, list):
        return False
    return len(content) > 0 and all(
        isinstance(block, dict) and block.get("type") == "tool_result" for block in content
    )


def _write_paths_from_assistant(message):
    paths = []
    for block in message.get("content") or []:
        if isinstance(block, dict) and block.get("type") == "tool_use" and block.get("name") in ("Write", "Edit"):
            path = (block.get("input") or {}).get("file_path")
            if path:
                paths.append(path)
    return paths


def parse_claude_transcript(path):
    """Returns a list of row dicts, one per stage/sub-stage segment found in the
    transcript, each carrying session_id/provider/model/effort/token sums/write_paths/
    git_branch/context/start/end/stage. Sessions with no /specify, /plan, or
    /implement command marker produce an empty list (nothing to attribute).
    """
    path = Path(path)
    lines = path.read_text(encoding="utf-8").splitlines()

    session_id = None
    stages = []  # list of (base_stage, context, [turns])
    current = None
    any_stage_started = False

    for raw_line in lines:
        raw_line = raw_line.strip()
        if not raw_line:
            continue
        try:
            obj = json.loads(raw_line)
        except json.JSONDecodeError:
            continue

        if session_id is None and obj.get("sessionId"):
            session_id = obj["sessionId"]

        line_type = obj.get("type")
        git_branch = obj.get("gitBranch")

        if line_type == "user":
            message = obj.get("message", {})
            text = _message_text(message)
            new_stage = _stage_from_command(text)
            if new_stage is not None:
                if current is not None:
                    stages.append(current)
                context = "continued" if any_stage_started else "fresh"
                any_stage_started = True
                current = (new_stage, context, [])
                continue

            if current is None:
                continue

            is_command_marker = bool(COMMAND_NAME_RE.search(text))
            is_meta = bool(obj.get("isMeta"))
            is_human_turn = (
                not is_command_marker and not is_meta
                and not _is_tool_result_only(message) and text.strip() != ""
            )
            current[2].append({
                "is_human_turn": is_human_turn,
                "timestamp": obj.get("timestamp"),
                "git_branch": git_branch,
            })
        elif line_type == "assistant":
            if current is None:
                continue
            message = obj.get("message", {})
            usage = message.get("usage") or {}
            current[2].append({
                "is_completed_response": message.get("stop_reason") != "tool_use",
                "timestamp": obj.get("timestamp"),
                "model": message.get("model"),
                "effort": obj.get("effort"),
                "usage": {
                    "input_tokens": usage.get("input_tokens", 0),
                    "cached_input_tokens": usage.get("cache_read_input_tokens", 0),
                    "output_tokens": usage.get("output_tokens", 0),
                },
                "write_paths": _write_paths_from_assistant(message),
                "git_branch": git_branch,
            })

    if current is not None:
        stages.append(current)

    rows = []
    for base_stage, context, turns in stages:
        for segment in split_into_substages(base_stage, context, turns):
            rows.append({
                "session_id": session_id,
                "provider": "claude",
                "stage": segment.stage,
                "context": segment.context,
                "start": segment.start,
                "end": segment.end,
                "model": segment.model,
                "effort": segment.effort,
                "input_tokens": segment.input_tokens,
                "cached_input_tokens": segment.cached_input_tokens,
                "output_tokens": segment.output_tokens,
                "write_paths": segment.write_paths,
                "git_branch": segment.git_branch,
            })
    return rows

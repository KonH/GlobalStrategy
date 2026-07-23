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
# Optional "plugin:" prefix (e.g. "k:specify"); anchored so names that merely contain a
# stage word ("implement-bot-feature") never match.
SKILL_STAGE_RE = re.compile(r"^(?:[\w.-]+:)?(specify|plan|implement)$")


def _stage_from_command(text):
    match = COMMAND_STAGE_RE.search(text)
    if match is None:
        return None
    name = match.group(1)
    return "spec" if name == "specify" else name


def _stage_from_skill(message):
    """Stage from a Skill tool invocation in an assistant message. Remote /
    skill-driven sessions never get a <command-name> marker (the CLI only injects
    it for typed slash commands), so the Skill tool_use is their structural
    stage marker."""
    for block in message.get("content") or []:
        if isinstance(block, dict) and block.get("type") == "tool_use" and block.get("name") == "Skill":
            skill = (block.get("input") or {}).get("skill") or ""
            match = SKILL_STAGE_RE.match(skill)
            if match is not None:
                name = match.group(1)
                return "spec" if name == "specify" else name
    return None


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
    transcript, each carrying session_id/provider/model/token sums/write_paths/
    git_branch/context/start/end/stage. A stage starts at a /specify, /plan, or
    /implement command marker (typed slash command) OR a Skill tool invocation of
    those skills (remote/skill-driven sessions, where no marker is injected); a
    repeated Skill invocation of the already-current stage (e.g. a retry) does not
    restart it. Sessions with neither produce an empty list (nothing to attribute).
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
            message = obj.get("message", {})
            skill_stage = _stage_from_skill(message)
            if skill_stage is not None and (current is None or current[0] != skill_stage):
                if current is not None:
                    stages.append(current)
                context = "continued" if any_stage_started else "fresh"
                any_stage_started = True
                current = (skill_stage, context, [])
            if current is None:
                continue
            usage = message.get("usage") or {}
            current[2].append({
                "is_completed_response": message.get("stop_reason") != "tool_use",
                "timestamp": obj.get("timestamp"),
                "model": message.get("model"),
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
                "input_tokens": segment.input_tokens,
                "cached_input_tokens": segment.cached_input_tokens,
                "output_tokens": segment.output_tokens,
                "write_paths": segment.write_paths,
                "git_branch": segment.git_branch,
            })
    return rows

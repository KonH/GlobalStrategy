"""Segments a single Codex rollout (.jsonl) into stage / sub-stage rows.

Codex has no <command-name> structural marker, so stage detection depends on
matching the literal prompt strings scripts/automation/codex/ralph.py and
scripts/automation/codex/handle_issues.py send. Kept as one small table here so a
wrapper's prompt wording change only needs one update.
"""

import json
from pathlib import Path

from scripts.stats.segmentation import split_into_substages

# (regex fragment to search for in a user_message's text, stage). Checked in order;
# first match wins. Ralph's own phases (create-prd/loop/complete-prd) are all part of
# implementing an already-planned spec, so they all map to "implement".
STAGE_MATCH_TABLE = [
    ("create-prd.md", "implement"),
    ("complete-prd.md", "implement"),
    ("follow these iteration instructions exactly", "implement"),
    ("codex-feature-issue/SKILL.md", None),  # batch handler - see module docstring below
    ("/specify", "spec"),
    ("/plan", "plan"),
    ("/implement", "implement"),
]


def _match_stage(text):
    for needle, stage in STAGE_MATCH_TABLE:
        if needle in text:
            return stage
    return None


def _cwd_matches_repo(cwd, repo_dir_name="GlobalStrategy"):
    if not cwd:
        return False
    return Path(cwd.replace("\\", "/")).name == repo_dir_name


def parse_codex_rollout(path, repo_dir_name="GlobalStrategy"):
    """Returns a list of row dicts (same shape as claude_transcript.parse_claude_transcript).

    Filters out rollouts whose thread_source is "subagent" (internal judge-model calls
    with no relation to a spec/plan/implement stage) and rollouts whose cwd doesn't
    match this repo. handle_issues.py-driven rollouts process a batch of issues that
    may span multiple stages within one CLI invocation with no per-stage marker in the
    outer prompt - those intentionally produce no rows from this per-file parser (they
    still get attributed via the wrapper's own --record calls, see collect_usage.py).
    """
    path = Path(path)
    lines = path.read_text(encoding="utf-8").splitlines()

    session_id = None
    cwd = None
    thread_source = None
    model = None
    stages = []
    current = None
    any_stage_started = False
    running_totals = {"input_tokens": 0, "cached_input_tokens": 0, "output_tokens": 0}

    for raw_line in lines:
        raw_line = raw_line.strip()
        if not raw_line:
            continue
        try:
            obj = json.loads(raw_line)
        except json.JSONDecodeError:
            continue

        payload = obj.get("payload", {})
        obj_type = obj.get("type")

        if obj_type == "session_meta":
            session_id = payload.get("id") or payload.get("session_id")
            cwd = payload.get("cwd")
            thread_source = payload.get("thread_source")
            continue

        if obj_type == "event_msg":
            event_type = payload.get("type")
            if event_type == "thread_settings_applied":
                model = (payload.get("thread_settings") or {}).get("model") or model
            elif event_type == "user_message":
                text = payload.get("message", "")
                new_stage = _match_stage(text)
                if new_stage is not None:
                    if current is not None:
                        stages.append(current)
                    context = "continued" if any_stage_started else "fresh"
                    any_stage_started = True
                    current = (new_stage, context, [])
                    continue
                if current is not None:
                    current[2].append({
                        "is_human_turn": True,
                        "timestamp": obj.get("timestamp"),
                    })
            elif event_type == "agent_message" and current is not None:
                current[2].append({
                    "is_completed_response": True,
                    "timestamp": obj.get("timestamp"),
                    "model": model,
                })
            elif event_type == "token_count" and current is not None:
                usage = (payload.get("info") or {}).get("total_token_usage") or {}
                # token_count events report a cumulative running total for the whole
                # rollout, not per-stage - diff against the rollout-wide running total
                # (not reset per stage) so stage 2+ isn't inflated by stage 1's usage.
                cumulative = {
                    "input_tokens": usage.get("input_tokens", 0),
                    "cached_input_tokens": usage.get("cached_input_tokens", 0),
                    "output_tokens": usage.get("output_tokens", 0),
                }
                delta = {k: cumulative[k] - running_totals[k] for k in running_totals}
                running_totals = cumulative
                current[2].append({"timestamp": obj.get("timestamp"), "usage": delta})

    if current is not None:
        stages.append(current)

    if thread_source == "subagent" or not _cwd_matches_repo(cwd, repo_dir_name):
        return []

    rows = []
    for base_stage, context, turns in stages:
        for segment in split_into_substages(base_stage, context, turns):
            rows.append({
                "session_id": session_id,
                "provider": "codex",
                "stage": segment.stage,
                "context": segment.context,
                "start": segment.start,
                "end": segment.end,
                "model": segment.model,
                "input_tokens": segment.input_tokens,
                "cached_input_tokens": segment.cached_input_tokens,
                "output_tokens": segment.output_tokens,
                "write_paths": segment.write_paths,
                "git_branch": None,
            })
    return rows

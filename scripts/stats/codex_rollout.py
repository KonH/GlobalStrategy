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
#
# codex-issue/SKILL.md batch runs (scripts/automation/codex/handle_issues.py) are one
# CLI invocation that may internally progress through /specify, /plan, or /implement
# with no per-stage marker in the outer prompt - there is nothing finer to segment on,
# so the whole rollout is treated as a single "implement"-labeled segment here. This
# placeholder stage is never what actually reaches usage.csv for these runs: the
# wrapper always calls record_usage_row_codex() with the real stage it observed via the
# item's live ai-specify/ai-plan/ai-implement label, which overrides it. The important
# part is that a row exists here at all - previously this entry mapped to `None` and
# the whole session was silently dropped (see scripts/automation/common/issue_handler.py's
# determine_usage_stage_and_spec_dir(), which the wrapper calls before recording).
STAGE_MATCH_TABLE = [
    ("create-prd.md", "implement"),
    ("complete-prd.md", "implement"),
    ("follow these iteration instructions exactly", "implement"),
    ("codex-issue/SKILL.md", "implement"),
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
    match this repo. handle_issues.py-driven rollouts process one candidate per CLI
    invocation that may internally progress through multiple stages with no per-stage
    marker in the outer prompt, so this parser returns exactly one placeholder-staged
    segment for the whole session (see the STAGE_MATCH_TABLE comment above) - the
    wrapper's own record_usage_row_codex() call overrides that placeholder with the
    real stage it read from the item's live ai-specify/ai-plan/ai-implement label.
    """
    path = Path(path)
    lines = path.read_text(encoding="utf-8").splitlines()

    session_id = None
    cwd = None
    thread_source = None
    model = None
    effort = None
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
                thread_settings = payload.get("thread_settings") or {}
                model = thread_settings.get("model") or model
                effort = thread_settings.get("reasoning_effort") or effort
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
                    "effort": effort,
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
                "effort": segment.effort,
                "input_tokens": segment.input_tokens,
                "cached_input_tokens": segment.cached_input_tokens,
                "output_tokens": segment.output_tokens,
                "write_paths": segment.write_paths,
                "git_branch": None,
            })
    return rows

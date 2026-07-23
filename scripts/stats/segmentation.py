"""Shared segment/sub-stage shapes and splitting logic used by both the Claude Code
transcript parser and the Codex rollout parser.

A "stage" (spec/plan/implement) starts at a structural marker each provider recognizes
in its own way (a <command-name> line for Claude, a matched wrapper prompt for Codex).
Within a stage, everything up to and including the first completed (non-tool-use)
assistant response belongs to the base stage segment. Every subsequent real human turn
starts a new "<stage>_user_input_N" sub-stage segment - this part of the rule is
identical for both providers, so it lives here once.
"""

from dataclasses import dataclass, field
from typing import List, Optional


@dataclass
class Segment:
    stage: str
    context: str  # "fresh" | "continued"
    start: str
    end: str
    model: str = ""
    effort: str = ""
    input_tokens: int = 0
    cached_input_tokens: int = 0
    output_tokens: int = 0
    write_paths: List[str] = field(default_factory=list)
    git_branch: Optional[str] = None


def stage_name(base_stage, sub_index):
    return base_stage if sub_index is None else f"{base_stage}_user_input_{sub_index}"


def split_into_substages(base_stage, context, turns):
    """turns: ordered list of dicts, each optionally carrying:
        - is_human_turn: bool - a genuine human turn (not a tool_result, not a marker)
        - is_completed_response: bool - a non-tool-use assistant completion
        - timestamp: str
        - model: str | None
        - effort: str | None
        - usage: {"input_tokens", "cached_input_tokens", "output_tokens"} | None
        - write_paths: list[str]
        - git_branch: str | None

    Returns a list of Segment, one per bucket that received at least one turn.
    """
    buckets = [[]]
    seen_completed_response = False
    for turn in turns:
        if seen_completed_response and turn.get("is_human_turn"):
            buckets.append([])
        buckets[-1].append(turn)
        if turn.get("is_completed_response"):
            seen_completed_response = True

    segments = []
    for sub_index, bucket in enumerate(buckets):
        if not bucket:
            continue
        timestamps = [t["timestamp"] for t in bucket if t.get("timestamp")]
        models = [t["model"] for t in bucket if t.get("model")]
        efforts = [t["effort"] for t in bucket if t.get("effort")]
        write_paths = [p for t in bucket for p in t.get("write_paths", [])]
        branches = [t["git_branch"] for t in bucket if t.get("git_branch")]

        input_tokens = cached_tokens = output_tokens = 0
        for t in bucket:
            usage = t.get("usage")
            if usage:
                input_tokens += usage.get("input_tokens", 0)
                cached_tokens += usage.get("cached_input_tokens", 0)
                output_tokens += usage.get("output_tokens", 0)

        segments.append(Segment(
            stage=stage_name(base_stage, None if sub_index == 0 else sub_index),
            context=context,
            start=timestamps[0] if timestamps else "",
            end=timestamps[-1] if timestamps else "",
            model=models[0] if models else "",
            effort=efforts[0] if efforts else "",
            input_tokens=input_tokens,
            cached_input_tokens=cached_tokens,
            output_tokens=output_tokens,
            write_paths=write_paths,
            git_branch=branches[-1] if branches else None,
        ))
    return segments

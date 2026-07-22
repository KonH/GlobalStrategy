#!/usr/bin/env python3
"""Collects per-spec development statistics (LLM token usage, duration, cost, output
sizes) from Claude Code transcripts and Codex rollouts into Docs/Specs/<dir>/usage.csv.

Pure Python stdlib only (see Docs/Specs/26_07_22_17_spec-dev-stats/plan.md section 1) -
this module must be invokable as a bare `python`/`python3` call with no third-party
dependencies, since the SessionEnd hook runs in whatever environment the editor
process has, not a shell the user set up by hand.

Usage:
  python scripts/stats/collect_usage.py --scan
  python scripts/stats/collect_usage.py --hook
  python scripts/stats/collect_usage.py --record --provider claude --stage implement \\
      --spec-dir <dir> --mode automated --session-id <id> --model <model> \\
      --start <iso> --end <iso> --input-tokens N --cached-input-tokens N --output-tokens N
  python scripts/stats/collect_usage.py --record --provider codex --stage implement \\
      --spec-dir <dir> --mode automated --scan-latest-rollout-since <iso>
"""

import argparse
import json
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent.parent))
from scripts.stats.attribution import attribute_segment  # noqa: E402
from scripts.stats.claude_transcript import parse_claude_transcript  # noqa: E402
from scripts.stats.codex_rollout import parse_codex_rollout  # noqa: E402
from scripts.stats.csv_store import upsert_row  # noqa: E402
from scripts.stats.pricing import compute_cost  # noqa: E402
from scripts.stats.version_info import read_bundle_version  # noqa: E402
from scripts.stats.watermark import advance_watermark, get_last_scanned  # noqa: E402

REPO_DIR_NAME = "GlobalStrategy"


def repo_root():
    return Path.cwd()


def project_slug(root):
    return str(Path(root).resolve()).replace(":", "-").replace("\\", "-").replace("/", "-")


def find_claude_transcripts(root, since_ts):
    project_dir = Path.home() / ".claude" / "projects" / project_slug(root)
    if not project_dir.is_dir():
        return []
    paths = sorted(project_dir.glob("*.jsonl"))
    if since_ts is None:
        return paths
    return [p for p in paths if p.stat().st_mtime > since_ts]


def find_codex_rollouts(since_ts):
    sessions_dir = Path.home() / ".codex" / "sessions"
    if not sessions_dir.is_dir():
        return []
    paths = sorted(sessions_dir.glob("*/*/*/rollout-*.jsonl"))
    if since_ts is None:
        return paths
    return [p for p in paths if p.stat().st_mtime > since_ts]


def file_size_kb(path):
    path = Path(path)
    if not path.exists():
        return ""
    return round(path.stat().st_size / 1024, 2)


def diff_lines_for_branch(branch, root):
    try:
        merge_base = subprocess.run(
            ["git", "merge-base", "main", branch], cwd=root, capture_output=True, text=True,
        )
        if merge_base.returncode != 0:
            return ""
        base = merge_base.stdout.strip()
        result = subprocess.run(
            ["git", "diff", "--shortstat", base, branch], cwd=root, capture_output=True, text=True,
        )
        if result.returncode != 0 or not result.stdout.strip():
            return 0
        total = 0
        for part in result.stdout.split(","):
            part = part.strip()
            if "insertion" in part or "deletion" in part:
                total += int(part.split()[0])
        return total
    except OSError:
        return ""


def build_row(spec_dir, stage, mode, context, provider, session_id, model, start, end,
              input_tokens, cached_input_tokens, output_tokens, diff_lines, root):
    cost, warning = compute_cost(model, input_tokens, cached_input_tokens, output_tokens)
    if warning:
        print(f"warning: {warning}", file=sys.stderr)

    full_spec_dir = Path(root) / "Docs" / "Specs" / spec_dir
    return {
        "spec_id": spec_dir,
        "version": read_bundle_version(Path(root) / "ProjectSettings" / "ProjectSettings.asset"),
        "stage": stage,
        "mode": mode,
        "context": context,
        "start": start,
        "end": end,
        "provider": provider,
        "model": model,
        "cost_usd": "" if cost is None else cost,
        "input_tokens": input_tokens,
        "cached_input_tokens": cached_input_tokens,
        "output_tokens": output_tokens,
        "spec_size_kb": file_size_kb(full_spec_dir / "spec.md"),
        "plan_size_kb": file_size_kb(full_spec_dir / "plan.md"),
        "diff_lines": diff_lines,
        "session_id": session_id,
    }


def process_rows(rows, mode, root):
    for row in rows:
        try:
            spec_dir = attribute_segment(row["write_paths"], row["git_branch"], root)
            if spec_dir is None:
                continue
            record = build_row(
                spec_dir=spec_dir, stage=row["stage"], mode=mode, context=row["context"],
                provider=row["provider"], session_id=row["session_id"], model=row["model"],
                start=row["start"], end=row["end"], input_tokens=row["input_tokens"],
                cached_input_tokens=row["cached_input_tokens"], output_tokens=row["output_tokens"],
                diff_lines=diff_lines_for_branch(row["git_branch"], root) if row["git_branch"] else "",
                root=root,
            )
            upsert_row(Path(root) / "Docs" / "Specs" / spec_dir, record)
        except Exception as error:
            # One bad row (a malformed pricing table, a missing bundleVersion) must
            # not drop every other already-attributed row in this batch.
            print(f"warning: failed to record usage row (session {row.get('session_id')}, "
                  f"stage {row.get('stage')}): {error} - skipping this row", file=sys.stderr)


def record_usage_row(provider, stage, spec_dir, mode, session_id, model, start, end,
                      input_tokens, cached_input_tokens, output_tokens, diff_lines=None,
                      context="fresh", root=None):
    """Direct-record path used by the automation wrappers - no transcript re-parse,
    every field comes from data the wrapper already has (e.g. `claude -p
    --output-format json`'s own result object)."""
    root = root or repo_root()
    record = build_row(
        spec_dir=spec_dir, stage=stage, mode=mode, context=context, provider=provider,
        session_id=session_id, model=model, start=start, end=end,
        input_tokens=input_tokens, cached_input_tokens=cached_input_tokens,
        output_tokens=output_tokens, diff_lines="" if diff_lines is None else diff_lines,
        root=root,
    )
    upsert_row(Path(root) / "Docs" / "Specs" / spec_dir, record)


def record_usage_row_codex(spec_dir, stage, mode, since_iso, diff_lines=None, root=None):
    """Codex form of record_usage_row: `codex exec --json`'s own stdout has no single
    clean summary object analogous to Claude's `result` JSON, so this locates the
    newest rollout file with session_meta.timestamp >= since_iso and cwd matching this
    repo, parses it with the normal Codex segmenter, and overrides its auto-detected
    stage/mode with the wrapper-supplied values (the wrapper already knows unambiguously
    which phase this was)."""
    root = root or repo_root()
    since_ts = datetime.fromisoformat(since_iso.replace("Z", "+00:00")).timestamp()
    # Newest first: find_codex_rollouts() spans every Codex project on the machine, not
    # just this repo, so the single newest file by mtime may belong to an unrelated
    # project (parse_codex_rollout's own cwd filter would then correctly return no
    # rows for it) - walk newest-to-oldest until one actually matches this repo.
    candidates = sorted(find_codex_rollouts(since_ts - 1), key=lambda p: p.stat().st_mtime, reverse=True)
    rows = []
    for candidate in candidates:
        rows = parse_codex_rollout(candidate)
        if rows:
            break
    if not rows:
        print("warning: no matching Codex rollout for this repo found since the given "
              "timestamp - skipping usage row", file=sys.stderr)
        return

    aggregate_input = sum(r["input_tokens"] for r in rows)
    aggregate_cached = sum(r["cached_input_tokens"] for r in rows)
    aggregate_output = sum(r["output_tokens"] for r in rows)
    record = build_row(
        spec_dir=spec_dir, stage=stage, mode=mode, context=rows[0]["context"], provider="codex",
        session_id=rows[0]["session_id"], model=rows[0]["model"], start=rows[0]["start"],
        end=rows[-1]["end"], input_tokens=aggregate_input, cached_input_tokens=aggregate_cached,
        output_tokens=aggregate_output, diff_lines="" if diff_lines is None else diff_lines,
        root=root,
    )
    upsert_row(Path(root) / "Docs" / "Specs" / spec_dir, record)


def run_scan(root):
    claude_since = get_last_scanned("claude")
    codex_since = get_last_scanned("codex")

    claude_rows = []
    for transcript in find_claude_transcripts(root, claude_since):
        try:
            claude_rows.extend(parse_claude_transcript(transcript))
        except (OSError, UnicodeDecodeError) as error:
            # A corrupt/partially-written transcript must not block the watermark
            # advance for every other, perfectly-parseable file in this scan.
            print(f"warning: failed to parse {transcript}: {error} - skipping this file", file=sys.stderr)
    process_rows(claude_rows, mode="interactive", root=root)
    advance_watermark("claude")

    codex_rows = []
    for rollout in find_codex_rollouts(codex_since):
        try:
            codex_rows.extend(parse_codex_rollout(rollout))
        except (OSError, UnicodeDecodeError) as error:
            print(f"warning: failed to parse {rollout}: {error} - skipping this file", file=sys.stderr)
    process_rows(codex_rows, mode="interactive", root=root)
    advance_watermark("codex")


def run_hook(root):
    try:
        payload = json.loads(sys.stdin.read())
        transcript_path = payload.get("transcript_path")
    except (json.JSONDecodeError, OSError) as error:
        print(f"warning: could not read SessionEnd hook payload: {error}", file=sys.stderr)
        return
    if not transcript_path or not Path(transcript_path).exists():
        print("warning: SessionEnd hook payload has no usable transcript_path - "
              "the catch-all scan will pick this session up later", file=sys.stderr)
        return
    try:
        rows = parse_claude_transcript(transcript_path)
        process_rows(rows, mode="interactive", root=root)
    except Exception as error:
        # A hook failure must never surface as an error to the user - the catch-all
        # scan will pick this session up later regardless.
        print(f"warning: failed to process transcript {transcript_path}: {error} - "
              "the catch-all scan will pick this session up later", file=sys.stderr)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--scan", action="store_true")
    parser.add_argument("--hook", action="store_true")
    parser.add_argument("--record", action="store_true")
    parser.add_argument("--provider", choices=["claude", "codex"])
    parser.add_argument("--stage")
    parser.add_argument("--spec-dir")
    parser.add_argument("--mode", choices=["automated", "interactive"])
    parser.add_argument("--context", choices=["fresh", "continued"], default="fresh")
    parser.add_argument("--session-id")
    parser.add_argument("--model")
    parser.add_argument("--start")
    parser.add_argument("--end")
    parser.add_argument("--input-tokens", type=int, default=0)
    parser.add_argument("--cached-input-tokens", type=int, default=0)
    parser.add_argument("--output-tokens", type=int, default=0)
    parser.add_argument("--diff-lines", type=int, default=None)
    parser.add_argument("--scan-latest-rollout-since")
    args = parser.parse_args()

    root = repo_root()

    if args.scan:
        run_scan(root)
    elif args.hook:
        run_hook(root)
    elif args.record:
        if args.provider == "codex":
            record_usage_row_codex(
                spec_dir=args.spec_dir, stage=args.stage, mode=args.mode,
                since_iso=args.scan_latest_rollout_since, diff_lines=args.diff_lines, root=root,
            )
        else:
            record_usage_row(
                provider="claude", stage=args.stage, spec_dir=args.spec_dir, mode=args.mode,
                session_id=args.session_id, model=args.model, start=args.start, end=args.end,
                input_tokens=args.input_tokens, cached_input_tokens=args.cached_input_tokens,
                output_tokens=args.output_tokens, diff_lines=args.diff_lines, context=args.context,
                root=root,
            )
    else:
        parser.print_help()


if __name__ == "__main__":
    main()

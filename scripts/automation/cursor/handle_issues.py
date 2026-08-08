#!/usr/bin/env python3
"""Run configured-contributor-authored `cursor`-labeled GitHub issues and PRs through Cursor CLI.

Run this from an isolated scheduled-task/cron clone, never the primary working copy: before
each candidate it force-resets the relevant branch and removes untracked files. The shared
label workflow, locking, claim, clear, and candidate discovery live in `common.issue_handler`.

Cross-instance claim: each candidate is claimed via `claim_candidate` (sets `ai-in-progress`)
before checkout/CLI. After the CLI returns - including non-zero `SystemExit` - this wrapper
clears that item's `ai-in-progress` in a `finally` block. Hard-kill before that clear can leave
the label stuck; recovery is manual owner removal (no automatic reclaim).

Agents may also set informational stage labels (`ai-specify` / `ai-plan` / `ai-implement`)
while running those commands; those are not discovery status labels.
"""

import argparse
import json
import logging
import os
import re
import shutil
import subprocess
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from common.issue_handler import (  # noqa: E402
	acquire_lock, candidate_branch, checkout_clean, claim_candidate, clear_in_progress,
	determine_usage_stage_and_spec_dir, find_candidates, limit_active, save_limit_retry_at,
	setup_logging,
)
sys.path.insert(0, str(Path(__file__).resolve().parent.parent.parent.parent))
from scripts.stats.collect_usage import diff_lines_for_branch, record_usage_row  # noqa: E402

MODEL = "cursor-grok-4.5-high"
LABEL = "cursor"
MARKER = "<!-- cursor-automation -->"
DEFAULT_SANDBOX = "disabled"
ROOT = Path(__file__).resolve().parents[3]
DEFAULT_LOG_FILE = ROOT / "Logs" / "handle_issues_cursor.log"
DEFAULT_LOCK_FILE = ROOT / "Logs" / "handle_issues_cursor.lock"
DEFAULT_PROVIDER_STATE_FILE = ROOT / "Logs" / "auto_ai_provider_state.json"
# Cursor CLI/agent production limit text (forum + CLI reports), e.g.
# "Error: You've hit your usage limit … Your usage limits will reset when your monthly
# cycle ends on 8/14/2025." Free-tier variant uses "free requests limit" with the same
# cycle-ends suffix. Detection looks ONLY at error output (non-JSON lines and an
# is_error/error-subtype result), never at assistant/tool text. Date has no timezone;
# treat as UTC midnight of that day, rolling to the next day if already past (cycle end
# on "today" often clears later that day / next day).
LIMIT_TEXT_RE = re.compile(
	r"(usage limit|rate limit|requests limit|too many requests|resource_exhausted|"
	r"quota exceeded|spend limit)",
	re.IGNORECASE,
)
LIMIT_CYCLE_ENDS_RE = re.compile(
	r"monthly cycle ends on\s+(\d{1,2})/(\d{1,2})/(\d{4})",
	re.IGNORECASE,
)

logger = logging.getLogger("handle_issues_cursor")


def parse_cycle_ends_on(text):
	"""Parse `monthly cycle ends on 8/14/2025` into aware-UTC midnight, or None.

	If that midnight is already past, returns the next day's midnight so a same-day
	cycle-end date keeps the pause active until the following UTC day.
	"""
	match = LIMIT_CYCLE_ENDS_RE.search(text)
	if not match:
		return None
	month, day, year = (int(match.group(1)), int(match.group(2)), int(match.group(3)))
	try:
		candidate = datetime(year, month, day, tzinfo=timezone.utc)
	except ValueError:
		return None
	now = datetime.now(timezone.utc)
	if candidate <= now:
		candidate += timedelta(days=1)
	return candidate


def detect_session_limit(error_texts):
	"""Returns (limit_hit, retry_at). retry_at is a parseable cycle-end date (aware UTC)
	when present, else None (caller falls back to a fixed backoff)."""
	joined = "\n".join(error_texts)
	if not LIMIT_TEXT_RE.search(joined):
		return False, None
	return True, parse_cycle_ends_on(joined)


def build_environment():
	environment = os.environ.copy()
	token = environment.get("GH_ACCESS_TOKEN") or environment.get("GH_TOKEN") or environment.get("GITHUB_TOKEN")
	if token:
		environment["GH_TOKEN"] = token
	environment["GIT_TERMINAL_PROMPT"] = "0"
	return environment


def build_prompt(candidate):
	if candidate["kind"] == "pr":
		header = f"[PR #{candidate['number']}] {candidate['url']} (head branch: {candidate['headRefName']})"
		checkout = f"the PR head branch '{candidate['headRefName']}'"
	else:
		header = f"[ISSUE #{candidate['number']}] {candidate['url']}"
		checkout = "main"
	return (
		"Read and follow .cursor/commands/cursor-issue.md.\n\n"
		"Process only this configured-contributor-authored `cursor`-labeled GitHub item. The working tree is "
		f"a clean, current checkout of {checkout}; its listing body may be stale, so re-read "
		"the live description and comments yourself.\n\n"
		f"{header}\n{candidate['title']}\n\n{candidate['body'] or '(empty body)'}"
	)


def summarize_event(event):
	event_type = event.get("type")
	if event_type == "system":
		return f"system: subtype={event.get('subtype')} model={event.get('model')}"
	if event_type == "result":
		return f"result: subtype={event.get('subtype')} success={not event.get('is_error')}"
	if event_type in ("assistant", "user"):
		parts = [block["text"].strip() for block in event.get("message", {}).get("content", [])
				 if block.get("type") == "text" and block.get("text", "").strip()]
		return f"{event_type}: {' | '.join(parts)}" if parts else None
	return None


def run_cursor(prompt, args):
	command = [
		shutil.which("agent") or "agent", "-p", prompt, "--model", args.model,
		"--output-format", "stream-json", "--force", "--trust", "--sandbox", args.sandbox,
	]
	process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
		text=True, encoding="utf-8", errors="replace", bufsize=1, env=build_environment())
	errors = []
	result = None
	for line in process.stdout:
		line = line.rstrip()
		if not line:
			continue
		try:
			event = json.loads(line)
		except json.JSONDecodeError:
			errors.append(line)
			logger.info("[agent -p] %s", line)
			continue
		if event.get("type") == "result":
			result = event
		summary = summarize_event(event)
		if summary:
			logger.info("[agent -p] %s", summary)
	process.wait()
	if result and (result.get("is_error") or str(result.get("subtype", "")).startswith("error")):
		errors.append(str(result.get("result", "")))
	logger.info("agent -p exited with code %s.", process.returncode)
	return process.returncode, errors, result


def current_branch():
	"""The branch actually checked out right now - unlike `candidate_branch()`, which is only
	the branch a run *started* from (always `main` for an issue candidate; the agent creates/
	switches to its own `feature/<name>` branch during the run per `.cursor/commands/cursor-
	issue.md`). Returns None on failure rather than raising - usage-stats attribution must
	never abort the automation run."""
	result = subprocess.run(["git", "rev-parse", "--abbrev-ref", "HEAD"],
		capture_output=True, text=True)
	return result.stdout.strip() if result.returncode == 0 else None


def record_cursor_usage_stats(candidate, since_iso, result, requested_model):
	"""Best-effort post-run usage.csv row for one candidate. Silently records nothing when the
	run's diff touched no Docs/Specs/<dir>/ path (most cursor-labeled issues are not spec
	work) - see determine_usage_stage_and_spec_dir() for the attribution rules. Any failure
	here is caught and logged, never raised, so a usage-stats hiccup can never fail the
	candidate's own outcome.

	`result` is the CLI's own final `type: "result"` stream-json event (same shape `claude -p
	--output-format json` returns, since Cursor's CLI mirrors it) - `usage`/`session_id`/
	`model` come from there, defensively via `.get()` since that shape isn't contractually
	documented."""
	try:
		branch = current_branch()
		if not branch:
			logger.warning("Usage-stats: could not resolve the current branch after the run "
				"for %s #%s - skipping.", candidate["kind"], candidate["number"])
			return
		spec_dir, stage = determine_usage_stage_and_spec_dir(
			logger, candidate["number"], branch, Path.cwd())
		if spec_dir is None:
			return
		usage = (result or {}).get("usage") or {}
		record_usage_row(
			provider="cursor", stage=stage, spec_dir=spec_dir, mode="automated",
			session_id=(result or {}).get("session_id", ""),
			model=(result or {}).get("model") or requested_model,
			start=since_iso, end=datetime.now(timezone.utc).isoformat(),
			input_tokens=usage.get("input_tokens", 0),
			cached_input_tokens=usage.get("cache_read_input_tokens", 0),
			output_tokens=usage.get("output_tokens", 0),
			diff_lines=diff_lines_for_branch(branch, Path.cwd()),
		)
		logger.info("Usage-stats: recorded cursor '%s' row for spec '%s' (%s #%s).",
			stage, spec_dir, candidate["kind"], candidate["number"])
	except Exception as exc:
		logger.warning("Usage-stats: failed to record cursor usage row for %s #%s: %s",
			candidate["kind"], candidate["number"], exc)


def process_claimed_candidate(candidate, args):
	"""Checkout + CLI + limit handling for one already-claimed candidate.

	Raises SystemExit on non-zero CLI return so callers can wrap this in try/finally and
	still clear `ai-in-progress` before the exit propagates (SystemExit is a BaseException).
	"""
	checkout_clean(logger, candidate_branch(candidate))
	iteration_start = datetime.now(timezone.utc).isoformat()
	returncode, errors, result = run_cursor(build_prompt(candidate), args)
	limit_hit, retry_at = detect_session_limit(errors)
	if limit_hit:
		if retry_at is None:
			retry_at = datetime.now(timezone.utc) + timedelta(minutes=args.limit_backoff_minutes)
		save_limit_retry_at(args.provider_state_file, LABEL, retry_at)
		clear_in_progress(logger, candidate["number"])
		logger.warning("Cursor usage limit hit; pausing until %s.", retry_at.isoformat())
		sys.exit(0)
	if returncode:
		raise SystemExit(returncode)
	record_cursor_usage_stats(candidate, iteration_start, result, args.model)


def main():
	parser = argparse.ArgumentParser(description=__doc__)
	parser.add_argument("--model", default=MODEL)
	parser.add_argument("--sandbox", default=DEFAULT_SANDBOX, choices=["disabled", "enabled"])
	parser.add_argument("--log-file", type=Path, default=DEFAULT_LOG_FILE)
	parser.add_argument("--log-max-bytes", type=int, default=5 * 1024 * 1024)
	parser.add_argument("--log-backup-count", type=int, default=5)
	parser.add_argument("--lock-file", type=Path, default=DEFAULT_LOCK_FILE)
	parser.add_argument("--provider-state-file", type=Path, default=DEFAULT_PROVIDER_STATE_FILE)
	parser.add_argument("--limit-backoff-minutes", type=int, default=60,
						help="How long to pause after a usage-limit hit when the error text "
							 "has no parseable `monthly cycle ends on M/D/YYYY`.")
	args = parser.parse_args()
	setup_logging(logger, args.log_file, args.log_max_bytes, args.log_backup_count)
	lock = acquire_lock(logger, args.lock_file)
	if lock is None or limit_active(logger, args.provider_state_file, LABEL):
		return
	candidates = find_candidates(LABEL)
	if not candidates:
		logger.info("No cursor candidates found.")
		return
	for candidate in candidates:
		if not claim_candidate(logger, LABEL, MARKER, candidate):
			logger.info("Lost claim race for %s #%s - skipping.", candidate["kind"], candidate["number"])
			continue
		try:
			process_claimed_candidate(candidate, args)
		finally:
			clear_in_progress(logger, candidate["number"])


if __name__ == "__main__":
	try:
		main()
	except Exception:
		logger.exception("Cursor issue runner failed")
		raise

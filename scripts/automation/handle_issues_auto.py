#!/usr/bin/env python3
"""Route unlabeled, configured-contributor-authored ``auto-ai`` GitHub items.

Routing is deliberately completed before any provider runner starts. Each routed item keeps
``auto-ai`` and receives exactly one provider label; then Claude, Codex, and Cursor each run
their normal independent handler once.
"""

import argparse
import logging
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))

from scripts.automation.common.issue_handler import (
    AI_NEED_ATTENTION, AI_STATUS_LABELS, acquire_lock, add_label, label_names,
    list_labeled_items, post_comment, record_auto_selection, select_auto_provider,
    setup_logging, verify_label_present,
)

PROVIDERS = ("claude", "codex", "cursor")
MARKER = "<!-- auto-ai-automation -->"
DEFAULT_LOG_FILE = ROOT / "Logs" / "handle_issues_auto.log"
DEFAULT_LOCK_FILE = ROOT / "Logs" / "handle_issues_auto.lock"
DEFAULT_PROVIDER_STATE_FILE = ROOT / "Logs" / "auto_ai_provider_state.json"
logger = logging.getLogger("handle_issues_auto")


def has_provider_label(candidate):
    labels = label_names(candidate)
    return any(provider in labels for provider in PROVIDERS)


def auto_candidates():
    """Open trusted ``auto-ai`` items not already claimed by a provider workflow."""
    return [
        candidate for candidate in list_labeled_items("auto-ai")
        if not has_provider_label(candidate) and not (label_names(candidate) & AI_STATUS_LABELS)
    ]


def park_unroutable(logger, candidate):
    """Park an auto-ai item when every provider is usage/session-limited.

    Applies ``ai-need-attention`` first so discovery skips it on the next tick, then posts a
    best-effort owner note. Keeps ``auto-ai`` so removing the status label resumes routing.
    """
    number = candidate["number"]
    kind = candidate.get("kind", "item")
    add_label(number, AI_NEED_ATTENTION)
    note = (
        f"{MARKER}\nAll AI providers ({', '.join(PROVIDERS)}) currently have an active "
        f"usage/session limit, so this `auto-ai` {kind} could not be routed. Applied "
        f"`{AI_NEED_ATTENTION}`. Remove that label once a provider is available again to "
        "resume auto-routing."
    )
    try:
        post_comment(number, note)
    except Exception as exc:
        logger.warning("Failed to post all-providers-limited note on %s #%s: %s",
                       kind, number, exc)
    logger.warning("All providers are limited; parked %s #%s with %s.",
                   kind, number, AI_NEED_ATTENTION)


def route_candidates(logger, state_file, candidates):
    routed = []
    for candidate in candidates:
        provider = select_auto_provider(logger, state_file, PROVIDERS)
        if provider is None:
            park_unroutable(logger, candidate)
            continue
        add_label(candidate["number"], provider)
        # Persist before evaluating the next candidate: a batch is sequential LRU, not a
        # snapshot of one initial ordering.
        record_auto_selection(state_file, provider)
        verify_label_present(logger, candidate["number"], provider)
        routed.append((candidate["number"], provider))
        logger.info("Routed %s #%s to %s.", candidate.get("kind", "item"), candidate["number"], provider)
    return routed


def run_provider_handlers():
    exit_code = 0
    for provider in PROVIDERS:
        runner = ROOT / "scripts" / "automation" / provider / "handle_issues.py"
        result = subprocess.run([sys.executable, str(runner)], check=False)
        if result.returncode:
            logger.error("%s handler exited with %s.", provider, result.returncode)
            exit_code = result.returncode
    return exit_code


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--log-file", type=Path, default=DEFAULT_LOG_FILE)
    parser.add_argument("--log-max-bytes", type=int, default=5 * 1024 * 1024)
    parser.add_argument("--log-backup-count", type=int, default=5)
    parser.add_argument("--lock-file", type=Path, default=DEFAULT_LOCK_FILE)
    parser.add_argument("--provider-state-file", type=Path, default=DEFAULT_PROVIDER_STATE_FILE)
    args = parser.parse_args()
    setup_logging(logger, args.log_file, args.log_max_bytes, args.log_backup_count)
    lock = acquire_lock(logger, args.lock_file)
    if lock is None:
        logger.info("Another auto-router instance is already running - exiting.")
        return 0
    candidates = auto_candidates()
    if candidates:
        route_candidates(logger, args.provider_state_file, candidates)
    else:
        logger.info("No unlabeled auto-ai items found.")
    return run_provider_handlers()


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception:
        logger.exception("Auto issue router failed")
        raise

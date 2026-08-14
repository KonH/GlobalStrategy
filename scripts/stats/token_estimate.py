"""Rough token-count estimate for spec.md/plan.md snapshots.

Stdlib-only, same constraint as the rest of scripts/stats (see plan.md's
"Stdlib-only constraint" in Docs/Specs/26_07_22_17_spec-dev-stats/plan.md
section 1) - the SessionEnd hook must run standalone with no network access
or API key. Anthropic's Messages API has a free `count_tokens` endpoint that
would give an exact count for real text, but calling it here would make every
row write depend on network + credentials, so this module uses the standard
~4-characters-per-token heuristic for English/markdown/code text instead.

This is an estimate for cross-spec size comparison, not a billing-accurate
count - it has no relation to input_tokens/cached_input_tokens/output_tokens,
which remain real API-reported figures when available.
"""

from pathlib import Path

CHARS_PER_TOKEN = 4


def estimate_tokens(text):
    if not text:
        return 0
    return round(len(text) / CHARS_PER_TOKEN)


def file_tokens(path):
    """Token estimate for a file's current content, or "" if it doesn't exist -
    same "empty cell means missing file" convention as file_size_kb."""
    path = Path(path)
    if not path.exists():
        return ""
    text = path.read_text(encoding="utf-8", errors="replace")
    return estimate_tokens(text)

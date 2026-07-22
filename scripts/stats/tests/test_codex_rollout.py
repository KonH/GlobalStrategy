import json
import tempfile
import unittest
from pathlib import Path

from scripts.stats.codex_rollout import parse_codex_rollout


def session_meta(session_id, cwd, thread_source="user"):
    return {
        "type": "session_meta",
        "timestamp": "2026-01-01T00:00:00Z",
        "payload": {"id": session_id, "cwd": cwd, "thread_source": thread_source},
    }


def user_message(text, timestamp="2026-01-01T00:00:01Z"):
    return {
        "type": "event_msg",
        "timestamp": timestamp,
        "payload": {"type": "user_message", "message": text},
    }


def agent_message(timestamp="2026-01-01T00:00:02Z"):
    return {
        "type": "event_msg",
        "timestamp": timestamp,
        "payload": {"type": "agent_message", "message": "done"},
    }


def token_count(input_tokens, cached, output_tokens, timestamp="2026-01-01T00:00:03Z"):
    return {
        "type": "event_msg",
        "timestamp": timestamp,
        "payload": {
            "type": "token_count",
            "info": {"total_token_usage": {
                "input_tokens": input_tokens,
                "cached_input_tokens": cached,
                "output_tokens": output_tokens,
            }},
        },
    }


def write_rollout(lines):
    tmp = tempfile.NamedTemporaryFile(mode="w", suffix=".jsonl", delete=False, encoding="utf-8")
    for line in lines:
        tmp.write(json.dumps(line) + "\n")
    tmp.close()
    return Path(tmp.name)


REPO_CWD = r"E:\Users\KonH\Git\GlobalStrategy"


class CodexRolloutTests(unittest.TestCase):
    def test_subagent_thread_source_is_excluded(self):
        path = write_rollout([
            session_meta("s1", REPO_CWD, thread_source="subagent"),
            user_message("Read and follow .claude/commands/create-prd.md as a Codex procedure."),
            agent_message(),
        ])

        rows = parse_codex_rollout(path)

        self.assertEqual([], rows)

    def test_cwd_mismatch_is_excluded(self):
        path = write_rollout([
            session_meta("s1", r"E:\Users\KonH\Git\SomeOtherRepo"),
            user_message("Read and follow .claude/commands/create-prd.md as a Codex procedure."),
            agent_message(),
        ])

        rows = parse_codex_rollout(path)

        self.assertEqual([], rows)

    def test_token_count_deltas_not_cumulative_totals(self):
        path = write_rollout([
            session_meta("s1", REPO_CWD),
            user_message("Read and follow .claude/commands/create-prd.md as a Codex procedure."),
            token_count(1000, 100, 50, timestamp="2026-01-01T00:00:02Z"),
            token_count(1800, 100, 90, timestamp="2026-01-01T00:00:03Z"),
        ])

        rows = parse_codex_rollout(path)

        self.assertEqual(1, len(rows))
        self.assertEqual(1800, rows[0]["input_tokens"])
        self.assertEqual(90, rows[0]["output_tokens"])

    def test_token_count_deltas_are_not_reset_at_stage_boundaries(self):
        # Regression guard: token_count is a rollout-wide cumulative total, not
        # per-stage - stage 2's delta must be computed against stage 1's last
        # cumulative total, not reset to zero at the stage boundary.
        path = write_rollout([
            session_meta("s1", REPO_CWD),
            user_message("Read and follow .claude/commands/create-prd.md as a Codex procedure.",
                         timestamp="2026-01-01T00:00:01Z"),
            token_count(1000, 0, 500, timestamp="2026-01-01T00:00:02Z"),
            user_message("/plan 26_01_01_00_example", timestamp="2026-01-01T00:00:03Z"),
            token_count(1600, 0, 800, timestamp="2026-01-01T00:00:04Z"),
        ])

        rows = parse_codex_rollout(path)

        implement_row = next(r for r in rows if r["stage"] == "implement")
        plan_row = next(r for r in rows if r["stage"] == "plan")
        self.assertEqual(1000, implement_row["input_tokens"])
        self.assertEqual(600, plan_row["input_tokens"])
        self.assertEqual(300, plan_row["output_tokens"])

    def test_stage_match_table_detects_wrapper_prompts(self):
        path = write_rollout([
            session_meta("s1", REPO_CWD),
            user_message("Read and follow .claude/commands/create-prd.md as a Codex procedure. "
                         "The dated spec identifier is 26_01_01_00_example."),
            agent_message(),
        ])

        rows = parse_codex_rollout(path)

        self.assertEqual(1, len(rows))
        self.assertEqual("implement", rows[0]["stage"])


if __name__ == "__main__":
    unittest.main()

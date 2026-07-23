import json
import tempfile
import unittest
from pathlib import Path

from scripts.stats.claude_transcript import parse_claude_transcript


def user_line(session_id, text, timestamp, branch="main"):
    return {
        "type": "user",
        "sessionId": session_id,
        "gitBranch": branch,
        "timestamp": timestamp,
        "message": {"role": "user", "content": text},
    }


def meta_line(session_id, text, timestamp, branch="main"):
    return {
        "type": "user",
        "sessionId": session_id,
        "gitBranch": branch,
        "timestamp": timestamp,
        "isMeta": True,
        "message": {"role": "user", "content": text},
    }


def tool_result_line(session_id, timestamp, branch="main"):
    return {
        "type": "user",
        "sessionId": session_id,
        "gitBranch": branch,
        "timestamp": timestamp,
        "message": {"role": "user", "content": [{"type": "tool_result", "content": "ok"}]},
    }


def assistant_line(session_id, timestamp, stop_reason, model="claude-sonnet-5",
                    input_tokens=10, cached=5, output_tokens=20, nested_session_id="other-session",
                    branch="main"):
    return {
        "type": "assistant",
        "sessionId": session_id,
        "session_id": nested_session_id,
        "gitBranch": branch,
        "timestamp": timestamp,
        "message": {
            "role": "assistant",
            "model": model,
            "stop_reason": stop_reason,
            "content": [{"type": "text", "text": "..."}],
            "usage": {
                "input_tokens": input_tokens,
                "cache_read_input_tokens": cached,
                "output_tokens": output_tokens,
            },
        },
    }


def skill_invocation_line(session_id, timestamp, skill, branch="main",
                          input_tokens=10, cached=5, output_tokens=20):
    line = assistant_line(session_id, timestamp, "tool_use", branch=branch,
                          input_tokens=input_tokens, cached=cached, output_tokens=output_tokens)
    line["message"]["content"] = [{"type": "tool_use", "name": "Skill", "input": {"skill": skill}}]
    return line


def write_transcript(lines):
    tmp = tempfile.NamedTemporaryFile(mode="w", suffix=".jsonl", delete=False, encoding="utf-8")
    for line in lines:
        tmp.write(json.dumps(line) + "\n")
    tmp.close()
    return Path(tmp.name)


class ClaudeTranscriptTests(unittest.TestCase):
    def test_command_name_marker_starts_new_stage(self):
        path = write_transcript([
            user_line("s1", "<command-name>/specify</command-name>", "2026-01-01T00:00:00Z"),
            assistant_line("s1", "2026-01-01T00:00:01Z", "end_turn"),
        ])

        rows = parse_claude_transcript(path)

        self.assertEqual(1, len(rows))
        self.assertEqual("spec", rows[0]["stage"])

    def test_stage_after_first_stage_in_session_is_continued(self):
        path = write_transcript([
            user_line("s1", "<command-name>/specify</command-name>", "2026-01-01T00:00:00Z"),
            assistant_line("s1", "2026-01-01T00:00:01Z", "end_turn"),
            user_line("s1", "<command-name>/plan</command-name>", "2026-01-01T00:00:02Z"),
            assistant_line("s1", "2026-01-01T00:00:03Z", "end_turn"),
        ])

        rows = parse_claude_transcript(path)

        spec_row = next(r for r in rows if r["stage"] == "spec")
        plan_row = next(r for r in rows if r["stage"] == "plan")
        self.assertEqual("fresh", spec_row["context"])
        self.assertEqual("continued", plan_row["context"])

    def test_user_turns_after_first_completed_response_become_user_input_substages(self):
        path = write_transcript([
            user_line("s1", "<command-name>/specify</command-name>", "2026-01-01T00:00:00Z"),
            assistant_line("s1", "2026-01-01T00:00:01Z", "end_turn"),
            user_line("s1", "actually also check X", "2026-01-01T00:00:02Z"),
            assistant_line("s1", "2026-01-01T00:00:03Z", "end_turn"),
            user_line("s1", "one more thing", "2026-01-01T00:00:04Z"),
            assistant_line("s1", "2026-01-01T00:00:05Z", "end_turn"),
        ])

        rows = parse_claude_transcript(path)
        stages = sorted(r["stage"] for r in rows)

        self.assertEqual(["spec", "spec_user_input_1", "spec_user_input_2"], stages)

    def test_meta_lines_are_not_treated_as_human_turns(self):
        path = write_transcript([
            user_line("s1", "<command-name>/specify</command-name>", "2026-01-01T00:00:00Z"),
            assistant_line("s1", "2026-01-01T00:00:01Z", "end_turn"),
            meta_line("s1", "<system-reminder>...</system-reminder>", "2026-01-01T00:00:02Z"),
            assistant_line("s1", "2026-01-01T00:00:03Z", "end_turn"),
        ])

        rows = parse_claude_transcript(path)

        self.assertEqual(1, len(rows))
        self.assertEqual("spec", rows[0]["stage"])

    def test_tool_result_lines_are_not_treated_as_human_turns(self):
        path = write_transcript([
            user_line("s1", "<command-name>/specify</command-name>", "2026-01-01T00:00:00Z"),
            assistant_line("s1", "2026-01-01T00:00:01Z", "tool_use"),
            tool_result_line("s1", "2026-01-01T00:00:02Z"),
            assistant_line("s1", "2026-01-01T00:00:03Z", "end_turn"),
        ])

        rows = parse_claude_transcript(path)

        self.assertEqual(1, len(rows))
        self.assertEqual("spec", rows[0]["stage"])

    def test_token_sums_are_exact_across_segment(self):
        path = write_transcript([
            user_line("s1", "<command-name>/specify</command-name>", "2026-01-01T00:00:00Z"),
            assistant_line("s1", "2026-01-01T00:00:01Z", "tool_use", input_tokens=10, cached=1, output_tokens=2),
            assistant_line("s1", "2026-01-01T00:00:02Z", "end_turn", input_tokens=5, cached=0, output_tokens=8),
        ])

        rows = parse_claude_transcript(path)

        self.assertEqual(1, len(rows))
        self.assertEqual(15, rows[0]["input_tokens"])
        self.assertEqual(1, rows[0]["cached_input_tokens"])
        self.assertEqual(10, rows[0]["output_tokens"])

    def test_skill_tool_invocation_starts_stage(self):
        path = write_transcript([
            user_line("s1", "write spec for my feature please", "2026-01-01T00:00:00Z"),
            skill_invocation_line("s1", "2026-01-01T00:00:01Z", "specify", input_tokens=7, cached=3, output_tokens=11),
            tool_result_line("s1", "2026-01-01T00:00:02Z"),
            assistant_line("s1", "2026-01-01T00:00:03Z", "end_turn", input_tokens=5, cached=0, output_tokens=8),
        ])

        rows = parse_claude_transcript(path)

        self.assertEqual(1, len(rows))
        self.assertEqual("spec", rows[0]["stage"])
        self.assertEqual(12, rows[0]["input_tokens"])
        self.assertEqual(19, rows[0]["output_tokens"])

    def test_plugin_prefixed_skill_invocation_maps_to_stage(self):
        path = write_transcript([
            skill_invocation_line("s1", "2026-01-01T00:00:00Z", "k:plan"),
            assistant_line("s1", "2026-01-01T00:00:01Z", "end_turn"),
        ])

        rows = parse_claude_transcript(path)

        self.assertEqual(1, len(rows))
        self.assertEqual("plan", rows[0]["stage"])

    def test_repeated_skill_invocation_of_current_stage_does_not_restart_it(self):
        path = write_transcript([
            skill_invocation_line("s1", "2026-01-01T00:00:00Z", "specify"),
            tool_result_line("s1", "2026-01-01T00:00:01Z"),
            skill_invocation_line("s1", "2026-01-01T00:00:02Z", "k:specify"),
            tool_result_line("s1", "2026-01-01T00:00:03Z"),
            assistant_line("s1", "2026-01-01T00:00:04Z", "end_turn"),
        ])

        rows = parse_claude_transcript(path)

        self.assertEqual(1, len(rows))
        self.assertEqual("spec", rows[0]["stage"])
        self.assertEqual("2026-01-01T00:00:00Z", rows[0]["start"])

    def test_skill_names_containing_stage_words_do_not_start_stages(self):
        path = write_transcript([
            skill_invocation_line("s1", "2026-01-01T00:00:00Z", "implement-bot-feature"),
            assistant_line("s1", "2026-01-01T00:00:01Z", "end_turn"),
        ])

        rows = parse_claude_transcript(path)

        self.assertEqual(0, len(rows))

    def test_outer_session_id_used_not_nested_message_session_id(self):
        path = write_transcript([
            user_line("outer-session", "<command-name>/specify</command-name>", "2026-01-01T00:00:00Z"),
            assistant_line("outer-session", "2026-01-01T00:00:01Z", "end_turn", nested_session_id="different-nested-id"),
        ])

        rows = parse_claude_transcript(path)

        self.assertEqual("outer-session", rows[0]["session_id"])


if __name__ == "__main__":
    unittest.main()

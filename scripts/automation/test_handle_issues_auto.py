import tempfile
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path
from unittest.mock import MagicMock, patch

from scripts.automation.common.issue_handler import (
    AUTO_MARKER, park_auto_item_unroutable, record_auto_selection, save_limit_retry_at,
    select_auto_provider,
)
from scripts.automation.handle_issues_auto import auto_candidates, route_candidates, run_provider_handlers


def item(number, labels, **extra):
    return {"number": number, "labels": [{"name": label} for label in labels], **extra}


class ProviderSelectionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.state_file = Path(self.temp.name) / "provider-state.json"
        self.logger = MagicMock()
        self.providers = ("claude", "codex", "cursor")

    def test_never_selected_provider_is_preferred_then_persisted_for_next_choice(self):
        record_auto_selection(self.state_file, "claude", datetime.now(timezone.utc) - timedelta(hours=1))
        self.assertEqual("codex", select_auto_provider(self.logger, self.state_file, self.providers))
        record_auto_selection(self.state_file, "codex")
        self.assertEqual("cursor", select_auto_provider(self.logger, self.state_file, self.providers))

    def test_limited_provider_is_excluded(self):
        save_limit_retry_at(self.state_file, "claude", datetime.now(timezone.utc) + timedelta(hours=1))
        self.assertEqual("codex", select_auto_provider(self.logger, self.state_file, self.providers))

    def test_all_limited_providers_leave_item_unassigned(self):
        for provider in self.providers:
            save_limit_retry_at(self.state_file, provider, datetime.now(timezone.utc) + timedelta(hours=1))
        self.assertIsNone(select_auto_provider(self.logger, self.state_file, self.providers))


class RoutingTests(unittest.TestCase):
    def test_auto_candidates_exclude_provider_and_ai_status_labels(self):
        with patch("scripts.automation.handle_issues_auto.list_labeled_items", return_value=[
            item(1, ["auto-ai"]),
            item(2, ["auto-ai", "claude"]),
            item(3, ["auto-ai", "ai-in-progress"]),
            item(4, ["auto-ai", "bug"]),
            item(5, ["auto-ai", "ai-need-attention"]),
            item(6, ["auto-ai", "ai-complete"]),
        ]):
            self.assertEqual([1, 4], [candidate["number"] for candidate in auto_candidates()])

    def test_route_assigns_once_verifies_and_persists_before_next_candidate(self):
        candidates = [item(1, ["auto-ai"]), item(2, ["auto-ai"])]
        with patch("scripts.automation.handle_issues_auto.select_auto_provider",
                   side_effect=["claude", "codex"]), \
             patch("scripts.automation.handle_issues_auto.add_label") as add, \
             patch("scripts.automation.handle_issues_auto.record_auto_selection") as record, \
             patch("scripts.automation.handle_issues_auto.verify_label_present",
                   return_value=True) as verify:
            routed = route_candidates(MagicMock(), Path("state.json"), candidates)
        self.assertEqual([(1, "claude"), (2, "codex")], routed)
        self.assertEqual([(1, "claude"), (2, "codex")], [call.args for call in add.call_args_list])
        self.assertEqual(["claude", "codex"], [call.args[1] for call in record.call_args_list])
        self.assertEqual([(1, "claude"), (2, "codex")],
                         [(call.args[1], call.args[2]) for call in verify.call_args_list])

    def test_all_limited_parks_with_need_attention_and_comment(self):
        candidate = item(7, ["auto-ai"], kind="issue")
        with patch("scripts.automation.handle_issues_auto.select_auto_provider",
                   return_value=None), \
             patch("scripts.automation.handle_issues_auto.park_auto_item_unroutable") as park, \
             patch("scripts.automation.handle_issues_auto.record_auto_selection") as record:
            routed = route_candidates(MagicMock(), Path("state.json"), [candidate])
        self.assertEqual([], routed)
        park.assert_called_once()
        record.assert_not_called()

    def test_park_unroutable_comment_failure_still_applies_need_attention(self):
        candidate = item(8, ["auto-ai"], kind="pr")
        with patch("scripts.automation.common.issue_handler.add_label") as add, \
             patch("scripts.automation.common.issue_handler.post_comment",
                   side_effect=RuntimeError("gh failed")):
            park_auto_item_unroutable(MagicMock(), candidate)
        add.assert_called_once_with(8, "ai-need-attention")

    def test_park_unroutable_posts_auto_marker_note(self):
        candidate = item(9, ["auto-ai"], kind="issue")
        with patch("scripts.automation.common.issue_handler.add_label") as add, \
             patch("scripts.automation.common.issue_handler.post_comment") as post:
            park_auto_item_unroutable(MagicMock(), candidate)
        add.assert_called_once_with(9, "ai-need-attention")
        self.assertTrue(post.call_args.args[1].startswith(AUTO_MARKER))
        self.assertIn("ai-need-attention", post.call_args.args[1])
        self.assertIn("claude, codex, cursor", post.call_args.args[1])

    def test_provider_handlers_run_in_provider_order_after_routing(self):
        with patch("scripts.automation.handle_issues_auto.subprocess.run",
                   side_effect=[MagicMock(returncode=0), MagicMock(returncode=3), MagicMock(returncode=0)]) as run:
            self.assertEqual(3, run_provider_handlers())
        self.assertEqual(["claude", "codex", "cursor"],
                         [Path(call.args[0][1]).parent.name for call in run.call_args_list])


if __name__ == "__main__":
    unittest.main()

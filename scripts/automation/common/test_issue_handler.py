import unittest
from datetime import datetime, timezone
from unittest.mock import patch

from scripts.automation.common.issue_handler import find_candidates, has_new_owner_activity

CUTOFF = datetime(2026, 7, 23, 13, 0, 0, tzinfo=timezone.utc)
BEFORE = "2026-07-23T12:00:00Z"
AFTER = "2026-07-23T13:30:00Z"
MARKER = "<!-- claude-automation -->"
BOT_COMMENT_PREFIX = "<!-- claude-automation"
BOT_STATUS_LABELS = {"claude-in-progress", "claude-needs-attention"}


def labeled_event(name, created_at):
    return {"event": "labeled", "label": {"name": name}, "created_at": created_at}


def unlabeled_event(name, created_at):
    return {"event": "unlabeled", "label": {"name": name}, "created_at": created_at}


def commented_event(body, created_at):
    return {"event": "commented", "body": body, "created_at": created_at}


class HasNewOwnerActivityTests(unittest.TestCase):
    def test_bot_status_label_churn_is_not_new_activity(self):
        with patch("scripts.automation.common.issue_handler.run_gh_json", return_value=[
            labeled_event("claude-in-progress", AFTER),
            unlabeled_event("claude-in-progress", AFTER),
            labeled_event("claude-needs-attention", AFTER),
        ]):
            self.assertFalse(has_new_owner_activity(BOT_COMMENT_PREFIX, BOT_STATUS_LABELS, 1, CUTOFF))

    def test_bot_marker_comment_is_not_new_activity(self):
        with patch("scripts.automation.common.issue_handler.run_gh_json", return_value=[
            commented_event("<!-- claude-automation -->\n## Needs Manual Attention", AFTER),
            commented_event("<!-- claude-automation:checklist -->\n## Progress", AFTER),
        ]):
            self.assertFalse(has_new_owner_activity(BOT_COMMENT_PREFIX, BOT_STATUS_LABELS, 1, CUTOFF))

    def test_owner_comment_is_new_activity(self):
        with patch("scripts.automation.common.issue_handler.run_gh_json", return_value=[
            commented_event("Try again", AFTER),
        ]):
            self.assertTrue(has_new_owner_activity(BOT_COMMENT_PREFIX, BOT_STATUS_LABELS, 1, CUTOFF))

    def test_non_status_label_change_is_new_activity(self):
        with patch("scripts.automation.common.issue_handler.run_gh_json", return_value=[
            labeled_event("claude", AFTER),
        ]):
            self.assertTrue(has_new_owner_activity(BOT_COMMENT_PREFIX, BOT_STATUS_LABELS, 1, CUTOFF))

    def test_events_before_cutoff_are_ignored(self):
        with patch("scripts.automation.common.issue_handler.run_gh_json", return_value=[
            commented_event("Try again", BEFORE),
            labeled_event("claude", BEFORE),
        ]):
            self.assertFalse(has_new_owner_activity(BOT_COMMENT_PREFIX, BOT_STATUS_LABELS, 1, CUTOFF))

    def test_renamed_event_is_new_activity(self):
        with patch("scripts.automation.common.issue_handler.run_gh_json", return_value=[
            {"event": "renamed", "created_at": AFTER},
        ]):
            self.assertTrue(has_new_owner_activity(BOT_COMMENT_PREFIX, BOT_STATUS_LABELS, 1, CUTOFF))


class FindCandidatesTests(unittest.TestCase):
    def test_issue_updated_only_by_bots_own_edits_is_not_a_candidate(self):
        issue = {"number": 1, "updatedAt": AFTER}
        with patch("scripts.automation.common.issue_handler.list_open_issues", return_value=[issue]), \
             patch("scripts.automation.common.issue_handler.has_new_owner_activity", return_value=False), \
             patch("scripts.automation.common.issue_handler.has_recent_owner_reaction", return_value=False):
            self.assertEqual([], find_candidates("claude", MARKER, CUTOFF))

    def test_issue_with_real_new_activity_is_a_candidate(self):
        issue = {"number": 1, "updatedAt": AFTER}
        with patch("scripts.automation.common.issue_handler.list_open_issues", return_value=[issue]), \
             patch("scripts.automation.common.issue_handler.has_new_owner_activity", return_value=True):
            candidates = find_candidates("claude", MARKER, CUTOFF)
        self.assertEqual(1, len(candidates))
        self.assertEqual("issue/comment updated", candidates[0]["reason"])

    def test_issue_not_updated_since_cutoff_skips_timeline_but_checks_reaction(self):
        issue = {"number": 1, "updatedAt": BEFORE}
        with patch("scripts.automation.common.issue_handler.list_open_issues", return_value=[issue]), \
             patch("scripts.automation.common.issue_handler.has_new_owner_activity") as mock_activity, \
             patch("scripts.automation.common.issue_handler.has_recent_owner_reaction", return_value=True):
            candidates = find_candidates("claude", MARKER, CUTOFF)
        mock_activity.assert_not_called()
        self.assertEqual(1, len(candidates))
        self.assertEqual("new reaction on a summary/conclusion comment", candidates[0]["reason"])


if __name__ == "__main__":
    unittest.main()

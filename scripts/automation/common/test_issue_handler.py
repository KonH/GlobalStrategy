import json
import tempfile
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path
from unittest.mock import MagicMock, patch

from scripts.automation.claude.handle_issues import detect_session_limit
from scripts.automation.common.issue_handler import (
    candidate_branch, checkout_clean, count_reclaims_since_owner_comment, find_candidates,
    limit_active, reclaim_stale_in_progress, release_in_progress_silently,
    save_limit_retry_at,
)

MARKER = "<!-- claude-automation -->"
MARKER_PREFIX = "<!-- claude-automation"
RECLAIM_MARKER = "<!-- claude-automation:reclaim -->"


def item(number, labels, **extra):
    return {"number": number, "labels": [{"name": name} for name in labels], **extra}


def comment(body, author="KonH"):
    return {"body": body, "user": {"login": author}}


class FindCandidatesTests(unittest.TestCase):
    def test_item_with_only_the_opt_in_label_is_a_candidate(self):
        with patch("scripts.automation.common.issue_handler.run_gh_json",
                   side_effect=[[item(1, ["claude"])], []]):
            candidates = find_candidates("claude")
        self.assertEqual([1], [c["number"] for c in candidates])
        self.assertEqual("issue", candidates[0]["kind"])

    def test_any_status_label_excludes_the_item(self):
        issues = [
            item(1, ["claude", "claude-in-progress"]),
            item(2, ["claude", "claude-needs-attention"]),
            item(3, ["claude", "claude-complete"]),
            item(4, ["claude"]),
        ]
        with patch("scripts.automation.common.issue_handler.run_gh_json",
                   side_effect=[issues, []]):
            candidates = find_candidates("claude")
        self.assertEqual([4], [c["number"] for c in candidates])

    def test_labeled_prs_are_candidates_with_kind_pr(self):
        pr = item(7, ["claude"], headRefName="feature/foo")
        with patch("scripts.automation.common.issue_handler.run_gh_json",
                   side_effect=[[], [pr]]):
            candidates = find_candidates("claude")
        self.assertEqual([7], [c["number"] for c in candidates])
        self.assertEqual("pr", candidates[0]["kind"])
        self.assertEqual("feature/foo", candidates[0]["headRefName"])

    def test_unrelated_labels_do_not_exclude(self):
        with patch("scripts.automation.common.issue_handler.run_gh_json",
                   side_effect=[[item(1, ["claude", "bug", "codex-in-progress"])], []]):
            self.assertEqual(1, len(find_candidates("claude")))


class CountReclaimsTests(unittest.TestCase):
    def count(self):
        return count_reclaims_since_owner_comment(MARKER_PREFIX, RECLAIM_MARKER, 1)

    def test_counts_reclaim_marker_comments(self):
        with patch("scripts.automation.common.issue_handler.run_gh_json", return_value=[
            comment(f"{RECLAIM_MARKER}\nretry 1"),
            comment(f"{RECLAIM_MARKER}\nretry 2"),
        ]):
            self.assertEqual(2, self.count())

    def test_owner_comment_resets_the_counter(self):
        with patch("scripts.automation.common.issue_handler.run_gh_json", return_value=[
            comment(f"{RECLAIM_MARKER}\nretry 1"),
            comment(f"{RECLAIM_MARKER}\nretry 2"),
            comment("here is more guidance, try again"),
            comment(f"{RECLAIM_MARKER}\nretry 1"),
        ]):
            self.assertEqual(1, self.count())

    def test_bot_marker_comment_does_not_reset(self):
        with patch("scripts.automation.common.issue_handler.run_gh_json", return_value=[
            comment(f"{RECLAIM_MARKER}\nretry 1"),
            comment(f"{MARKER}\nsummary of a previous run"),
            comment(f"{RECLAIM_MARKER}\nretry 2"),
        ]):
            self.assertEqual(2, self.count())

    def test_non_owner_comment_does_not_reset(self):
        with patch("scripts.automation.common.issue_handler.run_gh_json", return_value=[
            comment(f"{RECLAIM_MARKER}\nretry 1"),
            comment("drive-by comment", author="someone-else"),
            comment(f"{RECLAIM_MARKER}\nretry 2"),
        ]):
            self.assertEqual(2, self.count())


class ReclaimStaleInProgressTests(unittest.TestCase):
    def reclaim(self, items, reclaims):
        logger = MagicMock()
        with patch("scripts.automation.common.issue_handler.list_labeled_items", return_value=items), \
             patch("scripts.automation.common.issue_handler.count_reclaims_since_owner_comment",
                   return_value=reclaims), \
             patch("scripts.automation.common.issue_handler.post_comment") as post, \
             patch("scripts.automation.common.issue_handler.add_label") as add, \
             patch("scripts.automation.common.issue_handler.remove_label") as remove:
            reclaim_stale_in_progress(logger, "claude", MARKER)
        return post, add, remove

    def test_first_crash_requeues_with_a_reclaim_comment(self):
        stale = item(5, ["claude", "claude-in-progress"], kind="issue")
        post, add, remove = self.reclaim([stale], reclaims=0)
        self.assertTrue(post.call_args[0][1].startswith(RECLAIM_MARKER))
        add.assert_not_called()
        remove.assert_called_once_with(5, "claude-in-progress")

    def test_third_crash_escalates_to_needs_attention(self):
        stale = item(5, ["claude", "claude-in-progress"], kind="issue")
        post, add, remove = self.reclaim([stale], reclaims=2)
        self.assertTrue(post.call_args[0][1].startswith(MARKER))
        add.assert_called_once_with(5, "claude-needs-attention")
        remove.assert_called_once_with(5, "claude-in-progress")

    def test_needs_attention_items_are_left_untouched(self):
        parked = item(5, ["claude", "claude-in-progress", "claude-needs-attention"], kind="issue")
        post, add, remove = self.reclaim([parked], reclaims=2)
        post.assert_not_called()
        add.assert_not_called()
        remove.assert_not_called()


class CandidateBranchTests(unittest.TestCase):
    def test_issue_starts_from_main(self):
        self.assertEqual("main", candidate_branch({"kind": "issue", "number": 1}))

    def test_pr_starts_from_its_head_branch(self):
        self.assertEqual("feature/foo",
                         candidate_branch({"kind": "pr", "number": 1, "headRefName": "feature/foo"}))


class CheckoutCleanTests(unittest.TestCase):
    def test_force_resets_to_origin_and_removes_untracked(self):
        with patch("scripts.automation.common.issue_handler.run_git") as run_git:
            checkout_clean(MagicMock(), "feature/foo")
        self.assertEqual([
            (["fetch", "origin", "feature/foo"],),
            (["checkout", "-f", "-B", "feature/foo", "origin/feature/foo"],),
            (["clean", "-fd"],),
        ], [c.args for c in run_git.call_args_list])


class LimitFileTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.limit_file = Path(self.tmp.name) / "limit.json"
        self.logger = MagicMock()

    def test_no_file_means_no_active_limit(self):
        self.assertFalse(limit_active(self.logger, self.limit_file))

    def test_future_retry_at_skips_the_run(self):
        save_limit_retry_at(self.limit_file, datetime.now(timezone.utc) + timedelta(hours=1))
        self.assertTrue(limit_active(self.logger, self.limit_file))
        self.assertTrue(self.limit_file.exists())

    def test_expired_retry_at_resumes_and_deletes_the_file(self):
        save_limit_retry_at(self.limit_file, datetime.now(timezone.utc) - timedelta(minutes=1))
        self.assertFalse(limit_active(self.logger, self.limit_file))
        self.assertFalse(self.limit_file.exists())

    def test_retry_at_is_stored_as_aware_utc_even_from_another_timezone(self):
        # +03:00 wall-clock one hour in the future - must still count as active after the
        # round-trip through the file, regardless of the machine's local timezone.
        offset = timezone(timedelta(hours=3))
        save_limit_retry_at(self.limit_file, datetime.now(offset) + timedelta(hours=1))
        stored = json.loads(self.limit_file.read_text(encoding="utf-8"))["retry_at"]
        self.assertTrue(stored.endswith("+00:00"))
        self.assertTrue(limit_active(self.logger, self.limit_file))

    def test_corrupt_file_is_ignored(self):
        self.limit_file.write_text("not json", encoding="utf-8")
        self.assertFalse(limit_active(self.logger, self.limit_file))


class DetectSessionLimitTests(unittest.TestCase):
    def test_epoch_message_yields_aware_utc_retry_at(self):
        limit_hit, retry_at = detect_session_limit(["Claude AI usage limit reached|1753305600"])
        self.assertTrue(limit_hit)
        self.assertEqual(timezone.utc, retry_at.tzinfo)
        self.assertEqual(1753305600, int(retry_at.timestamp()))

    def test_plain_limit_message_without_epoch(self):
        limit_hit, retry_at = detect_session_limit(["Session limit reached, try again later"])
        self.assertTrue(limit_hit)
        self.assertIsNone(retry_at)

    def test_unrelated_error_is_not_a_limit(self):
        self.assertEqual((False, None), detect_session_limit(["fatal: repository not found"]))

    def test_no_error_output_is_not_a_limit(self):
        self.assertEqual((False, None), detect_session_limit([]))


class ReleaseInProgressSilentlyTests(unittest.TestCase):
    def test_releases_without_any_comment(self):
        stale = item(5, ["claude", "claude-in-progress"], kind="issue")
        with patch("scripts.automation.common.issue_handler.list_labeled_items", return_value=[stale]), \
             patch("scripts.automation.common.issue_handler.post_comment") as post, \
             patch("scripts.automation.common.issue_handler.remove_label") as remove:
            release_in_progress_silently(MagicMock(), "claude")
        post.assert_not_called()
        remove.assert_called_once_with(5, "claude-in-progress")

    def test_needs_attention_items_are_left_untouched(self):
        parked = item(5, ["claude", "claude-in-progress", "claude-needs-attention"], kind="issue")
        with patch("scripts.automation.common.issue_handler.list_labeled_items", return_value=[parked]), \
             patch("scripts.automation.common.issue_handler.remove_label") as remove:
            release_in_progress_silently(MagicMock(), "claude")
        remove.assert_not_called()


if __name__ == "__main__":
    unittest.main()

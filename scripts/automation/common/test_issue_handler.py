import unittest
from unittest.mock import MagicMock, patch

from scripts.automation.common.issue_handler import (
    count_reclaims_since_owner_comment, find_candidates, reclaim_stale_in_progress,
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


if __name__ == "__main__":
    unittest.main()

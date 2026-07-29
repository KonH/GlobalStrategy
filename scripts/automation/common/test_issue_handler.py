import json
import tempfile
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path
from unittest.mock import MagicMock, patch

from scripts.automation.claude.handle_issues import (
    detect_session_limit, limit_detection_texts,
)
from scripts.automation.common.issue_handler import (
    SALVAGE_COMMIT_MESSAGE, SALVAGE_GIT_IDENTITY, candidate_branch, checkout_clean,
    count_reclaims_since_owner_comment, find_candidates, handle_limit_pause, limit_active,
    reclaim_stale_in_progress, release_in_progress_silently, salvage_uncommitted_work,
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
    def test_not_ahead_force_resets_to_origin_and_removes_untracked(self):
        with patch("scripts.automation.common.issue_handler.local_branch_exists",
                   return_value=True), \
             patch("scripts.automation.common.issue_handler.run_git") as run_git:
            run_git.side_effect = ["", "0", "", ""]  # fetch, rev-list, checkout, clean
            checkout_clean(MagicMock(), "feature/foo")
        self.assertEqual([
            (["fetch", "origin", "feature/foo"],),
            (["rev-list", "--count", "origin/feature/foo..feature/foo"],),
            (["checkout", "-f", "-B", "feature/foo", "origin/feature/foo"],),
            (["clean", "-fd"],),
        ], [c.args for c in run_git.call_args_list])

    def test_local_branch_missing_skips_ahead_check(self):
        with patch("scripts.automation.common.issue_handler.local_branch_exists",
                   return_value=False), \
             patch("scripts.automation.common.issue_handler.run_git") as run_git:
            checkout_clean(MagicMock(), "feature/foo")
        self.assertEqual([
            (["fetch", "origin", "feature/foo"],),
            (["checkout", "-f", "-B", "feature/foo", "origin/feature/foo"],),
            (["clean", "-fd"],),
        ], [c.args for c in run_git.call_args_list])

    def test_ahead_pushes_then_resets(self):
        with patch("scripts.automation.common.issue_handler.local_branch_exists",
                   return_value=True), \
             patch("scripts.automation.common.issue_handler.run_git") as run_git:
            run_git.side_effect = ["", "2", "", "", ""]  # fetch, rev-list, push, checkout, clean
            checkout_clean(MagicMock(), "feature/foo")
        self.assertEqual([
            (["fetch", "origin", "feature/foo"],),
            (["rev-list", "--count", "origin/feature/foo..feature/foo"],),
            (["push", "-u", "origin", "feature/foo"],),
            (["checkout", "-f", "-B", "feature/foo", "origin/feature/foo"],),
            (["clean", "-fd"],),
        ], [c.args for c in run_git.call_args_list])

    def test_ahead_push_failure_does_not_reset_over_local_tip(self):
        with patch("scripts.automation.common.issue_handler.local_branch_exists",
                   return_value=True), \
             patch("scripts.automation.common.issue_handler.run_git") as run_git:
            def side_effect(args, env=None):
                if args[:2] == ["push", "-u"]:
                    raise RuntimeError("git push failed: rejected")
                if args[:2] == ["rev-list", "--count"]:
                    return "1"
                return ""
            run_git.side_effect = side_effect
            with self.assertRaises(RuntimeError):
                checkout_clean(MagicMock(), "feature/foo")
        checkout_calls = [c.args[0] for c in run_git.call_args_list]
        self.assertTrue(any(c[:2] == ["push", "-u"] for c in checkout_calls))
        self.assertFalse(any(c[:2] == ["checkout", "-f"] for c in checkout_calls))


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

    def test_hit_session_limit_with_wall_clock_resets(self):
        fixed_now = datetime(2026, 7, 29, 10, 0, tzinfo=timezone.utc)
        with patch("scripts.automation.claude.handle_issues.datetime") as mock_dt:
            mock_dt.now.return_value = fixed_now
            mock_dt.fromtimestamp = datetime.fromtimestamp
            limit_hit, retry_at = detect_session_limit(
                ["You've hit your session limit · resets 2:10pm (UTC)"]
            )
        self.assertTrue(limit_hit)
        self.assertEqual(datetime(2026, 7, 29, 14, 10, tzinfo=timezone.utc), retry_at)

    def test_hit_weekly_limit_12am_past_rolls_to_tomorrow(self):
        fixed_now = datetime(2026, 7, 29, 10, 0, tzinfo=timezone.utc)
        with patch("scripts.automation.claude.handle_issues.datetime") as mock_dt:
            mock_dt.now.return_value = fixed_now
            mock_dt.fromtimestamp = datetime.fromtimestamp
            limit_hit, retry_at = detect_session_limit(
                ["You've hit your weekly limit · resets 12am (UTC)"]
            )
        self.assertTrue(limit_hit)
        self.assertEqual(datetime(2026, 7, 30, 0, 0, tzinfo=timezone.utc), retry_at)

    def test_wall_clock_2_10pm_already_past_rolls_to_tomorrow(self):
        fixed_now = datetime(2026, 7, 29, 15, 0, tzinfo=timezone.utc)
        with patch("scripts.automation.claude.handle_issues.datetime") as mock_dt:
            mock_dt.now.return_value = fixed_now
            mock_dt.fromtimestamp = datetime.fromtimestamp
            limit_hit, retry_at = detect_session_limit(
                ["You've hit your session limit · resets 2:10pm (UTC)"]
            )
        self.assertTrue(limit_hit)
        self.assertEqual(datetime(2026, 7, 30, 14, 10, tzinfo=timezone.utc), retry_at)

    def test_epoch_preferred_over_wall_clock_resets(self):
        limit_hit, retry_at = detect_session_limit([
            "Claude AI usage limit reached|1753305600 resets 2:10pm (UTC)"
        ])
        self.assertTrue(limit_hit)
        self.assertEqual(1753305600, int(retry_at.timestamp()))

    def test_distant_hit_narration_is_not_a_limit(self):
        self.assertEqual(
            (False, None),
            detect_session_limit([
                "I hit a snag updating the session limit detector."
            ]),
        )

    def test_unrelated_error_is_not_a_limit(self):
        self.assertEqual((False, None), detect_session_limit(["fatal: repository not found"]))

    def test_no_error_output_is_not_a_limit(self):
        self.assertEqual((False, None), detect_session_limit([]))

    def test_production_pr84_assistant_only_error_shaped(self):
        fixed_now = datetime(2026, 7, 29, 10, 0, tzinfo=timezone.utc)
        assistant = ["You've hit your session limit · resets 2:10pm (UTC)"]
        result = {"is_error": True, "subtype": "success", "result": ""}
        texts = limit_detection_texts(1, result, [], assistant)
        with patch("scripts.automation.claude.handle_issues.datetime") as mock_dt:
            mock_dt.now.return_value = fixed_now
            mock_dt.fromtimestamp = datetime.fromtimestamp
            limit_hit, retry_at = detect_session_limit(texts)
        self.assertTrue(limit_hit)
        self.assertEqual(datetime(2026, 7, 29, 14, 10, tzinfo=timezone.utc), retry_at)


class LimitDetectionTextsTests(unittest.TestCase):
    def test_assistant_included_on_nonzero_returncode(self):
        texts = limit_detection_texts(1, None, ["err"], ["assistant limit text"])
        self.assertEqual(["err", "assistant limit text"], texts)

    def test_assistant_included_on_error_shaped_result(self):
        texts = limit_detection_texts(
            0, {"is_error": True, "subtype": "success"}, ["err"], ["assistant"]
        )
        self.assertEqual(["err", "assistant"], texts)

    def test_assistant_included_when_subtype_starts_with_error(self):
        texts = limit_detection_texts(
            0, {"is_error": False, "subtype": "error_max_turns"}, [], ["assistant"]
        )
        self.assertEqual(["assistant"], texts)

    def test_assistant_excluded_on_successful_completion(self):
        texts = limit_detection_texts(
            0, {"is_error": False, "subtype": "success"}, [],
            ["You've hit your session limit · resets 2:10pm (UTC)"],
        )
        self.assertEqual([], texts)

    def test_error_texts_always_included(self):
        texts = limit_detection_texts(0, {"is_error": False, "subtype": "success"},
                                      ["plain error"], [])
        self.assertEqual(["plain error"], texts)


class SalvageUncommittedWorkTests(unittest.TestCase):
    def test_clean_tree_is_noop(self):
        with patch("scripts.automation.common.issue_handler.run_git", return_value="") as run_git:
            status, detail = salvage_uncommitted_work(MagicMock())
        self.assertEqual("clean", status)
        self.assertEqual([(["status", "--porcelain"],)], [c.args for c in run_git.call_args_list])

    def test_dirty_tree_adds_commits_and_pushes_with_identity(self):
        with patch("scripts.automation.common.issue_handler.run_git") as run_git:
            run_git.side_effect = [" M file.py", "", "", ""]
            status, detail = salvage_uncommitted_work(MagicMock())
        self.assertEqual("committed", status)
        self.assertEqual(SALVAGE_COMMIT_MESSAGE, detail)
        calls = run_git.call_args_list
        self.assertEqual(["status", "--porcelain"], calls[0].args[0])
        self.assertEqual(["add", "-A"], calls[1].args[0])
        self.assertEqual(["commit", "-m", SALVAGE_COMMIT_MESSAGE], calls[2].args[0])
        self.assertEqual(SALVAGE_GIT_IDENTITY, calls[2].kwargs.get("env"))
        self.assertEqual(["push", "-u", "origin", "HEAD"], calls[3].args[0])

    def test_commit_failure_returns_failed(self):
        with patch("scripts.automation.common.issue_handler.run_git") as run_git:
            def side_effect(args, env=None):
                if args[0] == "commit":
                    raise RuntimeError("git commit failed")
                return " M file.py" if args[0] == "status" else ""
            run_git.side_effect = side_effect
            status, detail = salvage_uncommitted_work(MagicMock())
        self.assertEqual("failed", status)
        self.assertIn("git commit failed", detail)


class HandleLimitPauseTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.limit_file = Path(self.tmp.name) / "limit.json"
        self.logger = MagicMock()
        self.candidate = {"number": 84, "kind": "pr"}
        self.retry_at = datetime(2026, 7, 29, 14, 10, tzinfo=timezone.utc)

    def test_success_path_releases_then_posts_note(self):
        with patch("scripts.automation.common.issue_handler.salvage_uncommitted_work",
                   return_value=("committed", SALVAGE_COMMIT_MESSAGE)), \
             patch("scripts.automation.common.issue_handler.release_in_progress_silently") as release, \
             patch("scripts.automation.common.issue_handler.post_comment") as post, \
             patch("scripts.automation.common.issue_handler.add_label") as add, \
             patch("scripts.automation.common.issue_handler.remove_label") as remove:
            handle_limit_pause(self.logger, "claude", MARKER, self.candidate,
                               self.limit_file, self.retry_at)
        release.assert_called_once()
        add.assert_not_called()
        remove.assert_not_called()
        self.assertTrue(self.limit_file.exists())
        self.assertTrue(post.call_args[0][1].startswith(MARKER))
        self.assertIn("committed", post.call_args[0][1])

    def test_clean_path_still_releases_and_notes(self):
        with patch("scripts.automation.common.issue_handler.salvage_uncommitted_work",
                   return_value=("clean", "working tree clean")), \
             patch("scripts.automation.common.issue_handler.release_in_progress_silently") as release, \
             patch("scripts.automation.common.issue_handler.post_comment") as post:
            handle_limit_pause(self.logger, "claude", MARKER, self.candidate,
                               self.limit_file, self.retry_at)
        release.assert_called_once()
        self.assertIn("clean", post.call_args[0][1])

    def test_failure_path_needs_attention_direct_remove_not_silent_release(self):
        with patch("scripts.automation.common.issue_handler.salvage_uncommitted_work",
                   return_value=("failed", "push rejected")), \
             patch("scripts.automation.common.issue_handler.release_in_progress_silently") as release, \
             patch("scripts.automation.common.issue_handler.post_comment") as post, \
             patch("scripts.automation.common.issue_handler.add_label") as add, \
             patch("scripts.automation.common.issue_handler.remove_label") as remove:
            handle_limit_pause(self.logger, "claude", MARKER, self.candidate,
                               self.limit_file, self.retry_at)
        release.assert_not_called()
        add.assert_called_once_with(84, "claude-needs-attention")
        remove.assert_called_once_with(84, "claude-in-progress")
        self.assertIn("needs-attention", post.call_args[0][1])
        self.assertTrue(self.limit_file.exists())

    def test_post_comment_failure_after_release_does_not_raise(self):
        with patch("scripts.automation.common.issue_handler.salvage_uncommitted_work",
                   return_value=("clean", "working tree clean")), \
             patch("scripts.automation.common.issue_handler.release_in_progress_silently") as release, \
             patch("scripts.automation.common.issue_handler.post_comment",
                   side_effect=RuntimeError("gh failed")):
            handle_limit_pause(self.logger, "claude", MARKER, self.candidate,
                               self.limit_file, self.retry_at)
        release.assert_called_once()
        self.assertTrue(self.limit_file.exists())


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

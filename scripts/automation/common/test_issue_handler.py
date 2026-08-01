import json
import tempfile
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path
from unittest.mock import MagicMock, patch

from scripts.automation.claude.handle_issues import (
    detect_session_limit, limit_detection_texts,
)
from scripts.automation.codex.handle_issues import (
    detect_session_limit as detect_codex_session_limit,
)
from scripts.automation.cursor.handle_issues import (
    detect_session_limit as detect_cursor_session_limit,
)
from scripts.automation.common.issue_handler import (
    AI_IN_PROGRESS, SALVAGE_COMMIT_MESSAGE, SALVAGE_GIT_IDENTITY, candidate_branch,
    checkout_clean, claim_candidate, configured_contributors,
    count_reclaims_since_owner_comment, delete_comment, find_candidates,
    handle_limit_pause, limit_active, load_limit_retry_at, reclaim_stale_in_progress,
    release_in_progress_silently, reroute_auto_item_after_limit, salvage_uncommitted_work,
    save_limit_retry_at,
)

MARKER = "<!-- claude-automation -->"
MARKER_PREFIX = "<!-- claude-automation"
RECLAIM_MARKER = "<!-- claude-automation:reclaim -->"


def item(number, labels, **extra):
    return {"number": number, "labels": [{"name": name} for name in labels], **extra}


def comment(body, author="KonH", **extra):
    return {"body": body, "user": {"login": author}, **extra}


def _iso_utc(dt):
    return dt.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def claim_comment(token, comment_id, created_at, kind="issue",
                  marker_prefix=MARKER_PREFIX):
    body = (f"{marker_prefix}:claim:{token}: claiming this {kind} "
            f"for automated processing. -->")
    return comment(body, id=comment_id, created_at=_iso_utc(created_at))


class FindCandidatesTests(unittest.TestCase):
    def test_item_with_only_the_opt_in_label_is_a_candidate(self):
        with patch("scripts.automation.common.issue_handler.run_gh_json",
                   side_effect=[[item(1, ["claude"])], []]):
            candidates = find_candidates("claude")
        self.assertEqual([1], [c["number"] for c in candidates])
        self.assertEqual("issue", candidates[0]["kind"])

    def test_any_status_label_excludes_the_item(self):
        issues = [
            item(1, ["claude", "ai-in-progress"]),
            item(2, ["claude", "ai-need-attention"]),
            item(3, ["claude", "ai-complete"]),
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
                   side_effect=[[item(1, ["claude", "bug", "enhancement"])], []]):
            self.assertEqual(1, len(find_candidates("claude")))

    def test_discovery_queries_every_configured_contributor_for_issues_and_prs(self):
        with patch("scripts.automation.common.issue_handler.configured_contributors",
                   return_value=("KonH", "collaborator")), \
             patch("scripts.automation.common.issue_handler.run_gh_json",
                   side_effect=[[item(1, ["claude"])], [item(2, ["claude"])], [], []]) as run:
            self.assertEqual([1, 2], [candidate["number"] for candidate in find_candidates("claude")])
        authors = [call.args[0][call.args[0].index("--author") + 1] for call in run.call_args_list]
        self.assertEqual(["KonH", "collaborator", "KonH", "collaborator"], authors)


class CountReclaimsTests(unittest.TestCase):
    def count(self):
        return count_reclaims_since_owner_comment(MARKER_PREFIX, RECLAIM_MARKER, 1)

    def test_counts_reclaim_marker_comments(self):
        with patch("scripts.automation.common.issue_handler.run_gh_json", return_value=[
            comment(f"{RECLAIM_MARKER}\nretry 1"),
            comment(f"{RECLAIM_MARKER}\nretry 2"),
        ]):
            self.assertEqual(2, self.count())

    def test_configured_contributor_comment_resets_the_counter(self):
        with patch("scripts.automation.common.issue_handler.run_gh_json", return_value=[
            comment(f"{RECLAIM_MARKER}\nretry 1"),
            comment(f"{RECLAIM_MARKER}\nretry 2"),
            comment("here is more guidance, try again", author="collaborator"),
            comment(f"{RECLAIM_MARKER}\nretry 1"),
        ]), patch("scripts.automation.common.issue_handler.configured_contributors",
                  return_value=("KonH", "collaborator")):
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

    def test_leftover_claim_comment_does_not_reset_the_counter(self):
        claim_body = (f"{MARKER_PREFIX}:claim:deadbeef: claiming this issue "
                      "for automated processing. -->")
        with patch("scripts.automation.common.issue_handler.run_gh_json", return_value=[
            comment(f"{RECLAIM_MARKER}\nretry 1"),
            comment(claim_body),
            comment(f"{RECLAIM_MARKER}\nretry 2"),
        ]):
            self.assertEqual(2, self.count())


class ClaimCandidateTests(unittest.TestCase):
    OWN_TOKEN = "a" * 32
    RIVAL_TOKEN = "b" * 32
    OTHER_TOKEN = "c" * 32

    def setUp(self):
        self.logger = MagicMock()
        self.candidate = item(42, ["claude"], kind="issue")
        self.now = datetime(2026, 8, 1, 12, 0, 0, tzinfo=timezone.utc)

    def _claim(self, comments, settle_seconds=0, freshness_minutes=10,
               post_comment_side_effect=None, add_label_side_effect=None,
               delete_side_effect=None):
        uuid_mock = MagicMock()
        uuid_mock.hex = self.OWN_TOKEN
        with patch("scripts.automation.common.issue_handler.uuid.uuid4",
                   return_value=uuid_mock), \
             patch("scripts.automation.common.issue_handler.time.sleep") as sleep, \
             patch("scripts.automation.common.issue_handler.add_label",
                   side_effect=add_label_side_effect) as add_label, \
             patch("scripts.automation.common.issue_handler.post_comment",
                   side_effect=post_comment_side_effect) as post_comment, \
             patch("scripts.automation.common.issue_handler.run_gh_json",
                   return_value=comments) as run_gh_json, \
             patch("scripts.automation.common.issue_handler.delete_comment",
                   side_effect=delete_side_effect) as delete, \
             patch("scripts.automation.common.issue_handler.remove_label") as remove_label, \
             patch("scripts.automation.common.issue_handler.datetime") as dt_mod:
            dt_mod.now.return_value = self.now
            dt_mod.side_effect = lambda *a, **k: datetime(*a, **k)
            # fromisoformat must still work for created_at parsing
            dt_mod.fromisoformat = datetime.fromisoformat
            result = claim_candidate(
                self.logger, "claude", MARKER, self.candidate,
                freshness_minutes=freshness_minutes, settle_seconds=settle_seconds,
            )
        return result, {
            "sleep": sleep,
            "add_label": add_label,
            "post_comment": post_comment,
            "run_gh_json": run_gh_json,
            "delete": delete,
            "remove_label": remove_label,
        }

    def test_uncontested_claim_wins_and_deletes_own_comment(self):
        own = claim_comment(self.OWN_TOKEN, 100, self.now)
        result, mocks = self._claim([own], settle_seconds=5)
        self.assertTrue(result)
        mocks["add_label"].assert_called_once_with(42, AI_IN_PROGRESS)
        mocks["sleep"].assert_called_once_with(5)
        mocks["delete"].assert_called_once_with(42, 100)
        mocks["remove_label"].assert_not_called()
        posted = mocks["post_comment"].call_args.args[1]
        self.assertTrue(posted.endswith("-->"))
        self.assertTrue(posted.startswith(f"{MARKER_PREFIX}:claim:{self.OWN_TOKEN}"))

    def test_contested_claim_this_instance_wins_and_deletes_both(self):
        own = claim_comment(self.OWN_TOKEN, 10, self.now)
        rival = claim_comment(self.RIVAL_TOKEN, 20, self.now)
        result, mocks = self._claim([rival, own])  # rival listed first; own has lower id
        self.assertTrue(result)
        self.assertEqual(
            [(42, 10), (42, 20)],
            [call.args for call in mocks["delete"].call_args_list],
        )
        mocks["remove_label"].assert_not_called()

    def test_contested_claim_this_instance_loses_and_touches_nothing(self):
        own = claim_comment(self.OWN_TOKEN, 20, self.now)
        rival = claim_comment(self.RIVAL_TOKEN, 10, self.now)
        result, mocks = self._claim([own, rival])
        self.assertFalse(result)
        mocks["delete"].assert_not_called()
        mocks["remove_label"].assert_not_called()

    def test_three_way_contested_claim_sorts_by_id_and_deletes_all(self):
        own = claim_comment(self.OWN_TOKEN, 5, self.now)
        rival = claim_comment(self.RIVAL_TOKEN, 50, self.now)
        other = claim_comment(self.OTHER_TOKEN, 25, self.now)
        # Returned deliberately out of id order to force the explicit sort.
        result, mocks = self._claim([rival, other, own])
        self.assertTrue(result)
        self.assertEqual(
            [(42, 5), (42, 25), (42, 50)],
            [call.args for call in mocks["delete"].call_args_list],
        )

    def test_stale_claim_comments_are_ignored(self):
        stale_time = self.now - timedelta(minutes=11)
        stale = claim_comment(self.RIVAL_TOKEN, 1, stale_time)
        own = claim_comment(self.OWN_TOKEN, 99, self.now)
        result, mocks = self._claim([stale, own], freshness_minutes=10)
        self.assertTrue(result)
        mocks["delete"].assert_called_once_with(42, 99)

    def test_prior_cycle_leaves_no_stray_claim_comment_for_next_cycle(self):
        # After a clean win the winner deletes every fresh claim comment before the CLI
        # runs, so a later need-attention → reopen cycle must see an empty claim set from
        # history and only contend with its own new post.
        own = claim_comment(self.OWN_TOKEN, 200, self.now)
        result, mocks = self._claim([own])
        self.assertTrue(result)
        mocks["delete"].assert_called_once_with(42, 200)

    def test_exception_after_add_label_rolls_back_and_returns_false(self):
        result, mocks = self._claim(
            [], post_comment_side_effect=RuntimeError("gh failed"),
        )
        self.assertFalse(result)
        mocks["add_label"].assert_called_once_with(42, AI_IN_PROGRESS)
        mocks["remove_label"].assert_called_once_with(42, AI_IN_PROGRESS)
        mocks["delete"].assert_not_called()

    def test_exception_on_add_label_is_clean_loss_without_rollback(self):
        result, mocks = self._claim(
            [], add_label_side_effect=RuntimeError("label failed"),
        )
        self.assertFalse(result)
        mocks["post_comment"].assert_not_called()
        mocks["remove_label"].assert_not_called()

    def test_winner_cleanup_tolerates_delete_404(self):
        own = claim_comment(self.OWN_TOKEN, 10, self.now)
        rival = claim_comment(self.RIVAL_TOKEN, 20, self.now)

        def delete_side_effect(number, comment_id):
            if comment_id == 10:
                raise RuntimeError("HTTP 404")

        result, mocks = self._claim(
            [own, rival], delete_side_effect=delete_side_effect,
        )
        self.assertTrue(result)
        self.assertEqual(
            [(42, 10), (42, 20)],
            [call.args for call in mocks["delete"].call_args_list],
        )
        mocks["remove_label"].assert_not_called()

    def test_claim_comment_body_renders_nothing_visible(self):
        own = claim_comment(self.OWN_TOKEN, 100, self.now)
        _, mocks = self._claim([own])
        body = mocks["post_comment"].call_args.args[1]
        self.assertTrue(body.startswith("<!-- "))
        self.assertTrue(body.endswith("-->"))
        self.assertEqual(body.count("-->"), 1)

    def test_delete_comment_uses_comment_id_endpoint(self):
        with patch("scripts.automation.common.issue_handler.run_gh") as run_gh:
            delete_comment(42, 999)
        run_gh.assert_called_once_with([
            "api", "-X", "DELETE",
            "repos/KonH/GlobalStrategy/issues/comments/999",
        ])

    def test_no_fresh_claim_comments_is_a_loss(self):
        result, mocks = self._claim([])
        self.assertFalse(result)
        mocks["delete"].assert_not_called()
        mocks["remove_label"].assert_not_called()


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
        stale = item(5, ["claude", "ai-in-progress"], kind="issue")
        post, add, remove = self.reclaim([stale], reclaims=0)
        self.assertTrue(post.call_args[0][1].startswith(RECLAIM_MARKER))
        add.assert_not_called()
        remove.assert_called_once_with(5, "ai-in-progress")

    def test_third_crash_escalates_to_needs_attention(self):
        stale = item(5, ["claude", "ai-in-progress"], kind="issue")
        post, add, remove = self.reclaim([stale], reclaims=2)
        self.assertTrue(post.call_args[0][1].startswith(MARKER))
        add.assert_called_once_with(5, "ai-need-attention")
        remove.assert_called_once_with(5, "ai-in-progress")

    def test_needs_attention_items_are_left_untouched(self):
        parked = item(5, ["claude", "ai-in-progress", "ai-need-attention"], kind="issue")
        post, add, remove = self.reclaim([parked], reclaims=2)
        post.assert_not_called()
        add.assert_not_called()
        remove.assert_not_called()

    def test_provider_items_without_in_progress_are_skipped(self):
        idle = item(5, ["claude", "ai-complete"], kind="issue")
        post, add, remove = self.reclaim([idle], reclaims=0)
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
        self.state_file = Path(self.tmp.name) / "provider-state.json"
        self.logger = MagicMock()

    def test_no_file_means_no_active_limit(self):
        self.assertFalse(limit_active(self.logger, self.state_file, "claude"))

    def test_future_retry_at_skips_the_run(self):
        save_limit_retry_at(self.state_file, "claude", datetime.now(timezone.utc) + timedelta(hours=1))
        self.assertTrue(limit_active(self.logger, self.state_file, "claude"))
        self.assertTrue(self.state_file.exists())

    def test_expired_retry_at_resumes_and_deletes_the_file(self):
        save_limit_retry_at(self.state_file, "claude", datetime.now(timezone.utc) - timedelta(minutes=1))
        self.assertFalse(limit_active(self.logger, self.state_file, "claude"))
        state = json.loads(self.state_file.read_text(encoding="utf-8"))
        self.assertNotIn("claude", state["providers"])

    def test_retry_at_is_stored_as_aware_utc_even_from_another_timezone(self):
        # +03:00 wall-clock one hour in the future - must still count as active after the
        # round-trip through the file, regardless of the machine's local timezone.
        offset = timezone(timedelta(hours=3))
        save_limit_retry_at(self.state_file, "claude", datetime.now(offset) + timedelta(hours=1))
        stored = json.loads(self.state_file.read_text(encoding="utf-8"))["providers"]["claude"]["limit_retry_at"]
        self.assertTrue(stored.endswith("+00:00"))
        self.assertTrue(limit_active(self.logger, self.state_file, "claude"))

    def test_corrupt_file_is_ignored(self):
        self.state_file.write_text("not json", encoding="utf-8")
        self.assertFalse(limit_active(self.logger, self.state_file, "claude"))

    def test_provider_limits_are_independent(self):
        save_limit_retry_at(self.state_file, "claude", datetime.now(timezone.utc) + timedelta(hours=1))
        save_limit_retry_at(self.state_file, "codex", datetime.now(timezone.utc) + timedelta(hours=2))
        self.assertTrue(limit_active(self.logger, self.state_file, "claude"))
        self.assertTrue(limit_active(self.logger, self.state_file, "codex"))
        self.assertFalse(limit_active(self.logger, self.state_file, "cursor"))

    def test_missing_limit_retry_at_is_silent_none(self):
        # Provider records with only last_auto_selection_at are normal after auto-routing;
        # that must not look like a corrupt limit-retry timestamp.
        self.state_file.write_text(json.dumps({
            "providers": {
                "cursor": {"last_auto_selection_at": "2026-07-30T10:15:18.164620+00:00"},
            }
        }), encoding="utf-8")
        self.assertIsNone(load_limit_retry_at(self.logger, self.state_file, "cursor"))
        self.assertFalse(limit_active(self.logger, self.state_file, "cursor"))
        self.logger.warning.assert_not_called()

    def test_unparseable_limit_retry_at_warns_and_ignores(self):
        self.state_file.write_text(json.dumps({
            "providers": {"cursor": {"limit_retry_at": "not-a-timestamp"}}
        }), encoding="utf-8")
        self.assertIsNone(load_limit_retry_at(self.logger, self.state_file, "cursor"))
        self.logger.warning.assert_called_once()
        self.assertEqual(
            ("Could not parse stored %s limit-retry time - ignoring it.", "cursor"),
            self.logger.warning.call_args.args,
        )


class DetectCodexSessionLimitTests(unittest.TestCase):
    PRODUCTION_LIMIT = (
        "You've hit your usage limit. Upgrade to Pro (https://chatgpt.com/explore/pro), "
        "visit https://chatgpt.com/codex/settings/usage to purchase more credits or "
        "try again at Aug 5th, 2026 9:31 AM."
    )

    def test_production_usage_limit_parses_try_again_at(self):
        # error/failed events are json-dumped into error_texts by run_codex
        error_event = json.dumps({"type": "error", "message": self.PRODUCTION_LIMIT})
        limit_hit, retry_at = detect_codex_session_limit([error_event])
        self.assertTrue(limit_hit)
        self.assertEqual(datetime(2026, 8, 5, 9, 31, tzinfo=timezone.utc), retry_at)

    def test_plain_usage_limit_without_try_again_at(self):
        limit_hit, retry_at = detect_codex_session_limit(["usage limit exceeded"])
        self.assertTrue(limit_hit)
        self.assertIsNone(retry_at)

    def test_unrelated_error_is_not_a_limit(self):
        self.assertEqual((False, None), detect_codex_session_limit(["fatal: repository not found"]))


class DetectCursorSessionLimitTests(unittest.TestCase):
    # Production cursor-agent CLI wording from forum.cursor.com/t/cursor-agent-cli-limit-hit
    PRODUCTION_LIMIT = (
        "Error: You've hit your usage limit You've saved xx on API model usage this month "
        "with Pro. Switch to Auto for more usage or set a Spend Limit to continue with Sonnet. "
        "Your usage limits will reset when your monthly cycle ends on 8/14/2025. "
        "fallbackModel: default spendLimitHit: false"
    )
    FREE_REQUESTS_LIMIT = (
        "You've hit your free requests limit. Upgrade to Pro for more usage, frontier models, "
        "Background Agents, and more. Your usage limits will reset when your monthly cycle "
        "ends on 8/21/2025."
    )

    def test_production_usage_limit_parses_cycle_ends_on(self):
        fixed_now = datetime(2025, 8, 10, 12, 0, tzinfo=timezone.utc)
        with patch("scripts.automation.cursor.handle_issues.datetime") as mock_dt:
            mock_dt.now.return_value = fixed_now
            mock_dt.side_effect = lambda *a, **k: datetime(*a, **k)
            limit_hit, retry_at = detect_cursor_session_limit([self.PRODUCTION_LIMIT])
        self.assertTrue(limit_hit)
        self.assertEqual(datetime(2025, 8, 14, tzinfo=timezone.utc), retry_at)

    def test_cycle_ends_on_today_rolls_to_tomorrow(self):
        fixed_now = datetime(2025, 8, 14, 11, 0, tzinfo=timezone.utc)
        with patch("scripts.automation.cursor.handle_issues.datetime") as mock_dt:
            mock_dt.now.return_value = fixed_now
            mock_dt.side_effect = lambda *a, **k: datetime(*a, **k)
            limit_hit, retry_at = detect_cursor_session_limit([self.PRODUCTION_LIMIT])
        self.assertTrue(limit_hit)
        self.assertEqual(datetime(2025, 8, 15, tzinfo=timezone.utc), retry_at)

    def test_free_requests_limit_parses_cycle_ends_on(self):
        fixed_now = datetime(2025, 8, 10, 12, 0, tzinfo=timezone.utc)
        with patch("scripts.automation.cursor.handle_issues.datetime") as mock_dt:
            mock_dt.now.return_value = fixed_now
            mock_dt.side_effect = lambda *a, **k: datetime(*a, **k)
            limit_hit, retry_at = detect_cursor_session_limit([self.FREE_REQUESTS_LIMIT])
        self.assertTrue(limit_hit)
        self.assertEqual(datetime(2025, 8, 21, tzinfo=timezone.utc), retry_at)

    def test_plain_usage_limit_without_cycle_ends(self):
        limit_hit, retry_at = detect_cursor_session_limit(["You've hit your usage limit."])
        self.assertTrue(limit_hit)
        self.assertIsNone(retry_at)

    def test_unrelated_error_is_not_a_limit(self):
        self.assertEqual((False, None), detect_cursor_session_limit(["fatal: repository not found"]))


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

    def test_success_path_releases_then_reroutes_then_posts_note(self):
        with patch("scripts.automation.common.issue_handler.salvage_uncommitted_work",
                   return_value=("committed", SALVAGE_COMMIT_MESSAGE)), \
             patch("scripts.automation.common.issue_handler.release_in_progress_silently") as release, \
             patch("scripts.automation.common.issue_handler.reroute_auto_item_after_limit",
                   return_value=None) as reroute, \
             patch("scripts.automation.common.issue_handler.post_comment") as post, \
             patch("scripts.automation.common.issue_handler.add_label") as add, \
             patch("scripts.automation.common.issue_handler.remove_label") as remove:
            handle_limit_pause(self.logger, "claude", MARKER, self.candidate,
                               self.limit_file, self.retry_at)
        release.assert_called_once()
        reroute.assert_called_once_with(self.logger, "claude", self.candidate, self.limit_file)
        add.assert_not_called()
        remove.assert_not_called()
        self.assertTrue(self.limit_file.exists())
        self.assertTrue(post.call_args[0][1].startswith(MARKER))
        self.assertIn("committed", post.call_args[0][1])
        self.assertIn("Pausing until", post.call_args[0][1])

    def test_success_path_note_mentions_reroute_when_a_new_provider_is_picked(self):
        with patch("scripts.automation.common.issue_handler.salvage_uncommitted_work",
                   return_value=("clean", "working tree clean")), \
             patch("scripts.automation.common.issue_handler.release_in_progress_silently"), \
             patch("scripts.automation.common.issue_handler.reroute_auto_item_after_limit",
                   return_value="codex"), \
             patch("scripts.automation.common.issue_handler.post_comment") as post:
            handle_limit_pause(self.logger, "claude", MARKER, self.candidate,
                               self.limit_file, self.retry_at)
        self.assertIn("Rerouted to `codex`", post.call_args[0][1])
        self.assertNotIn("Pausing until", post.call_args[0][1])

    def test_clean_path_still_releases_and_notes(self):
        with patch("scripts.automation.common.issue_handler.salvage_uncommitted_work",
                   return_value=("clean", "working tree clean")), \
             patch("scripts.automation.common.issue_handler.release_in_progress_silently") as release, \
             patch("scripts.automation.common.issue_handler.reroute_auto_item_after_limit",
                   return_value=None), \
             patch("scripts.automation.common.issue_handler.post_comment") as post:
            handle_limit_pause(self.logger, "claude", MARKER, self.candidate,
                               self.limit_file, self.retry_at)
        release.assert_called_once()
        self.assertIn("clean", post.call_args[0][1])

    def test_failure_path_needs_attention_direct_remove_not_silent_release(self):
        with patch("scripts.automation.common.issue_handler.salvage_uncommitted_work",
                   return_value=("failed", "push rejected")), \
             patch("scripts.automation.common.issue_handler.release_in_progress_silently") as release, \
             patch("scripts.automation.common.issue_handler.reroute_auto_item_after_limit") as reroute, \
             patch("scripts.automation.common.issue_handler.post_comment") as post, \
             patch("scripts.automation.common.issue_handler.add_label") as add, \
             patch("scripts.automation.common.issue_handler.remove_label") as remove:
            handle_limit_pause(self.logger, "claude", MARKER, self.candidate,
                               self.limit_file, self.retry_at)
        release.assert_not_called()
        reroute.assert_not_called()
        add.assert_called_once_with(84, "ai-need-attention")
        remove.assert_called_once_with(84, "ai-in-progress")
        self.assertIn("ai-need-attention", post.call_args[0][1])
        self.assertTrue(self.limit_file.exists())

    def test_post_comment_failure_after_release_does_not_raise(self):
        with patch("scripts.automation.common.issue_handler.salvage_uncommitted_work",
                   return_value=("clean", "working tree clean")), \
             patch("scripts.automation.common.issue_handler.release_in_progress_silently") as release, \
             patch("scripts.automation.common.issue_handler.reroute_auto_item_after_limit",
                   return_value=None), \
             patch("scripts.automation.common.issue_handler.post_comment",
                   side_effect=RuntimeError("gh failed")):
            handle_limit_pause(self.logger, "claude", MARKER, self.candidate,
                               self.limit_file, self.retry_at)
        release.assert_called_once()
        self.assertTrue(self.limit_file.exists())


class ReleaseInProgressSilentlyTests(unittest.TestCase):
    def test_releases_without_any_comment(self):
        stale = item(5, ["claude", "ai-in-progress"], kind="issue")
        with patch("scripts.automation.common.issue_handler.list_labeled_items", return_value=[stale]), \
             patch("scripts.automation.common.issue_handler.post_comment") as post, \
             patch("scripts.automation.common.issue_handler.remove_label") as remove:
            release_in_progress_silently(MagicMock(), "claude")
        post.assert_not_called()
        remove.assert_called_once_with(5, "ai-in-progress")

    def test_needs_attention_items_are_left_untouched(self):
        parked = item(5, ["claude", "ai-in-progress", "ai-need-attention"], kind="issue")
        with patch("scripts.automation.common.issue_handler.list_labeled_items", return_value=[parked]), \
             patch("scripts.automation.common.issue_handler.remove_label") as remove:
            release_in_progress_silently(MagicMock(), "claude")
        remove.assert_not_called()

    def test_skips_items_without_in_progress(self):
        idle = item(5, ["claude"], kind="issue")
        with patch("scripts.automation.common.issue_handler.list_labeled_items", return_value=[idle]), \
             patch("scripts.automation.common.issue_handler.remove_label") as remove:
            release_in_progress_silently(MagicMock(), "claude")
        remove.assert_not_called()


class RerouteAutoItemAfterLimitTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.state_file = Path(self.tmp.name) / "provider-state.json"
        self.logger = MagicMock()

    def test_non_auto_item_does_nothing(self):
        candidate = item(84, ["claude"], kind="pr")
        with patch("scripts.automation.common.issue_handler.remove_label") as remove, \
             patch("scripts.automation.common.issue_handler.add_label") as add:
            result = reroute_auto_item_after_limit(self.logger, "claude", candidate, self.state_file)
        self.assertIsNone(result)
        remove.assert_not_called()
        add.assert_not_called()

    def test_auto_item_drops_current_label_and_routes_to_a_free_provider(self):
        candidate = item(84, ["claude", "auto-ai"], kind="pr")
        with patch("scripts.automation.common.issue_handler.select_auto_provider",
                   return_value="codex") as select, \
             patch("scripts.automation.common.issue_handler.remove_label") as remove, \
             patch("scripts.automation.common.issue_handler.add_label") as add, \
             patch("scripts.automation.common.issue_handler.record_auto_selection") as record, \
             patch("scripts.automation.common.issue_handler.verify_label_present") as verify:
            result = reroute_auto_item_after_limit(self.logger, "claude", candidate, self.state_file)
        self.assertEqual("codex", result)
        remove.assert_called_once_with(84, "claude")
        add.assert_called_once_with(84, "codex")
        record.assert_called_once_with(self.state_file, "codex")
        verify.assert_called_once_with(self.logger, 84, "codex")
        self.assertEqual(["codex", "cursor"], select.call_args[0][2])

    def test_all_other_providers_limited_parks_with_need_attention(self):
        candidate = item(84, ["claude", "auto-ai"], kind="issue")
        with patch("scripts.automation.common.issue_handler.select_auto_provider",
                   return_value=None), \
             patch("scripts.automation.common.issue_handler.remove_label") as remove, \
             patch("scripts.automation.common.issue_handler.park_auto_item_unroutable") as park:
            result = reroute_auto_item_after_limit(self.logger, "claude", candidate, self.state_file)
        self.assertIsNone(result)
        remove.assert_called_once_with(84, "claude")
        park.assert_called_once_with(self.logger, candidate)


class VerifyLabelPresentTests(unittest.TestCase):
    def test_returns_true_on_first_visible_read(self):
        logger = MagicMock()
        with patch("scripts.automation.common.issue_handler.get_item_label_names",
                   return_value={"auto-ai", "claude"}), \
             patch("scripts.automation.common.issue_handler.time.sleep") as sleep:
            from scripts.automation.common.issue_handler import verify_label_present
            self.assertTrue(verify_label_present(logger, 9, "claude"))
        sleep.assert_not_called()

    def test_retries_with_backoff_then_succeeds(self):
        logger = MagicMock()
        with patch("scripts.automation.common.issue_handler.get_item_label_names",
                   side_effect=[{"auto-ai"}, {"auto-ai"}, {"auto-ai", "claude"}]), \
             patch("scripts.automation.common.issue_handler.time.sleep") as sleep:
            from scripts.automation.common.issue_handler import verify_label_present
            self.assertTrue(verify_label_present(logger, 9, "claude"))
        self.assertEqual([1.0, 2.0], [call.args[0] for call in sleep.call_args_list])

    def test_missing_label_logs_and_returns_false_without_raising(self):
        logger = MagicMock()
        with patch("scripts.automation.common.issue_handler.get_item_label_names",
                   return_value={"auto-ai"}), \
             patch("scripts.automation.common.issue_handler.time.sleep") as sleep:
            from scripts.automation.common.issue_handler import verify_label_present
            self.assertFalse(verify_label_present(logger, 9, "claude"))
        self.assertEqual(2, sleep.call_count)
        logger.warning.assert_called()


if __name__ == "__main__":
    unittest.main()

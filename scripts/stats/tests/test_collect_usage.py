import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from scripts.stats.collect_usage import (
    _scan_watermark_cap,
    find_claude_transcripts,
    find_repo_worktree_roots,
    project_slug,
)


def run_git(args, cwd):
    subprocess.run(["git"] + args, cwd=cwd, check=True, capture_output=True, text=True)


class WorktreeDiscoveryTests(unittest.TestCase):
    def test_single_checkout_returns_just_that_root(self):
        with tempfile.TemporaryDirectory() as repo_dir:
            run_git(["init"], repo_dir)

            roots = find_repo_worktree_roots(repo_dir)

            self.assertEqual([Path(repo_dir).resolve()], [r.resolve() for r in roots])

    def test_added_worktree_is_included(self):
        with tempfile.TemporaryDirectory() as parent:
            repo_dir = Path(parent) / "main"
            worktree_dir = Path(parent) / "wt"
            run_git(["init", str(repo_dir)], parent)
            run_git(["-C", str(repo_dir), "config", "user.email", "test@example.com"], parent)
            run_git(["-C", str(repo_dir), "config", "user.name", "Test"], parent)
            (repo_dir / "f.txt").write_text("x", encoding="utf-8")
            run_git(["-C", str(repo_dir), "add", "f.txt"], parent)
            run_git(["-C", str(repo_dir), "commit", "-m", "init"], parent)
            run_git(["-C", str(repo_dir), "worktree", "add", "-b", "feature", str(worktree_dir)], parent)

            roots = {r.resolve() for r in find_repo_worktree_roots(repo_dir)}

            self.assertIn(repo_dir.resolve(), roots)
            self.assertIn(worktree_dir.resolve(), roots)

    def test_non_git_directory_falls_back_to_itself(self):
        with tempfile.TemporaryDirectory() as plain_dir:
            roots = find_repo_worktree_roots(plain_dir)

            self.assertEqual([Path(plain_dir)], roots)


class FindClaudeTranscriptsTests(unittest.TestCase):
    def test_transcripts_from_a_worktree_project_dir_are_included(self):
        with tempfile.TemporaryDirectory() as fake_home:
            with tempfile.TemporaryDirectory() as parent:
                repo_dir = Path(parent) / "main"
                worktree_dir = Path(parent) / "wt"
                run_git(["init", str(repo_dir)], parent)
                run_git(["-C", str(repo_dir), "config", "user.email", "test@example.com"], parent)
                run_git(["-C", str(repo_dir), "config", "user.name", "Test"], parent)
                (repo_dir / "f.txt").write_text("x", encoding="utf-8")
                run_git(["-C", str(repo_dir), "add", "f.txt"], parent)
                run_git(["-C", str(repo_dir), "commit", "-m", "init"], parent)
                run_git(["-C", str(repo_dir), "worktree", "add", "-b", "feature", str(worktree_dir)], parent)

                projects_dir = Path(fake_home) / ".claude" / "projects"
                main_project_dir = projects_dir / project_slug(repo_dir)
                wt_project_dir = projects_dir / project_slug(worktree_dir)
                main_project_dir.mkdir(parents=True)
                wt_project_dir.mkdir(parents=True)
                (main_project_dir / "main-session.jsonl").write_text("{}\n", encoding="utf-8")
                (wt_project_dir / "wt-session.jsonl").write_text("{}\n", encoding="utf-8")

                with patch("scripts.stats.collect_usage.Path.home", return_value=Path(fake_home)):
                    found = find_claude_transcripts(repo_dir, since_ts=None)

                names = {p.name for p in found}
                self.assertEqual({"main-session.jsonl", "wt-session.jsonl"}, names)


class ScanWatermarkCapTests(unittest.TestCase):
    def test_returns_none_when_every_file_parses_cleanly(self):
        with tempfile.TemporaryDirectory() as d:
            path = Path(d) / "a.jsonl"
            path.write_text("{}", encoding="utf-8")

            rows, cap = _scan_watermark_cap([path], lambda p: [{"path": str(p)}])

            self.assertEqual([{"path": str(path)}], rows)
            self.assertIsNone(cap)

    def test_a_failed_file_caps_the_watermark_at_its_own_mtime_so_it_is_retried(self):
        with tempfile.TemporaryDirectory() as d:
            good = Path(d) / "good.jsonl"
            bad = Path(d) / "bad.jsonl"
            good.write_text("{}", encoding="utf-8")
            bad.write_text("{}", encoding="utf-8")

            def parse(path):
                if path == bad:
                    raise UnicodeDecodeError("utf-8", b"", 0, 1, "boom")
                return [{"path": str(path)}]

            rows, cap = _scan_watermark_cap([good, bad], parse)

            self.assertEqual([{"path": str(good)}], rows)
            self.assertAlmostEqual(bad.stat().st_mtime, cap, delta=2)

    def test_cap_is_the_earlier_of_two_failed_files(self):
        with tempfile.TemporaryDirectory() as d:
            older = Path(d) / "older.jsonl"
            newer = Path(d) / "newer.jsonl"
            older.write_text("{}", encoding="utf-8")
            newer.write_text("{}", encoding="utf-8")
            older_mtime = newer.stat().st_mtime - 100
            import os
            os.utime(older, (older_mtime, older_mtime))

            def parse(path):
                raise OSError("boom")

            _, cap = _scan_watermark_cap([newer, older], parse)

            self.assertAlmostEqual(older.stat().st_mtime, cap, delta=2)


if __name__ == "__main__":
    unittest.main()

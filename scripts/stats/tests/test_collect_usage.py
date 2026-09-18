import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from scripts.stats.collect_usage import find_claude_transcripts, find_repo_worktree_roots, project_slug


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


if __name__ == "__main__":
    unittest.main()

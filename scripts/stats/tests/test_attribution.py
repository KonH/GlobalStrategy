import tempfile
import unittest
from pathlib import Path

from scripts.stats.attribution import attribute_segment


class AttributionTests(unittest.TestCase):
    def test_spec_dir_write_wins_over_branch_fallback(self):
        with tempfile.TemporaryDirectory() as tmp:
            (Path(tmp) / "Docs" / "Specs" / "26_01_01_00_other").mkdir(parents=True)
            (Path(tmp) / "Docs" / "Specs" / "26_01_01_00_example").mkdir(parents=True)

            result = attribute_segment(
                write_paths=["Docs/Specs/26_01_01_00_example/plan.md"],
                git_branch="ralph/26_01_01_00_other",
                repo_root=tmp,
            )

            self.assertEqual("26_01_01_00_example", result)

    def test_branch_fallback_used_when_no_spec_dir_write(self):
        with tempfile.TemporaryDirectory() as tmp:
            (Path(tmp) / "Docs" / "Specs" / "26_01_01_00_example").mkdir(parents=True)

            result = attribute_segment(
                write_paths=["src/GlobalStrategy.Core/Foo.cs"],
                git_branch="ralph/26_01_01_00_example",
                repo_root=tmp,
            )

            self.assertEqual("26_01_01_00_example", result)

    def test_no_write_and_no_matching_branch_produces_no_attribution(self):
        with tempfile.TemporaryDirectory() as tmp:
            result = attribute_segment(
                write_paths=["src/GlobalStrategy.Core/Foo.cs"],
                git_branch="feature/unrelated",
                repo_root=tmp,
            )

            self.assertIsNone(result)

    def test_multi_dir_segment_attributes_to_first_match_only(self):
        with tempfile.TemporaryDirectory() as tmp:
            (Path(tmp) / "Docs" / "Specs" / "26_01_01_00_first").mkdir(parents=True)
            (Path(tmp) / "Docs" / "Specs" / "26_01_01_00_second").mkdir(parents=True)

            result = attribute_segment(
                write_paths=[
                    "Docs/Specs/26_01_01_00_first/plan.md",
                    "Docs/Specs/26_01_01_00_second/spec.md",
                ],
                git_branch=None,
                repo_root=tmp,
            )

            self.assertEqual("26_01_01_00_first", result)


if __name__ == "__main__":
    unittest.main()

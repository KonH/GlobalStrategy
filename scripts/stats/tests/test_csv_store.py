import tempfile
import unittest
from pathlib import Path

from scripts.stats.csv_store import COLUMNS, upsert_row, usage_csv_path


class CsvStoreTests(unittest.TestCase):
    def test_header_written_for_new_file(self):
        with tempfile.TemporaryDirectory() as tmp:
            upsert_row(tmp, {"session_id": "s1", "stage": "spec"})

            content = usage_csv_path(tmp).read_text(encoding="utf-8")
            self.assertEqual(",".join(COLUMNS), content.splitlines()[0])

    def test_column_order_matches_spec_exactly(self):
        expected = [
            "spec_id", "version", "stage", "mode", "context", "start", "end",
            "provider", "model", "cost_usd", "input_tokens", "cached_input_tokens",
            "output_tokens", "spec_size_kb", "plan_size_kb", "diff_lines", "session_id",
        ]
        self.assertEqual(expected, COLUMNS)

    def test_new_row_appended_when_key_absent(self):
        with tempfile.TemporaryDirectory() as tmp:
            upsert_row(tmp, {"session_id": "s1", "stage": "spec", "input_tokens": "10"})
            upsert_row(tmp, {"session_id": "s1", "stage": "plan", "input_tokens": "20"})

            lines = usage_csv_path(tmp).read_text(encoding="utf-8").splitlines()
            self.assertEqual(3, len(lines))  # header + 2 rows
            self.assertIn("spec", lines[1])
            self.assertIn("plan", lines[2])

    def test_existing_row_updated_in_place_when_key_present(self):
        with tempfile.TemporaryDirectory() as tmp:
            upsert_row(tmp, {"session_id": "s1", "stage": "spec", "input_tokens": "10"})
            upsert_row(tmp, {"session_id": "s2", "stage": "spec", "input_tokens": "99"})
            upsert_row(tmp, {"session_id": "s1", "stage": "spec", "input_tokens": "500"})

            lines = usage_csv_path(tmp).read_text(encoding="utf-8").splitlines()
            self.assertEqual(3, len(lines))  # header + s1 + s2, no duplicate s1 row
            self.assertIn("500", lines[1])  # s1's row stays in its original position
            self.assertIn("99", lines[2])


if __name__ == "__main__":
    unittest.main()

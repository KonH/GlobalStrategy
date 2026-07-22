import tempfile
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path

from scripts.stats.csv_store import upsert_row, usage_csv_path
from scripts.stats.watermark import advance_watermark, get_last_scanned


class WatermarkTests(unittest.TestCase):
    def test_missing_watermark_file_scans_everything(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "watermark.json"

            self.assertIsNone(get_last_scanned("claude", path))

    def test_watermark_advances_after_successful_scan(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "watermark.json"
            before = datetime.now(timezone.utc) - timedelta(seconds=1)

            advance_watermark("claude", path=path)

            after_ts = get_last_scanned("claude", path)
            self.assertGreater(after_ts, before.timestamp())

    def test_deleting_watermark_does_not_duplicate_rows(self):
        with tempfile.TemporaryDirectory() as spec_dir:
            row = {"session_id": "s1", "stage": "spec", "input_tokens": "10"}
            upsert_row(spec_dir, row)
            # Simulate a full rescan after the watermark file was deleted: the same
            # session/stage gets reprocessed and re-submitted.
            upsert_row(spec_dir, row)

            lines = usage_csv_path(spec_dir).read_text(encoding="utf-8").splitlines()
            self.assertEqual(2, len(lines))  # header + exactly one row, not two


if __name__ == "__main__":
    unittest.main()

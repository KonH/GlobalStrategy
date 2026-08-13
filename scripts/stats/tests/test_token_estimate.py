import tempfile
import unittest
from pathlib import Path

from scripts.stats.token_estimate import estimate_tokens, file_tokens


class TokenEstimateTests(unittest.TestCase):
    def test_estimate_tokens_uses_four_chars_per_token(self):
        self.assertEqual(25, estimate_tokens("a" * 100))

    def test_estimate_tokens_empty_string_is_zero(self):
        self.assertEqual(0, estimate_tokens(""))

    def test_file_tokens_missing_file_is_empty_string(self):
        with tempfile.TemporaryDirectory() as tmp:
            self.assertEqual("", file_tokens(Path(tmp) / "nope.md"))

    def test_file_tokens_reads_existing_file(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "spec.md"
            path.write_text("b" * 40, encoding="utf-8")

            self.assertEqual(10, file_tokens(path))


if __name__ == "__main__":
    unittest.main()

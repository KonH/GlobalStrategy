import unittest

from scripts.stats.backfill_implement_rows import (
    DEFAULT_PROVIDER_MODEL,
    TRAILER_MODEL_MAP,
    base_stage,
    checkbox_counts,
)


class CheckboxCountsTests(unittest.TestCase):
    def test_counts_checked_and_unchecked_lines(self):
        text = "- [x] Step 1\n- [ ] Step 2\n- [x] Step 3\nnot a checkbox line\n"

        checked, unchecked = checkbox_counts(text)

        self.assertEqual(2, checked)
        self.assertEqual(1, unchecked)

    def test_empty_text_is_zero_and_zero(self):
        self.assertEqual((0, 0), checkbox_counts(""))

    def test_indented_or_mid_line_brackets_are_not_counted(self):
        # Only a checkbox at the start of a line counts - a stray "[x]" inside prose
        # (e.g. quoting the syntax itself) must not be mistaken for a real step.
        text = "See the `- [x]` convention below.\n  - [x] not at line start\n"

        self.assertEqual((0, 0), checkbox_counts(text))


class BaseStageTests(unittest.TestCase):
    def test_strips_user_input_suffix(self):
        self.assertEqual("implement", base_stage("implement_user_input_3"))

    def test_leaves_plain_stage_unchanged(self):
        self.assertEqual("plan", base_stage("plan"))


class TrailerModelMapTests(unittest.TestCase):
    def test_every_entry_maps_to_a_provider_and_model(self):
        for trailer, (provider, model) in TRAILER_MODEL_MAP.items():
            self.assertTrue(provider, msg=f"missing provider for {trailer!r}")
            self.assertTrue(model, msg=f"missing model for {trailer!r}")

    def test_default_fallback_is_claude_sonnet_5(self):
        # Matches the original spec/plan backfill's own documented convention for
        # commits with no Co-Authored-By trailer at all.
        self.assertEqual(("claude", "claude-sonnet-5"), DEFAULT_PROVIDER_MODEL)


if __name__ == "__main__":
    unittest.main()

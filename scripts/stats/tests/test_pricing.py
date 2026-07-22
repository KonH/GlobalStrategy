import unittest

from scripts.stats.pricing import compute_cost


SAMPLE_TABLE = {
    "test-model": {
        "uncached_input_per_mtok": 3.0,
        "cached_input_per_mtok": 0.3,
        "output_per_mtok": 15.0,
    }
}


class PricingTests(unittest.TestCase):
    def test_known_model_cost_computed_from_rates(self):
        cost, warning = compute_cost(
            "test-model", input_tokens=1_000_000, cached_input_tokens=1_000_000, output_tokens=1_000_000,
            table=SAMPLE_TABLE,
        )

        self.assertIsNone(warning)
        self.assertAlmostEqual(3.0 + 0.3 + 15.0, cost)

    def test_unknown_model_returns_none_cost_and_warning(self):
        cost, warning = compute_cost(
            "nonexistent-model", input_tokens=100, cached_input_tokens=0, output_tokens=100,
            table=SAMPLE_TABLE,
        )

        self.assertIsNone(cost)
        self.assertIn("nonexistent-model", warning)

    def test_zero_token_counts_produce_zero_cost(self):
        cost, warning = compute_cost(
            "test-model", input_tokens=0, cached_input_tokens=0, output_tokens=0,
            table=SAMPLE_TABLE,
        )

        self.assertIsNone(warning)
        self.assertEqual(0.0, cost)


if __name__ == "__main__":
    unittest.main()

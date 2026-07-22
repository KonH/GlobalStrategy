"""Cost computation from a checked-in, hand-maintained per-model pricing table.

Never uses a CLI's own reported cost figure - cost is always derived from
pricing_table.json so figures are comparable across providers and modes.
"""

import json
from pathlib import Path

PRICING_TABLE_PATH = Path(__file__).parent / "pricing_table.json"


def load_pricing_table(path=PRICING_TABLE_PATH):
    return json.loads(Path(path).read_text(encoding="utf-8"))


def compute_cost(model, input_tokens, cached_input_tokens, output_tokens, table=None):
    """Returns (cost_usd: float | None, warning: str | None).

    A model absent from the table never raises and never blocks the caller -
    it returns None with a warning the caller should log and continue past.
    """
    if table is None:
        table = load_pricing_table()

    rates = table.get(model)
    if rates is None:
        return None, f"unknown model '{model}', cost_usd left empty"

    cost = (
        input_tokens / 1e6 * rates["uncached_input_per_mtok"]
        + cached_input_tokens / 1e6 * rates["cached_input_per_mtok"]
        + output_tokens / 1e6 * rates["output_per_mtok"]
    )
    return cost, None

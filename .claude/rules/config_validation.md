---
paths:
  - "Assets/Configs/*.json"
---

# Config Cross-Validation

## Country IDs must match `countries.json` exactly

Any feature config that references a `countryId` (e.g. `characters.json`, `organizations.json`) must use the exact ID string from `countries.json`. Mismatches are **silent**: no error is thrown; the feature simply produces nothing at runtime.

Example that caused a bug: `characters.json` had pool `countryId: "Great_Britain"` while the actual entry in `countries.json` was `"United_Kingdom_of_Great_Britain_and_Ireland"`. Result: zero characters created for that country, no console warning.

## Validation script

When adding or editing a feature config that references country IDs, run a quick cross-check:

```python
import json

with open("Assets/Configs/countries.json") as f:
    available = {c["countryId"] for c in json.load(f)["countries"] if c.get("isAvailable")}

with open("Assets/Configs/characters.json") as f:
    pools = {p["countryId"] for p in json.load(f)["countryPools"]}

print("Pools not in available countries:", pools - available)
print("Available countries without pools:", available - pools)
```

Both sets should be empty before shipping a feature that creates per-country entities.

## General rule

Treat `countryId` as a foreign key. Always verify the value exists in `countries.json` before wiring it into a new config. The Python script in `.tmp/run.py` is the right tool for a quick check.

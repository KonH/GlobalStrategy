# Ralph PRD — <feature name here>

<One paragraph: what this batch of work achieves and any constraints.>

## How this file works

- The loop implements the first task with `"passes": false`, verifies it via its `gate`, flips the flag, commits, and repeats.
- Tasks must be **atomic** (one logical change), **verifiable** (the `gate` decides pass/fail — a shell command, or a Unity MCP check: `refresh_unity` + empty error console), and **ordered** (dependencies first).
- When every task has `"passes": true`, the loop stops.

## Tasks

```json
[
	{
		"category": "example",
		"description": "EXAMPLE ONLY — replace with real tasks. Marked passed so an accidental run exits immediately.",
		"steps": [
			"describe the concrete implementation steps",
			"keep each task small enough for one iteration"
		],
		"gate": "dotnet test src/GlobalStrategy.Core.sln",
		"passes": true
	}
]
```

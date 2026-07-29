# Plan: Session-Limit Detection and Commit Salvage

## Goal

Stop missed Claude session/weekly-limit detections (PR #84 class) and prevent `checkout_clean` from wiping mid-run work via salvage + local-ahead push.

## Agent Steps

- [x] **Expand Claude limit detection** — tighter adjacent `hit/reached … (session|usage|weekly) limit` regex; wall-clock `resets` parse; epoch preference.
- [x] **Gate assistant text into detection** — `limit_detection_texts` + `run_claude` collects assistant blocks.
- [x] **Add shared salvage helper** — `salvage_uncommitted_work` with fixed message + `SALVAGE_GIT_IDENTITY`.
- [x] **Add shared limit-pause orchestration** — `handle_limit_pause` (salvage → save → release/needs-attention → best-effort note).
- [x] **Wire Claude main limit path**
- [x] **Wire Codex main limit path** (detector unchanged)
- [x] **Teach `checkout_clean` to push when local is ahead**
- [x] **Update automation SKILL**
- [x] **Extend unit tests** (incl. PR #84 triad + distant-narration negative)

## User Steps

None.

## Constitution Check

No conflicts found — plan aligns with all principles.

Use the implement skill to start working on the plan or request changes.

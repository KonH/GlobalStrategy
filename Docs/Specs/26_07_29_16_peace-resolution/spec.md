# Spec: Peace Resolution

## Feature Intent

As a player (or tester driving the simulation), I want a war that has drifted far enough toward one side to resolve automatically into a peace outcome — transferring some occupied territory, clearing occupation, moving gold, and shifting control — so that wars end with observable conquest and spoils consequences rather than only disappearing via an unconditional debug stop.

## Dependency

Depends on `Docs/Specs/26_07_25_06_war-mechanics-core/` (GitHub #69, closed/implemented): `War` / `WarProgress` / `WarParticipant` (exactly two sides; one war per country), monthly attacker progress decay, and the debug declare/stop commands. Peace resolution is the natural end path for an active war; this feature builds on that model without changing the declare-war rules.

Also assumes the existing separate `ProvinceOwnership` and `ProvinceOccupation` runtime state (`Docs/Specs/26_07_11_10_province-ownership/`, `Docs/Specs/26_07_18_19_province-occupation/`), and org-held gold / country control (`ControlEffect`, shared `maxControlPool` of 100).

## Acceptance Criteria

### Monthly peace chance

- **Given** an active war whose `WarProgress.Value` is strictly inside the open interval `(-100 + MinLose, 100 - MinWin)` **When** a simulation month boundary passes **Then** no peace-resolution roll is made for that war and the war continues (subject only to existing monthly progress decay and any other out-of-scope progress movers).
- **Given** an active war whose progress is exactly at the lose-band edge `progress = -100 + MinLose` (and `MinLose > 0`) **When** a month boundary passes **Then** a peace-resolution chance of **1%** is rolled for that war.
- **Given** an active war whose progress is exactly at the win-band edge `progress = 100 - MinWin` (and `MinWin > 0`) **When** a month boundary passes **Then** a peace-resolution chance of **1%** is rolled for that war.
- **Given** an active war whose progress is at `-100` **When** a month boundary passes **Then** a peace-resolution chance of **100%** is rolled (peace resolution always fires that month).
- **Given** an active war whose progress is at `+100` **When** a month boundary passes **Then** a peace-resolution chance of **100%** is rolled (peace resolution always fires that month).
- **Given** an active war whose progress is inside the lose band (`progress ≤ -100 + MinLose`) but strictly above `-100` **When** a month boundary passes **Then** the monthly peace chance grows **linearly** from 1% at the band edge toward 100% at `-100`, as a function of how far progress has moved from the edge toward `-100`.
- **Given** an active war whose progress is inside the win band (`progress ≥ 100 - MinWin`) but strictly below `+100` **When** a month boundary passes **Then** the monthly peace chance grows **linearly** from 1% at the band edge toward 100% at `+100`, as a function of how far progress has moved from the edge toward `+100`.
- **Given** a peace-chance roll is due for a war on a month boundary **When** the roll fails **Then** the war remains active; no ownership, occupation, gold, or control consequences of peace are applied that month.
- **Given** a peace-chance roll is due for a war on a month boundary **When** the roll succeeds **Then** peace resolution for that war runs fully in that same resolution (winner/loser determination, province transfer, occupation clear, gold transfer, control shifts), and afterward the war no longer exists — both former participants are free to enter a new war.
- **Given** `MinLose`, `MinWin`, and the 1%→100% chance endpoints **When** balance needs tuning **Then** all of those numbers are readable/writable via `game_settings.json` / `GameSettings` without a code change.

### Winner and loser

- **Given** peace resolution fires for a war whose progress is on the attacker-favored side (progress `> 0`, up through `+100`) **When** outcomes are applied **Then** the attacker country is the winner and the defender country is the loser.
- **Given** peace resolution fires for a war whose progress is on the defender-favored side (progress `< 0`, down through `-100`) **When** outcomes are applied **Then** the defender country is the winner and the attacker country is the loser.
- **Given** peace resolution would fire while progress is exactly `0` **When** outcomes are considered **Then** [NEEDS CLARIFICATION: progress `0` is outside both win/lose bands under the stated thresholds, so this should be unreachable — confirm that peace never fires at progress `0`, or define a tie-break if it somehow can].

### Province ownership transfer

- **Given** the loser currently owns one or more provinces that are occupied (occupier is the winner, or otherwise on the winner's side — see Ambiguities) **When** peace resolution runs **Then** a configured fraction in the inclusive range **[PeaceProvinceTransferMin, PeaceProvinceTransferMax]** (defaults corresponding to **10%–30%**) of those occupied-loser provinces change `ProvinceOwnership` to the winner country.
- **Given** several occupied-loser provinces are eligible for transfer **When** the subset to transfer is chosen **Then** provinces are selected preferring those **closer to the winner** first (see Ambiguities for the exact distance definition), filling the transfer count from closest toward farthest.
- **Given** the computed transfer count would be fractional **When** the count is applied **Then** [NEEDS CLARIFICATION: round down, round nearest, or always transfer at least one when any occupied-loser province exists?].
- **Given** the loser has zero occupied provinces at peace time **When** peace resolution runs **Then** no province ownership changes occur from the transfer step, and resolution continues with occupation clear / gold / control as usual.
- **Given** the loser has occupied provinces but the configured transfer percent yields a transfer count of zero after rounding **When** peace resolution runs **Then** no ownership changes occur from the transfer step (occupation clear / gold / control still apply).
- **Given** peace resolution has finished transferring the selected provinces **When** occupation state is cleaned up **Then** **every** province owned by either war participant loses its occupation state (returns to unoccupied), including provinces that were not transferred and provinces that never changed owner.

### Gold spoils

- **Given** a war that has lasted `D` whole months since declaration (see Ambiguities) and a configured per-month gold rate `G` (default **100**) **When** peace resolution runs **Then** a total gold amount of `D × G` is taken from the loser side and passed to the winner side.
- **Given** gold is held by organizations, not by countries **When** the spoils amount is collected from the loser side **Then** it is taken from orgs that hold control in the **loser** country, each contributing in proportion to that org's control share in the loser country; an org may go into **debt** (negative gold) if its share exceeds its current gold.
- **Given** the same spoils amount is being paid out **When** it is distributed to the winner side **Then** it is received by orgs that hold control in the **winner** country, each receiving in proportion to that org's control share in the winner country.
- **Given** one or more orgs hold control in the loser country and one or more hold control in the winner country **When** proportions are computed **Then** each org's share is `orgControlInCountry / totalControlInCountry` for that country, and the sum of contributions (respectively receipts) equals the full `D × G` amount (within ordinary numeric tolerance).
- **Given** the loser country has **no** org with control `> 0` at peace time **When** gold collection runs **Then** [NEEDS CLARIFICATION: is the full amount skipped, treated as uncollectable with winner still unpaid, forced from a designated org, or distributed some other way?].
- **Given** the winner country has **no** org with control `> 0` at peace time **When** gold payout runs **Then** [NEEDS CLARIFICATION: is collected gold discarded, held nowhere, or assigned by a fallback rule?].
- **Given** `G` (gold per month of war) **When** balance needs tuning **Then** it is a `game_settings.json` / `GameSettings` value (default 100).

### Control shifts

- **Given** peace resolution runs for a war **When** control consequences are applied in the **winner** country **Then** control is increased by a configured amount corresponding to **+5%** for each org that holds control there, processed starting with the **top** (highest-control) org and continuing through the remaining orgs in descending control order (see Ambiguities for the exact meaning of "5%" and pool interaction).
- **Given** peace resolution runs for a war **When** control consequences are applied in the **loser** country **Then** control is decreased by a configured amount corresponding to **−10%** for each org that holds control there, processed starting with the **top** (highest-control) org and continuing through the remaining orgs in descending control order (see Ambiguities for the exact meaning of "10%" and floor behavior).
- **Given** an org in the loser country holds less control than the configured decrease would remove **When** its decrease is applied **Then** [NEEDS CLARIFICATION: clamp at 0, allow negative control, or reduce by a smaller residual only?].
- **Given** applying the winner-country increases would push total control in that country past `maxControlPool` (100) **When** the increases are applied **Then** [NEEDS CLARIFICATION: how do +5% boosts interact with the shared pool — truncate per org, truncate remaining pool across orgs in top-first order, or ignore the pool for peace boosts?].
- **Given** the winner or loser country has no orgs with control **When** control shifts run **Then** that country's control step is a no-op (no crash, no invented control rows solely to apply the shift).
- **Given** the +5% / −10% magnitudes (and any related ordering rule) **When** balance needs tuning **Then** those magnitudes are config values on `game_settings.json` / `GameSettings`.

### War lifecycle after peace

- **Given** peace resolution has successfully completed for a war **When** the resolution finishes **Then** the war entity and its participants/progress are removed (same end-of-existence outcome as today's stop, but **with** the ownership / occupation / gold / control consequences above applied first).
- **Given** a country is named in a debug stop-war command **When** that command runs **Then** [NEEDS CLARIFICATION: does debug `StopWar` remain a hard delete with **no** peace consequences, or should it also route through peace resolution?].

### Config surface

- **Given** all numeric tunables named in this feature (band widths `MinLose` / `MinWin`, chance endpoints, province transfer min/max percent, gold-per-month, control +/- magnitudes) **When** a designer opens `Assets/Configs/game_settings.json` (mirrored on `GameSettings`) **Then** every one of those values is present and adjustable without a code change.

## Out of Scope

- Peace triggers driven by **card actions** — separate spec; this feature only covers the monthly progress-threshold chance path.
- Any player-facing UI / HUD / notifications for peace chance, imminent resolution, or the resolution summary (Action Log / fly-text may be deferred; not required by this spec).
- Allies / multi-country wars — still exactly two participants per war; no allied spoils or multi-side occupation rules.
- Natural war declaration, combat-driven progress changes, and any progress movers other than the existing monthly attacker decay (except insofar as whatever sets progress into the win/lose bands makes the chance relevant).
- Re-balancing unrelated systems (scoring, income formulas, occupation visuals) beyond the direct ownership / occupation / gold / control mutations listed above.
- Changing country **relations** (Rival / Friend) as a side effect of peace — not specified by the issue; left to Ambiguities if product wants it later.

## Ambiguities

- [NEEDS CLARIFICATION: Exact meaning of control +5% / −10% per org — absolute percentage points of the 100-point pool (e.g. +5 / −10 control points), a fraction of each org's current control, a fraction of unused pool, or something else?]
- [NEEDS CLARIFICATION: Is the 10–30% province transfer a uniform random draw in `[min, max]` each peace, or a fixed pair of configurable bounds with a separately specified selection rule (always min, always max, random once, etc.)?]
- [NEEDS CLARIFICATION: What does "closer to winner" mean for province selection — distance to winner capital, distance to winner territory centroid, distance to nearest winner-owned province, or another metric?]
- [NEEDS CLARIFICATION: Confirm winner/loser from progress sign — attacker wins when progress is positive / at +100, defender wins when progress is negative / at −100 (assumed above). What if peace somehow fires at exactly 0?]
- [NEEDS CLARIFICATION: Is war duration `D` the count of calendar month boundaries crossed since declaration (same month-boundary notion as decay), or elapsed whole months by date difference, or something else? Does a war that peaces on the same month it was declared yield `D = 0` gold?]
- [NEEDS CLARIFICATION: How is gold handled when zero orgs have control in the loser and/or winner country?]
- [NEEDS CLARIFICATION: How do winner +5% control boosts interact with `maxControlPool` / unused control? How do loser −10% decreases floor (clamp at 0 vs other)?]
- [NEEDS CLARIFICATION: Does debug `StopWar` stay a hard delete with no peace consequences now that peace resolution exists?]
- [NEEDS CLARIFICATION: Do country relations (e.g. Rival) change on peace, or remain untouched?]
- [NEEDS CLARIFICATION: Is the peace chance rolled once per month per war on the month boundary, on the same tick / ordering as attacker progress decay — and if so, is chance evaluated on progress **before** or **after** that month's decay is applied?]
- [NEEDS CLARIFICATION: For "loser provinces which occupied", must the occupier be specifically the winner country, or any non-owner occupier, or any occupier on the winner's side?]
- [NEEDS CLARIFICATION: When transferring a percentage of occupied-loser provinces, how is a fractional count rounded, and is there a minimum of one province when any are eligible?]

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
- **Given** a month boundary where both peace chance and monthly attacker progress decay would apply **When** that month is resolved **Then** the peace-chance roll is evaluated on the war's progress **before** that month's attacker decay is applied.
- **Given** a peace-chance roll is due for a war on a month boundary **When** the roll fails **Then** the war remains active; no ownership, occupation, gold, or control consequences of peace are applied that month.
- **Given** a peace-chance roll is due for a war on a month boundary **When** the roll succeeds **Then** peace resolution for that war runs fully in that same resolution (winner/loser determination, province transfer, occupation clear, gold transfer, control shifts), and afterward the war no longer exists — both former participants are free to enter a new war.
- **Given** `MinLose`, `MinWin`, and the 1%→100% chance endpoints **When** balance needs tuning **Then** all of those numbers are readable/writable via `game_settings.json` / `GameSettings` without a code change.

### Winner and loser

- **Given** peace resolution fires for a war whose progress is on the attacker-favored side (progress `> 0`, up through `+100`) **When** outcomes are applied **Then** the attacker country is the winner and the defender country is the loser.
- **Given** peace resolution fires for a war whose progress is on the defender-favored side (progress `< 0`, down through `-100`) **When** outcomes are applied **Then** the defender country is the winner and the attacker country is the loser.
- **Given** the monthly peace-chance path **When** progress is exactly `0` **Then** peace resolution does not fire: progress `0` lies outside both win/lose bands, so that case is unreachable for the chance path and no tie-break is defined for it.

### Province ownership transfer

- **Given** the loser currently owns one or more provinces that are occupied by **any non-loser country** (not necessarily the winner) **When** peace resolution runs **Then** those provinces are the eligible transfer set.
- **Given** eligible occupied-loser provinces exist **When** the transfer fraction is chosen **Then** a single uniform random draw in the inclusive range `[PeaceProvinceTransferMin, PeaceProvinceTransferMax]` (defaults corresponding to **10%–30%**) is taken for that peace, and that fraction is applied to the eligible count.
- **Given** the computed transfer count is fractional **When** the count is finalized **Then** it is rounded **up** (ceiling). If the ceiling is ≥ 1 and eligible provinces exist, that many provinces are transferred (capped by the eligible count).
- **Given** several occupied-loser provinces are eligible for transfer **When** the subset to transfer is chosen **Then** provinces are selected preferring those **closer to the winner territory centroid** (centroid of winner-owned provinces) first, filling the transfer count from closest toward farthest.
- **Given** the loser has zero eligible occupied provinces at peace time **When** peace resolution runs **Then** no province ownership changes occur from the transfer step, and resolution continues with occupation clear / gold / control as usual.
- **Given** the loser has eligible occupied provinces but the configured transfer percent yields a transfer count of zero after ceiling **When** peace resolution runs **Then** no ownership changes occur from the transfer step (occupation clear / gold / control still apply).
- **Given** peace resolution has finished transferring the selected provinces **When** occupation state is cleaned up **Then** **every** province owned by either war participant loses its occupation state (returns to unoccupied), including provinces that were not transferred and provinces that never changed owner.

### Gold spoils

- **Given** a war whose duration `D` is the count of calendar month boundaries crossed since declaration (same month-boundary notion as decay) and a configured per-month gold rate `G` (default **100**) **When** peace resolution runs **Then** a total gold amount of `D × G` is taken from the loser side and passed to the winner side.
- **Given** a war that peaces in the same calendar month it was declared (zero month boundaries crossed) **When** gold spoils are computed **Then** `D = 0` and no gold is transferred.
- **Given** gold is primarily held by organizations **When** the spoils amount is collected from the loser side **Then** it is taken from orgs that hold control in the **loser** country, each contributing in proportion to that org's control share in the loser country; an org may go into **debt** (negative gold) if its share exceeds its current gold.
- **Given** the same spoils amount is being paid out **When** it is distributed to the winner side **Then** org shares go to orgs that hold control in the **winner** country, each receiving in proportion to that org's control share in the winner country.
- **Given** one or more orgs hold control in the loser (respectively winner) country **When** proportions are computed **Then** each org's share is `orgControlInCountry / totalControlInCountry` for that country, and those proportional shares are attributed to those orgs.
- **Given** after attributing proportional shares to orgs with control `> 0` there is remaining gold (including the full `D × G` when **no** org has control `> 0` in that country) **When** collection or payout completes for that side **Then** the remaining gold is attributed to the **country** (country-held gold / country treasury path). If the codebase has no country gold today, planning must still honor this intended behavior — either by adding a country gold account or by routing remainder through an equivalent country treasury path.
- **Given** `G` (gold per month of war) **When** balance needs tuning **Then** it is a `game_settings.json` / `GameSettings` value (default 100).

### Control shifts

- **Given** peace resolution runs for a war **When** control consequences are applied in the **winner** country **Then** each org that holds control there has its control increased by a configured fraction of **its own current control** corresponding to **+5%** (e.g. an org with 40 control gains `0.05 × 40 = +2`), processed starting with the **top** (highest-control) org and continuing through the remaining orgs in descending control order.
- **Given** peace resolution runs for a war **When** control consequences are applied in the **loser** country **Then** each org that holds control there has its control decreased by a configured fraction of **its own current control** corresponding to **−10%** (e.g. an org with 40 control loses `0.10 × 40 = −4`), processed starting with the **top** (highest-control) org and continuing through the remaining orgs in descending control order.
- **Given** any org's control would fall below 0 or rise above 100 after its shift **When** the shift is applied **Then** the resulting control value is clamped to **`[0, 100]`**.
- **Given** applying winner-country increases would push total control in that country past `maxControlPool` (100) **When** the increases are applied top-first **Then** each boost is applied in descending-control order and clamped so that both individual org control and the country-wide control pool remain within range (no org exceeds 100; total control in the country does not exceed 100).
- **Given** the winner or loser country has no orgs with control **When** control shifts run **Then** that country's control step is a no-op (no crash, no invented control rows solely to apply the shift).
- **Given** the +5% / −10% magnitudes (and any related ordering rule) **When** balance needs tuning **Then** those magnitudes are config values on `game_settings.json` / `GameSettings`.

### War lifecycle after peace

- **Given** peace resolution has successfully completed for a war **When** the resolution finishes **Then** the war entity and its participants/progress are removed (same end-of-existence outcome as today's stop, but **with** the ownership / occupation / gold / control consequences above applied first).
- **Given** a country is named in a debug `StopWar` command **When** that command runs **Then** it routes through **full peace resolution** (same consequences as automatic peace), with winner/loser still determined by progress sign (`> 0` attacker wins, `< 0` defender wins).
- **Given** debug `StopWar` fires while progress is exactly `0` **When** resolution runs **Then** occupation is still cleared for both participants' owned provinces and the war is ended, but winner-dependent province transfer, gold spoils, and control shifts are **skipped** (no winner). This edge case applies to debug `StopWar` only; the monthly chance path cannot fire at progress `0`.

### Relations

- **Given** peace resolution completes (automatic or via debug `StopWar`) **When** the war ends **Then** country relations (Rival / Friend) are **unchanged**.

### Config surface

- **Given** all numeric tunables named in this feature (band widths `MinLose` / `MinWin`, chance endpoints, province transfer min/max percent, gold-per-month, control +/- magnitudes) **When** a designer opens `Assets/Configs/game_settings.json` (mirrored on `GameSettings`) **Then** every one of those values is present and adjustable without a code change.

## Out of Scope

- Peace triggers driven by **card actions** — separate spec; this feature only covers the monthly progress-threshold chance path (and debug `StopWar` routing through that same resolution).
- Any player-facing UI / HUD / notifications for peace chance, imminent resolution, or the resolution summary (Action Log / fly-text may be deferred; not required by this spec).
- Allies / multi-country wars — still exactly two participants per war; no allied spoils or multi-side occupation rules.
- Natural war declaration, combat-driven progress changes, and any progress movers other than the existing monthly attacker decay (except insofar as whatever sets progress into the win/lose bands makes the chance relevant).
- Re-balancing unrelated systems (scoring, income formulas, occupation visuals) beyond the direct ownership / occupation / gold / control mutations listed above.
- Changing country **relations** (Rival / Friend) as a side effect of peace — deliberate decision: relations remain untouched on peace (see Acceptance Criteria).

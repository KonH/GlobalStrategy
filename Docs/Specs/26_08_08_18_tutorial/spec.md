# Spec: Tutorial (Guided Onboarding)

Connected pair: this folder is **part B** of issue #143 (Tasks + tutorial). It builds on the short-term tasks concept shipped in part A (`Docs/Specs/26_08_07_13_short-term-tasks/` — `TasksConfig` / `TaskProgressSystem` / `TriggerCondition` / HUD `PlayerTasksView` / `ActiveTasks`). Part A remains the general tasks pipeline; this spec authors the tutorial content layer on top of it (settings toggle, highlight arrow, pause/time UX, cross-session progress, and the concrete tutorial task list).

## Feature Intent

As a new player, I want a sequential guided tutorial delivered through the existing short-term tasks HUD, with UI highlights, pause-friendly pacing, a default-on Settings toggle, and progress that survives across game sessions, so that I learn camera, HUD panels, country selection, characters, actions, and goals without re-seeing steps I already finished.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition where one applies to several rows.

### Settings — Tutorials checkbox

- The player opens **Main Menu → Settings** (`SettingsWindowDocument` / `Assets/UI/Modal/SettingsWindow/SettingsWindow.uxml`).
  - A **Tutorials** checkbox is present (alongside language / auto-save / data).
  - The checkbox is **enabled by default** when no preference has been stored yet.
- Tutorials are enabled.
  - Tutorial-marked tasks may open under the normal task open rules (subject to mutual-exclusion and open conditions below).
- Tutorials are disabled (player clears the checkbox).
  - No tutorial task opens or re-opens while the preference is off; non-tutorial short-term tasks (if any) are unaffected.
- The player toggles the checkbox.
  - The new value persists across app launches via the existing app-preferences / settings persistence path (Unity: extend `SettingsStorage` / `settings.json`; Web: extend `AppPreferences` / `IPreferencesStore` — same default-on semantics on both clients).
- Settings also exposes two reset actions (Unity Settings window + Web Settings page):
  - **Reset tutorials** — clears the app-level completed-tutorial id set so previously finished tutorial steps can open again on a later session (does not rewrite the current world’s savable `TaskCompleted` mid-session unless a defined session-start reseeding path applies; primary effect is forgetting preference progress).
  - **Reset settings to default** — deletes the entire global settings / preferences store, reloads defaults (Tutorials checkbox on, locale/autosave defaults, empty completed-tutorial set), and refreshes the Settings UI to match.

### Config — tutorial tasks on the tasks pipeline

- The game loads configs at startup / session start.
  - Tutorial steps are authored as entries in `Assets/Configs/tasks_config.json` via the existing `TasksConfig` / `TaskDefinition` pipeline (not a separate parallel task runtime).
  - Each tutorial entry uses the part A fields (`taskId`, `NameKey`, `DescKey`, `openCondition`, `closeCondition`, optional `reward` / `openEffectIds` / `closeEffectIds`) plus tutorial-specific fields:
    - `isTutorial: true` — marks the task as tutorial content (settings gate, mutual exclusion, cross-session progress, auto-expand, highlight eligibility).
    - optional `highlightTargetId` — stable UI highlight target key for the pointing arrow (omit / empty when no highlight).
  - Checked-in config ships the full tutorial list 0–10 below (replacing part A’s empty `{ "tasks": [] }` for this content slice). Non-tutorial gameplay task packs remain out of scope.

### Runtime — mutual exclusion and sequencing

- Exactly one tutorial task may be active at a time.
  - Every tutorial `openCondition` includes a `tutorialTaskActive` expression that is true only when **no** tutorial task currently has `TaskActive` (owner’s `TutorialTaskActive(false)`). Non-tutorial tasks are ignored by this check.
- Tutorial N+1 (indices 1–9) opens only after tutorial N is completed.
  - Default open chain uses a new `taskCompleted` expression (`TaskCompletedCondition(prevTutorialTaskId)`) ANDed with `tutorialTaskActive` (false/none active) and tutorials-enabled.
- Tutorial 0 is the first step.
  - Open when tutorials are enabled, the task is not yet completed, and no tutorial is active (no prior `taskCompleted` prerequisite).
- Tutorial 10 (`tutorial_goals_window`) opens only when tutorials are enabled, no tutorial is active, `tutorial_discard_card` is completed, **and** no other UI chrome is shown (`uiElementShown(none)` / equivalent “chrome clear” fact — selected-country panel, characters/actions slides, goals window, card-draw modal, and other overlay chrome closed/hidden).

### Runtime — cross-session tutorial progress

- The player completes one or more tutorial tasks, then starts a **new** game session (new play / continue into a fresh world — not merely save/load of the same world).
  - Those completed tutorial `taskId`s are already recorded as completed for the new session: they do **not** re-open or re-show in the HUD.
  - Persistence is **app-level** (preferences / settings storage), not only the per-save `[Savable] TaskCompleted` world state from part A. On session start, completed tutorial ids from preferences are seeded into the world’s completed-task set (or otherwise honored before open checks) so `TaskProgressSystem` treats them as already done.
- The player save/loads mid-tutorial within one save.
  - Part A savable `TaskActive` / `TaskCompleted` membership continues to apply; preference store stays in sync when a tutorial completes so a later new session still skips shown steps.

### Runtime — pause / time modifier side change

- The game is paused (`GameTime.IsPaused == true`) and the player clicks a time-speed modifier button (HUD `TimeView` / `ChangeTimeMultiplierCommand` path, including keyboard digit shortcuts that push the same command).
  - The game **resumes** (`UnpauseCommand` or equivalent inside `TimeSystem`) **and** applies the chosen multiplier index in the same interaction. Today `TimeSystem` only updates `MultiplierIndex` on speed change and leaves pause unchanged — that behaviour is updated for this side change.
- When a tutorial task **opens**, if the player was **not** already paused, the game auto-pauses for reading.
  - If the player was already paused, leave pause as-is (do not treat the tutorial as owning pause).
- When that tutorial task **completes** (or is force-completed by disabling Tutorials), if the tutorial owned the pause (opened while unpaused), the game auto-unpauses.
  - The player may resume at any time while a tutorial is active (space / pause button / speed-click-while-paused); do **not** block player resume, and do **not** re-pause after the player has resumed for that step.

### UI — tasks accordion (tutorial exception)

- A tutorial task becomes newly active and appears in `PlayerTasksView` / `ActiveTasks`.
  - That task’s accordion item is **initially expanded** (description visible) without requiring a header click.
- Part A’s accordion rule is otherwise unchanged:
  - While any details are expanded, clicking **any** header only collapses the expanded item (no same-click switch-to-other).
  - This “initially expanded when opened” behaviour applies to **tutorial** tasks only; non-tutorial tasks keep part A’s collapsed-by-default list behaviour.

### UI — highlight arrow

- An active tutorial task has a non-empty `highlightTargetId`.
  - A pointing arrow overlays the HUD/map UI, aimed at the mapped element for that target id, with a continuous back-and-forth motion animation while the task remains active.
- The tutorial completes, is cancelled by settings off mid-flight, or the task leaves the active set.
  - The highlight arrow is removed.
- An active tutorial has no `highlightTargetId`.
  - No arrow is shown for that step.

### Expression / trigger kinds required (new)

These are required so the authored list can evaluate; names below are the product-facing kinds (JSON `type` / trigger ids may be camelCase variants chosen at plan time). Facts may be implemented as dedicated `ExpressionNode` types and/or as values published into `ExpressionContext.Triggers` consumed by existing `triggerCondition` — plan chooses the shape; acceptance requires the behaviours.

| Kind | Meaning |
|---|---|
| `tutorialTaskActive` | 1 if any tutorial (`isTutorial`) task currently has `TaskActive`, else 0. Used as `TutorialTaskActive(false)` via `eq`/`value` or a boolean-style wrapper. |
| `taskCompleted` | 1 if the given `taskId` is in the completed set (world + seeded preference completions). |
| `tutorialsEnabled` | 1 if the Settings tutorials preference is on (optional if the system hard-gates tutorial opens when off). |
| `mapPositionChanged` | Fires once the player has panned/moved the map camera (LMB/RMB drag) since the task became active (or equivalent session edge). |
| `mapZoomChanged` | Fires once the player has changed map zoom (scroll wheel) since the task became active. |
| `keyPressed` | Fires on any keyboard key press while the task is active (`KeyPressedCondition(any)`). |
| `uiOpened` | 1 when the named UI surface is open/visible (e.g. selected country panel, characters slide, actions slide, goals window). |
| `uiElementShown` | 1 when the named UI element is shown. Special case `none` / chrome-clear: 1 only when no other UI chrome is shown (used by tutorial 10 open). |
| `tooltipShown` | 1 when the named tooltip is currently shown (e.g. military advisor role tooltip via `TooltipSystem`). |
| `commandTrigger` | 1 when a matching command type has been processed while the task is active. Tutorials 7–8 bind to the shipped card-draw rework (`DrawCardsCommand` / `ReceiveCardCommand` from issue #153). |

Close condition for tutorial 0 uses **both** map position and map zoom changed (logical AND of the two facts).

### Authored tutorial list (0–10)

Locale: invent stable `taskId`s and English `NameKey` titles; `DescKey` English text is an invented translation matching the given Russian meaning (RU strings below are authoritative for `ru.asset`). Localization entries go through `en.asset` / `ru.asset` via the localization skill at implement time.

Open condition defaults: unless noted, `tutorialsEnabled` ∧ ¬`tutorialTaskActive` ∧ `taskCompleted(previous)`. Close conditions as noted. Highlight as noted.

#### 0 — `tutorial_welcome_camera`

- **NameKey:** `task.tutorial_welcome_camera.name` — EN title: "Welcome"
- **DescKey:** `task.tutorial_welcome_camera.desc`
  - EN: "Welcome! You can move the camera by holding LMB/RMB and change zoom with the mouse wheel."
  - RU: "Добро пожаловать! Вы можете перемещать камеру, зажав ЛКМ/ПКМ и изменять масштаб колесом мыши."
- **Open:** tutorials enabled ∧ ¬tutorialTaskActive ∧ not completed (first step — no prior task).
- **Close:** `mapPositionChanged` ∧ `mapZoomChanged`
- **Highlight:** none

#### 1 — `tutorial_org_resources`

- **NameKey:** `task.tutorial_org_resources.name` — EN title: "Organization resources"
- **DescKey:** `task.tutorial_org_resources.desc`
  - EN: "The organization panel shows your organization's resources: gold and victory points."
  - RU: "На панели организации отображаются ресурсы организации: золото и очки победы."
- **Open:** default after `tutorial_welcome_camera`
- **Close:** `keyPressed` (any)
- **Highlight:** `player_org_panel` → HUD `player-country` / `.player-country-panel` (top-left org panel)

#### 2 — `tutorial_time_controls`

- **NameKey:** `task.tutorial_time_controls.name` — EN title: "Time controls"
- **DescKey:** `task.tutorial_time_controls.desc`
  - EN: "The time panel has controls for the flow of time: play/pause and speed modifiers."
  - RU: "На панели времени есть элементы управления течением времени: старт/пауза и модификаторы скорости."
- **Open:** default after `tutorial_org_resources`
- **Close:** `keyPressed` (any)
- **Highlight:** `time_panel` → HUD `time-panel`

#### 3 — `tutorial_select_country`

- **NameKey:** `task.tutorial_select_country.name` — EN title: "Select a country"
- **DescKey:** `task.tutorial_select_country.desc`
  - EN: "Select any country on the map."
  - RU: "Выберите любое государство на карте."
- **Open:** default after `tutorial_time_controls`
- **Close:** `uiOpened(selectedCountryPanel)` — selected country HUD (`country-info` / `SelectedCountry.IsValid` after `SelectCountryCommand`)
- **Highlight:** none

#### 4 — `tutorial_open_characters`

- **NameKey:** `task.tutorial_open_characters.name` — EN title: "Country panel & characters"
- **DescKey:** `task.tutorial_open_characters.desc`
  - EN: "This panel shows the country's resources and lets you open the character list and action cards. The main country resource for you is control, held by organizations inside that country. Open the character list."
  - RU: "Здесь отображаются ресурсы государства и есть возможность открыть список персонажей и карты действия. Главный ресурс государства для вас - контроль, которым обладают организации внутри этого государства. Откройте список персонажей."
- **Open:** default after `tutorial_select_country`
- **Close:** `uiOpened(characterList)` — characters slide open (`characters-slide--open` / `chars-toggle-btn` path in `CountryInfoView`)
- **Highlight:** `characters_button` → `chars-toggle-btn`

#### 5 — `tutorial_military_advisor_tooltip`

- **NameKey:** `task.tutorial_military_advisor_tooltip.name` — EN title: "Characters & advisors"
- **DescKey:** `task.tutorial_military_advisor_tooltip.desc`
  - EN: "Each character has skills, and action cards can improve relations with them and unlock corresponding options. Hover the cursor over the military advisor."
  - RU: "Каждый персонаж обладает навыками и с помощью карт действий можно улучшать отношения с ними и получать соответствующие возможности. Наведите курсор на военного советника."
- **Open:** default after `tutorial_open_characters`
- **Close:** `tooltipShown(militaryAdvisorTooltip)` — role tooltip for `military_advisor` (`CharactersView` / `TooltipSystem` trigger id pattern `role-military_advisor-…`)
- **Highlight:** `military_advisor_card` — military advisor character card in the characters list

#### 6 — `tutorial_open_actions`

- **NameKey:** `task.tutorial_open_actions.name` — EN title: "Open actions"
- **DescKey:** `task.tutorial_open_actions.desc`
  - EN: "Open the actions panel."
  - RU: "Откройте панель действий."
- **Open:** default after `tutorial_military_advisor_tooltip`
- **Close:** `uiOpened(actionsPanel)` — actions slide open (`actions-slide--open` / `actions-toggle-btn`)
- **Highlight:** `actions_button` → `actions-toggle-btn` (owner confirmed: actions button, not characters).

#### 7 — `tutorial_draw_from_deck`

- **NameKey:** `task.tutorial_draw_from_deck.name` — EN title: "Action deck"
- **DescKey:** `task.tutorial_draw_from_deck.desc`
  - EN: "Here is the action deck. Click it to draw cards."
  - RU: "Здесь расположена колода действий. Нажмите на нее, чтобы получить карты."
- **Open:** default after `tutorial_open_actions`
- **Close:** `commandTrigger(draw)` — fires when `DrawCardsCommand` is processed (card-draw rework from issue #153 / `Docs/Specs/26_08_08_16_card-draw-*`)
- **Highlight:** `action_deck` → country actions deck pile (`CountryActionsView.DeckPileElement` / `.action-deck-wrapper`)

#### 8 — `tutorial_select_drawn_card`

- **NameKey:** `task.tutorial_select_drawn_card.name` — EN title: "Choose a card"
- **DescKey:** `task.tutorial_select_drawn_card.desc`
  - EN: "When you receive action cards, you choose one of three. The card shows its effect, restrictions, cost, and the countries where you can play it. Choose wisely!"
  - RU: "При получении карт действий у вас есть выбор одной из трех карт. На карте отображается ее эффект, ограничения, стоимость и доступные госудаства, в которых можно сыграть эту карту. Выбирайте с умом!"
- **Open:** default after `tutorial_draw_from_deck`
- **Close:** `commandTrigger(cardSelected)` — fires when `ReceiveCardCommand` is processed (choose-one-of-three from the draw offer)
- **Highlight:** none (or draft UI when present)

#### 9 — `tutorial_discard_card`

- **NameKey:** `task.tutorial_discard_card.name` — EN title: "Discard a card"
- **DescKey:** `task.tutorial_discard_card.desc`
  - EN: "If you don't need a card, you can discard it by holding LMB on that card."
  - RU: "Если вам не нужна какая-то карта - ее можно сбросить, зажав ЛКМ на этой карте."
- **Open:** default after `tutorial_select_drawn_card`
- **Close:** `keyPressed` (any)
- **Highlight:** none

#### 10 — `tutorial_goals_window`

- **NameKey:** `task.tutorial_goals_window.name` — EN title: "Victory goals"
- **DescKey:** `task.tutorial_goals_window.desc`
  - EN: "To win, you must complete one of the conditions listed in the goals window."
  - RU: "Чтобы победить, нужно выполнить одно из условий из списка в окне целей."
- **Open:** tutorials enabled ∧ ¬tutorialTaskActive ∧ `taskCompleted(tutorial_discard_card)` ∧ `uiElementShown(none)` (no other UI chrome shown — owner choice **c**).
- **Close:** `uiOpened(goalsWindow)` — goals window UI shown (`GoalsWindowDocument`)
- **Highlight:** `goals_button` → HUD `btn-goals`

### Happy path (end-to-end)

- Tutorials enabled (default). New player starts a game with no prior tutorial completions in preferences.
  - Tutorial 0 opens (auto-pause if the player was unpaused), initially expanded; player pans and zooms → completes (auto-unpause if tutorial owned pause) → 1 opens with org-panel highlight → … → through 10 (opens only when chrome is clear) → goals window open completes the chain; all ids stored in preferences so a later new game skips them.

### Edge cases

- Tutorials disabled before any tutorial opens => no tutorial tasks appear; HUD tasks block stays empty unless non-tutorial tasks exist.
- Tutorials disabled while a tutorial is active => that active tutorial is **force-completed** (marked completed, preference updated, no reward/close effects unless already closing through normal completion); highlight removed; no further tutorials open while disabled.
- Player already completed 0–4 in a previous session => new session starts at tutorial 5 (first not-completed) when tutorials remain enabled.
- Multiple non-tutorial tasks may still be active concurrently per part A; they do not block `tutorialTaskActive` unless marked `isTutorial`.
- Player resumes while a tutorial that auto-paused is still active => stay unpaused for that step; do not re-pause until a later tutorial opens while the player is unpaused again.
- Reset tutorials in Settings => completed tutorial preference set cleared; next new session can re-open from tutorial 0 (subject to tutorials enabled).
- Reset settings to default => full preferences wipe + UI reload to defaults (Tutorials on, empty completed set, default locale/autosave).

## Tech Notes

- **Builds on part A:** Reuse `TaskProgressSystem`, savable `TaskId` / `TaskActive` / `TaskCompleted`, `TaskConditionContext` → `ExpressionContext`, `ActiveTasks` / `VisualStateConverter.UpdateActiveTasks`, and `PlayerTasksView`. Do not fork a second task runtime.
- **Config:** Prefer extending `tasks_config.json` + `TaskDefinition` with `IsTutorial` / `HighlightTargetId` rather than a separate config file. Empty part A skeleton is replaced by the tutorial list for this feature.
- **Expression extensions:** Part A intentionally added only `triggerCondition`. Part B **requires additional expression/trigger kinds** listed above; `TaskConditionContext.Build` (and/or UI→simulation trigger publishers) must populate the facts. Combining AND facts for open/close may use a new `and` node or existing `mul` of 0/1 members — plan chooses.
- **Settings persistence:** Unity main-menu settings today live in `SettingsStorage` (`settings.json` via `IPersistentStorage`) with locale only; in-game `SettingsWindowDocument` pushes locale/autosave commands. Web uses `AppPreferences` (`gs.preferences.*` keys). Tutorials enabled + completed tutorial id set should follow the same **app-level** persistence pattern on both clients (not `GameSettings` / `game_settings.json`, which is shipped config). Reset-tutorials and reset-settings-to-default live on the same Settings surfaces.
- **Tutorial pause ownership:** Track whether the current tutorial step auto-paused because the player was unpaused at open; release that ownership on complete/force-complete (auto-unpause) or when the player resumes early (leave unpaused, do not steal control back).
- **Time resume side change:** Update HUD `OnSpeedChange` / `TimeInputHandler` and/or `TimeSystem.Update` so a speed change while paused also clears `IsPaused`. Keep spacebar pause/unpause toggle behaviour unless it conflicts.
- **Highlight presentation:** UI Toolkit overlay only (Constitution); map highlight targets that aren’t VisualElements need a screen-space anchor from the map camera presentation layer. Animation is presentation-only; which task is active remains ECS/`ActiveTasks` driven.
- **UI element name anchors (current tree):** `player-country`, `time-panel`, `country-info`, `chars-toggle-btn`, `actions-toggle-btn`, `characters-slide`, `actions-slide`, `CountryActionsView.DeckPileElement`, `btn-goals`, `GoalsWindowDocument`, military advisor card via `CharactersView` + role `military_advisor`.
- **EN DescKey copy:** Invented here to match RU meaning so localization can proceed without blocking; owner may edit wording later.
- **RU desc for task 9:** use owner string exactly: `Если вам не нужна какая-то карта - ее можно сбросить, зажав ЛКМ на этой карте.`

## Out of Scope

- Redesigning part A’s core open/close/reward/effect pipeline beyond the tutorial fields and expression/trigger additions above.
- Changing part A’s accordion “any header collapses only” rule except the tutorial **initially expanded when opened** exception.
- Non-tutorial short-term task content packs / sample gameplay tasks beyond this tutorial list.
- Bot / AI consumption of tutorials.
- Multiplayer sync of tutorial progress.
- New reward VFX for tutorial completion (tutorials may ship with empty rewards).
- Redesigning the card-draw choose-one-of-three UX itself (already shipped via issue #153); tutorials 7–8 only bind close conditions to `DrawCardsCommand` / `ReceiveCardCommand`.
- Localization of unrelated existing strings.

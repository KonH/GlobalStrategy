# Plan: Version Label on Main Menu

## Spec

### Feature Intent

As a player or developer, I want to see the current build version displayed on the main menu screen, so that I can quickly identify which version of the game I am running without opening any settings or external tools.

### Acceptance Criteria

- **Given** the main menu scene is loaded **When** the UI renders **Then** a version label is visible in the bottom-right corner of the screen, overlaying the background but not obscuring any menu button.

- **Given** the version label is visible **When** I read it **Then** it displays the text `v{bundleVersion}` where `{bundleVersion}` is the value of `Application.version` at runtime (sourced from `ProjectSettings/ProjectSettings.asset` → `bundleVersion` field).

- **Given** the current `bundleVersion` is `0.01` **When** the label renders **Then** the text reads exactly `v0.01`.

- **Given** the version label element **When** it is rendered **Then** it uses the `.gs-hint` USS class (16 px, lighter brown, italic) so it is visually unobtrusive and consistent with the shared style kit.

- **Given** the version label is positioned in the bottom-right corner **When** the screen resolution changes **Then** the label remains anchored to the bottom-right edge (absolute position, right/bottom offsets) and does not overlap the centred menu panel.

- **Given** the label reads `Application.version` **When** `bundleVersion` in `ProjectSettings.asset` is updated to a new value (e.g. `0.02`) **Then** the next run of the game shows the updated version string without any additional code change.

- **Given** the version label is placed in the UXML **When** inspecting `Assets/UI/Modal/MainMenu/MainMenu.uxml` **Then** the label element is a direct child of `main-menu-root` (the existing absolute full-screen container in the Modal layer), and no separate UIDocument or PanelSettings asset is introduced.

- **Given** a `/implement` command completes **When** the post-tool hook runs **Then** `bundleVersion` in `ProjectSettings/ProjectSettings.asset` is incremented by 0.01 (e.g. `0.01 → 0.02`) and the change is staged for the next commit.

### Out of Scope

- Displaying git commit hash, branch name, or build timestamp alongside the version.
- A version label on any scene other than the main menu.
- Localization of the version string (it is always shown as `v{version}`, language-independent).
- Any clickable behaviour on the version label (e.g. copy-to-clipboard).
- Automated version bumping tied to CI or remote pipelines.

## Goal

Add a static, bottom-right version label to the main menu by wiring `Application.version` into a new UXML label element and auto-bumping `bundleVersion` via a post-implement hook.

## Approach

The label is added directly to `MainMenu.uxml` as a child of the existing `main-menu-root` element, positioned with absolute CSS in `MainMenu.uss`. `MainMenuDocument.cs` queries the label by name in `Start()` and sets its text to `$"v{Application.version}"` — no injection or reactive state needed since the version is static per run. The auto-bump is handled by a PowerShell script (`.claude/bump_version.ps1`) invoked via a `PostToolUse` hook on the `Skill` matcher in `.claude/settings.json`.

## Agent Steps

- [x] **Add label to UXML** — In `Assets/UI/Modal/MainMenu/MainMenu.uxml`, add `<ui:Label name="version-label" class="gs-hint version-label"/>` as the last direct child of `main-menu-root`, after the `menu-panel` element.

- [x] **Add USS rule** — In `Assets/UI/Modal/MainMenu/MainMenu.uss`, add a `.version-label` rule with `position: absolute; right: 12px; bottom: 8px;` to anchor the label to the bottom-right corner.

- [x] **Wire text in MainMenuDocument** — In `Assets/Scripts/Unity/UI/MainMenuDocument.cs`, add a `Label _versionLabel;` field, query it in `Start()` with `root.Q<Label>("version-label")`, and set `_versionLabel.text = $"v{Application.version}";` inside a null-guard (`if (_versionLabel != null)`) immediately after the query.

- [x] **Create bump script** — Write `.claude/bump_version.ps1` that reads `ProjectSettings/ProjectSettings.asset`, finds the `bundleVersion` line via regex, increments the value by 0.01 using integer hundredths arithmetic (parse both parts, add 1 to the total hundredths, reformat as `{0}.{1:D2}`), writes the file back, and runs `git add ProjectSettings/ProjectSettings.asset` to stage the change.

- [x] **Register post-implement hook** — In `.claude/settings.json`, add a `PostToolUse` hook with `matcher: "Skill(skill:implement)"` that runs `powershell -File .claude/bump_version.ps1`.

- [x] **Pre-approve hook shell command** — In `.claude/settings.local.json`, add `"Bash(powershell -File .claude/bump_version.ps1:*)"` to `permissions.allow` so the hook can run non-interactively without a prompt.

## User Steps

### 1. Verify the label in the Unity Editor

Open the Main Menu scene in Play mode and confirm the version label appears in the bottom-right corner displaying `v0.01` (or whatever the current `bundleVersion` is). The label should not overlap the centred menu panel at any supported resolution.

## Constitution Check

No conflicts found — plan aligns with all principles.

- UI Toolkit only: label added via UXML/USS, no Canvas or UGUI involved.
- VContainer: no new registrations needed; `Application.version` is a static Unity API call, not an injected dependency.
- ECS: no game-logic changes; this is purely a presentation concern on a MonoBehaviour.
- One `.asmdef` per feature folder: no new assembly; the change lives inside the existing `GS.Unity.UI` assembly.
- C# style: new field and assignment follow tab indentation, `_` prefix, braces-always conventions.

Use /implement to start working on the plan or request changes.

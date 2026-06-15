# Spec: Version Label on Main Menu

## Feature Intent

As a player or developer, I want to see the current build version displayed on the main menu screen, so that I can quickly identify which version of the game I am running without opening any settings or external tools.

## Acceptance Criteria

- **Given** the main menu scene is loaded **When** the UI renders **Then** a version label is visible in the bottom-right corner of the screen, overlaying the background but not obscuring any menu button.

- **Given** the version label is visible **When** I read it **Then** it displays the text `v{bundleVersion}` where `{bundleVersion}` is the value of `Application.version` at runtime (sourced from `ProjectSettings/ProjectSettings.asset` → `bundleVersion` field).

- **Given** the current `bundleVersion` is `0.01` **When** the label renders **Then** the text reads exactly `v0.01`.

- **Given** the version label element **When** it is rendered **Then** it uses the `.gs-hint` USS class (16 px, lighter brown, italic) so it is visually unobtrusive and consistent with the shared style kit.

- **Given** the version label is positioned in the bottom-right corner **When** the screen resolution changes **Then** the label remains anchored to the bottom-right edge (absolute position, right/bottom offsets) and does not overlap the centred menu panel.

- **Given** the label reads `Application.version` **When** `bundleVersion` in `ProjectSettings.asset` is updated to a new value (e.g. `0.02`) **Then** the next run of the game shows the updated version string without any additional code change.

- **Given** the version label is placed in the UXML **When** inspecting `Assets/UI/Modal/MainMenu/MainMenu.uxml` **Then** the label element is a direct child of `main-menu-root` (the existing absolute full-screen container in the Modal layer), and no separate UIDocument or PanelSettings asset is introduced.

- **Given** a `/implement` command completes **When** the post-tool hook runs **Then** `bundleVersion` in `ProjectSettings/ProjectSettings.asset` is incremented by 0.01 (e.g. `0.01 → 0.02`) and the change is staged for the next commit.

## Out of Scope

- Displaying git commit hash, branch name, or build timestamp alongside the version.
- A version label on any scene other than the main menu.
- Localization of the version string (it is always shown as `v{version}`, language-independent).
- Any clickable behaviour on the version label (e.g. copy-to-clipboard).
- Automated version bumping tied to CI or remote pipelines.

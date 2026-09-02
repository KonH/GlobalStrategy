using System.Collections.Generic;
using GS.Main;
using GS.Unity.Common;
using GS.Unity.Map;
using GS.Unity.UI;
using UnityEngine.UIElements;

namespace GS.Unity.Gallery {
	/// <summary>
	/// Gallery blocks for "the six view-less documents" (Docs/Specs/26_08_28_16_ui-refactoring
	/// phase 7): MainMenuDocument, GameMenuDocument, SettingsWindowDocument, LoadWindowDocument,
	/// SelectOrgDocument. (OrgInfoDocument's block, OrgInfoGalleryBlock, already landed in the
	/// "Hand/deck and animation blocks" batch, in HandDeckGalleryBlocks.cs.) Same approach as the
	/// other window blocks: clone the real named root out of its own UXML via
	/// HudGalleryPreview.CloneNamed, construct the plain view class the real document now uses, and
	/// feed it hand-built sample data — no running game, no ECS world, no save.
	/// </summary>
	public class MainMenuGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Sample menu" };
		static readonly List<string> _states = new List<string> { "No saves", "Has saves" };

		readonly ILocalization _loc;
		readonly VisualTreeAsset _mainMenuUxml;

		public override string Id => "window-main-menu";
		public override string Title => "MainMenuDocument: Overlay";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;
		protected override bool IsFullSurface => true;

		public MainMenuGalleryBlock(ILocalization loc, VisualTreeAsset mainMenuUxml) {
			_loc = loc;
			_mainMenuUxml = mainMenuUxml;
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			VisualElement root = HudGalleryPreview.CloneNamed(_mainMenuUxml, "main-menu-root", resetToRelative: false);
			if (root == null) {
				return;
			}
			var view = new MainMenuView(root);
			view.SetVersion("Gallery Preview", "v0.00");
			view.RefreshTexts(_loc);
			view.Refresh(hasSaves: stateIndex == 1);
			stage.Add(root);
		}
	}

	public class GameMenuGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Sample menu" };
		static readonly List<string> _states = new List<string> { "Default" };

		readonly ILocalization _loc;
		readonly VisualTreeAsset _gameMenuUxml;

		public override string Id => "window-game-menu";
		public override string Title => "GameMenuDocument: Overlay";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;
		protected override bool IsFullSurface => true;

		public GameMenuGalleryBlock(ILocalization loc, VisualTreeAsset gameMenuUxml) {
			_loc = loc;
			_gameMenuUxml = gameMenuUxml;
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			VisualElement root = HudGalleryPreview.CloneNamed(_gameMenuUxml, "game-menu-root", resetToRelative: false);
			if (root == null) {
				return;
			}
			var view = new GameMenuView(root);
			view.RefreshTexts(_loc);
			stage.Add(root);
		}
	}

	public class SettingsWindowGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Sample settings" };
		static readonly List<string> _states = new List<string> { "EN / Daily / Tutorials on", "RU / Monthly / Tutorials off" };

		readonly ILocalization _loc;
		readonly VisualTreeAsset _settingsWindowUxml;

		public override string Id => "window-settings";
		public override string Title => "SettingsWindowDocument: Overlay";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;
		protected override bool IsFullSurface => true;

		public SettingsWindowGalleryBlock(ILocalization loc, VisualTreeAsset settingsWindowUxml) {
			_loc = loc;
			_settingsWindowUxml = settingsWindowUxml;
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			VisualElement root = HudGalleryPreview.CloneNamed(_settingsWindowUxml, "settings-window-root", resetToRelative: false);
			if (root == null) {
				return;
			}
			var view = new SettingsWindowView(root);
			view.RefreshTexts(_loc);
			var state = stateIndex == 1
				? new SettingsWindowViewState { CurrentLocale = "ru", CurrentInterval = GS.Game.Components.AutoSaveInterval.Monthly, TutorialsEnabled = false }
				: new SettingsWindowViewState { CurrentLocale = "en", CurrentInterval = GS.Game.Components.AutoSaveInterval.Daily, TutorialsEnabled = true };
			view.Refresh(state);
			stage.Add(root);
		}
	}

	public class LoadWindowGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Sample saves" };
		static readonly List<string> _states = new List<string> { "Some saves", "No saves" };

		readonly ILocalization _loc;
		readonly VisualTreeAsset _loadWindowUxml;

		public override string Id => "window-load";
		public override string Title => "LoadWindowDocument: Overlay";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;
		protected override bool IsFullSurface => true;

		public LoadWindowGalleryBlock(ILocalization loc, VisualTreeAsset loadWindowUxml) {
			_loc = loc;
			_loadWindowUxml = loadWindowUxml;
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			VisualElement root = HudGalleryPreview.CloneNamed(_loadWindowUxml, "load-window-root", resetToRelative: false);
			if (root == null) {
				return;
			}
			var view = new LoadWindowView(root, _loc, onLoad: null, onDelete: null);
			view.RefreshTexts();
			var saves = new List<SaveFileInfo>();
			if (stateIndex == 0) {
				saves.Add(new SaveFileInfo { SaveName = "sample_1", OrganizationId = "player_org", GameDate = new System.DateTime(1885, 3, 1), SavedAt = System.DateTime.UtcNow });
				saves.Add(new SaveFileInfo { SaveName = "sample_2", OrganizationId = "rival_org", GameDate = new System.DateTime(1890, 7, 12), SavedAt = System.DateTime.UtcNow });
			}
			view.Refresh(saves);
			stage.Add(root);
		}
	}

	public class SelectOrgGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Sample org" };
		static readonly List<string> _states = new List<string> { "Selected", "Not selected" };

		readonly ILocalization _loc;
		readonly VisualTreeAsset _selectCountryUxml;
		readonly OrgVisualConfig _orgVisualConfig;

		public override string Id => "window-select-org";
		public override string Title => "SelectOrgDocument: Overlay";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;
		protected override bool IsFullSurface => true;

		public SelectOrgGalleryBlock(ILocalization loc, VisualTreeAsset selectCountryUxml, OrgVisualConfig orgVisualConfig) {
			_loc = loc;
			_selectCountryUxml = selectCountryUxml;
			_orgVisualConfig = orgVisualConfig;
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			VisualElement root = HudGalleryPreview.CloneNamed(_selectCountryUxml, "select-country-root", resetToRelative: false);
			if (root == null) {
				return;
			}
			var view = new SelectOrgView(root, _loc);
			view.RefreshTexts();

			bool selected = stateIndex == 0;
			string orgId = _orgVisualConfig != null && _orgVisualConfig.Entries.Count > 0 ? _orgVisualConfig.Entries[0].orgId : "player_org";
			string displayName = _loc?.Get($"organization_name.{orgId}") ?? orgId;

			var state = new SelectedOrganizationState();
			state.Set(selected, orgId, displayName, 500);
			view.Refresh(state, _orgVisualConfig, baseControl: 45, estimatedIncome: 12.5);

			var hint = new WinConditionHintState();
			hint.Set(
				selected,
				isAlternativeGroup: true,
				rows: new List<WinConditionHintRowState> {
					new WinConditionHintRowState(WinConditionHintKind.TotalControl, 0.5, 154),
					new WinConditionHintRowState(WinConditionHintKind.ScoreGoal, 5000, 0),
				});
			view.RefreshGoalHint(hint);
			stage.Add(root);
		}
	}
}

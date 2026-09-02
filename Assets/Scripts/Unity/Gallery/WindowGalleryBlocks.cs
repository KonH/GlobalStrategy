using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using GS.Game.Configs;
using GS.Main;
using GS.Unity.Common;
using GS.Unity.Map;
using GS.Unity.UI;

namespace GS.Unity.Gallery {
	/// <summary>
	/// Gallery blocks for "the seven windows that already have a view" (Docs/Specs/26_08_28_16_ui-refactoring
	/// phase 7): LeaderboardWindow, GoalsWindow, WarProgressWindow (+ its WarProgressLayout subtree,
	/// authored inline in both WarProgressWindow.uxml and WarResultWindow.uxml per the plan's spec-correction
	/// note), WarResultWindow, EndGameWindow, CountryDestroyedWindow, OrgDestroyedWindow. Each clones its own
	/// window's full root out of its own UXML via HudGalleryPreview.CloneNamed - the same helper the HUD panel
	/// blocks use, since these window roots are the same shape of "absolutely-positioned full-screen overlay
	/// that needs position:Relative to preview inside a small gallery-stage" problem HudGalleryPreview already
	/// solves - and constructs the same document-less View class the real window Document builds, fed
	/// hand-built VisualState substates from HudSampleData. No running game, no ECS world, no save.
	///
	/// Per the coordinator's phase-6 status correction: the PanelRenderer go/no-go (User Step 4) is still
	/// pending, not passed, and no panelrenderer-findings.md exists. These windows are therefore left on
	/// UIDocument exactly as-is - only their Gallery blocks land in this batch, matching batches 1 and 2.
	/// </summary>
	public class LeaderboardWindowGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Sample leaderboard" };
		static readonly List<string> _states = new List<string> { "Default" };

		readonly ILocalization _loc;
		readonly VisualTreeAsset _windowUxml;
		readonly CountryVisualConfig _countryVisualConfig;
		readonly OrgVisualConfig _orgVisualConfig;
		readonly CountryConfig _countryConfig;

		public override string Id => "window-leaderboard";
		public override string Title => "Window: Leaderboard";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;
		protected override bool IsFullSurface => true;

		public LeaderboardWindowGalleryBlock(
			ILocalization loc, VisualTreeAsset windowUxml, CountryVisualConfig countryVisualConfig,
			OrgVisualConfig orgVisualConfig, CountryConfig countryConfig) {
			_loc = loc;
			_windowUxml = windowUxml;
			_countryVisualConfig = countryVisualConfig;
			_orgVisualConfig = orgVisualConfig;
			_countryConfig = countryConfig;
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			VisualElement root = HudGalleryPreview.CloneNamed(_windowUxml, "leaderboard-root", resetToRelative: false);
			if (root == null) {
				return;
			}
			var view = new LeaderboardWindowView(root, _loc, _countryVisualConfig, _orgVisualConfig);
			view.ResetToDefaultTab();
			view.Refresh(HudSampleData.BuildLeaderboardState(_loc, _orgVisualConfig, _countryVisualConfig, _countryConfig));
			stage.Add(root);
		}
	}

	public class GoalsWindowGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Sample goals" };
		static readonly List<string> _states = new List<string> { "Default" };

		readonly ILocalization _loc;
		readonly VisualTreeAsset _windowUxml;
		readonly OrgVisualConfig _orgVisualConfig;

		public override string Id => "window-goals";
		public override string Title => "Window: Goals";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;
		protected override bool IsFullSurface => true;

		public GoalsWindowGalleryBlock(ILocalization loc, VisualTreeAsset windowUxml, OrgVisualConfig orgVisualConfig) {
			_loc = loc;
			_windowUxml = windowUxml;
			_orgVisualConfig = orgVisualConfig;
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			VisualElement root = HudGalleryPreview.CloneNamed(_windowUxml, "goals-root", resetToRelative: false);
			if (root == null) {
				return;
			}
			string playerOrgId = _orgVisualConfig != null && _orgVisualConfig.Entries.Count > 0 ? _orgVisualConfig.Entries[0].orgId : "";
			var view = new GoalsWindowView(root, _loc, _orgVisualConfig);
			view.ResetToPlayerOrg(playerOrgId);
			view.Refresh(HudSampleData.BuildLeaderboardState(_loc, _orgVisualConfig, null), HudSampleData.BuildGoalsState(_orgVisualConfig));
			stage.Add(root);
		}
	}

	public class WarProgressWindowGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Sample war" };
		static readonly List<string> _states = new List<string> { "Attacker leading", "Defender leading", "No battles yet" };

		readonly ILocalization _loc;
		readonly VisualTreeAsset _windowUxml;
		readonly CountryVisualConfig _countryVisualConfig;
		readonly string _attackerCountryId;
		readonly string _defenderCountryId;

		public override string Id => "window-war-progress";
		public override string Title => "Window: WarProgress";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;
		protected override bool IsFullSurface => true;

		public WarProgressWindowGalleryBlock(ILocalization loc, VisualTreeAsset windowUxml, CountryVisualConfig countryVisualConfig) {
			_loc = loc;
			_windowUxml = windowUxml;
			_countryVisualConfig = countryVisualConfig;
			_attackerCountryId = "Afghanistan";
			_defenderCountryId = "Germany";
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			VisualElement root = HudGalleryPreview.CloneNamed(_windowUxml, "war-progress-root", resetToRelative: false);
			if (root == null) {
				return;
			}
			var tooltip = new TooltipSystem(root);
			var view = new WarProgressWindowView(root, _loc, _countryVisualConfig, null, tooltip);
			view.RefreshStaticTexts((key, fallback) => GetText(key, fallback));
			view.Refresh(HudSampleData.BuildSelectedWarState(_attackerCountryId, _defenderCountryId, stateIndex));
			stage.Add(root);
		}

		string GetText(string key, string fallback) {
			string value = _loc?.Get(key) ?? "";
			return string.IsNullOrEmpty(value) || value == key ? fallback : value;
		}
	}

	public class WarResultWindowGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Sample result" };
		static readonly List<string> _states = new List<string> { "Attacker won", "Defender won" };

		readonly ILocalization _loc;
		readonly VisualTreeAsset _windowUxml;
		readonly CountryVisualConfig _countryVisualConfig;
		readonly string _attackerCountryId;
		readonly string _defenderCountryId;

		public override string Id => "window-war-result";
		public override string Title => "Window: WarResult";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;
		protected override bool IsFullSurface => true;

		public WarResultWindowGalleryBlock(ILocalization loc, VisualTreeAsset windowUxml, CountryVisualConfig countryVisualConfig) {
			_loc = loc;
			_windowUxml = windowUxml;
			_countryVisualConfig = countryVisualConfig;
			_attackerCountryId = "Afghanistan";
			_defenderCountryId = "Germany";
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			VisualElement root = HudGalleryPreview.CloneNamed(_windowUxml, "war-result-root", resetToRelative: false);
			if (root == null) {
				return;
			}
			var tooltip = new TooltipSystem(root);
			var view = new WarResultWindowView(root, _loc, _countryVisualConfig, null, tooltip);
			view.RefreshStaticTexts((key, fallback) => GetText(key, fallback));
			view.Refresh(HudSampleData.BuildWarResultSnapshot(_attackerCountryId, _defenderCountryId, attackerWon: stateIndex == 0));
			stage.Add(root);
		}

		string GetText(string key, string fallback) {
			string value = _loc?.Get(key) ?? "";
			return string.IsNullOrEmpty(value) || value == key ? fallback : value;
		}
	}

	public class EndGameWindowGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Sample game" };
		static readonly List<string> _states = new List<string> { "Win", "Lose (winner known)", "Lose (destroyed, no winner)" };

		readonly ILocalization _loc;
		readonly VisualTreeAsset _windowUxml;
		readonly OrgVisualConfig _orgVisualConfig;

		public override string Id => "window-end-game";
		public override string Title => "Window: EndGame";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;
		protected override bool IsFullSurface => true;

		public EndGameWindowGalleryBlock(ILocalization loc, VisualTreeAsset windowUxml, OrgVisualConfig orgVisualConfig) {
			_loc = loc;
			_windowUxml = windowUxml;
			_orgVisualConfig = orgVisualConfig;
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			VisualElement root = HudGalleryPreview.CloneNamed(_windowUxml, "end-game-root", resetToRelative: false);
			if (root == null) {
				return;
			}
			string playerOrgId = _orgVisualConfig != null && _orgVisualConfig.Entries.Count > 0 ? _orgVisualConfig.Entries[0].orgId : "player_org";
			string playerDisplayName = _loc?.Get($"organization_name.{playerOrgId}") ?? playerOrgId;
			string rivalOrgId = _orgVisualConfig != null && _orgVisualConfig.Entries.Count > 1 ? _orgVisualConfig.Entries[1].orgId : "";

			var view = new EndGameWindowView(root, _loc, _orgVisualConfig);
			LeaderboardState leaderboard = HudSampleData.BuildLeaderboardState(_loc, _orgVisualConfig, null);
			PlayerOrganizationState player = HudSampleData.BuildPlayerOrganization(playerOrgId, playerDisplayName, "", null);

			GameCompletionState completion = new GameCompletionState();
			switch (stateIndex) {
				case 0:
					completion.Set(true, playerOrgId, GameResult.Win);
					break;
				case 1:
					completion.Set(true, rivalOrgId, GameResult.Lose);
					break;
				default:
					completion.Set(true, "", GameResult.Lose);
					break;
			}

			view.Refresh(completion, leaderboard, player, HudSampleData.BuildEndGameComparisons());
			stage.Add(root);
		}
	}

	public class CountryDestroyedWindowGalleryBlock : GalleryBlockBase {
		static readonly List<string> _states = new List<string> { "Default" };

		readonly ILocalization _loc;
		readonly VisualTreeAsset _windowUxml;
		readonly List<string> _countryIds = new();

		public override string Id => "window-country-destroyed";
		public override string Title => "Window: CountryDestroyed";
		protected override IReadOnlyList<string> InstanceChoices => _countryIds;
		protected override IReadOnlyList<string> StateChoices => _states;
		protected override bool IsFullSurface => true;
		protected override string InstanceLabel => "Country";

		public CountryDestroyedWindowGalleryBlock(ILocalization loc, VisualTreeAsset windowUxml, CountryVisualConfig countryVisualConfig, CountryConfig countryConfig) {
			_loc = loc;
			_windowUxml = windowUxml;
			if (countryVisualConfig != null) {
				foreach (CountryVisualEntry entry in countryVisualConfig.Entries) {
					if (!HudConfigLoader.IsCountryAvailable(countryConfig, entry.countryId)) {
						continue;
					}
					_countryIds.Add(entry.countryId);
					if (_countryIds.Count >= 6) {
						break;
					}
				}
			}
		}

		protected override void Render(VisualElement stage, string countryId, int stateIndex) {
			VisualElement root = HudGalleryPreview.CloneNamed(_windowUxml, "country-destroyed-root", resetToRelative: false);
			if (root == null) {
				return;
			}
			var view = new CountryDestroyedWindowView(root, _loc);
			view.RefreshStaticTexts((key, fallback) => GetText(key, fallback));
			view.Refresh(new CountryDestroyedSnapshotState(countryId));
			stage.Add(root);
		}

		string GetText(string key, string fallback) {
			string value = _loc?.Get(key) ?? "";
			return string.IsNullOrEmpty(value) || value == key ? fallback : value;
		}
	}

	public class OrgDestroyedWindowGalleryBlock : GalleryBlockBase {
		static readonly List<string> _states = new List<string> { "Default" };

		readonly ILocalization _loc;
		readonly VisualTreeAsset _windowUxml;
		readonly List<string> _orgIds = new();

		public override string Id => "window-org-destroyed";
		public override string Title => "Window: OrgDestroyed";
		protected override IReadOnlyList<string> InstanceChoices => _orgIds;
		protected override IReadOnlyList<string> StateChoices => _states;
		protected override bool IsFullSurface => true;
		protected override string InstanceLabel => "Org";

		public OrgDestroyedWindowGalleryBlock(ILocalization loc, VisualTreeAsset windowUxml, OrgVisualConfig orgVisualConfig) {
			_loc = loc;
			_windowUxml = windowUxml;
			if (orgVisualConfig != null) {
				foreach (OrgVisualEntry entry in orgVisualConfig.Entries) {
					_orgIds.Add(entry.orgId);
				}
			}
		}

		protected override void Render(VisualElement stage, string orgId, int stateIndex) {
			VisualElement root = HudGalleryPreview.CloneNamed(_windowUxml, "org-destroyed-root", resetToRelative: false);
			if (root == null) {
				return;
			}
			var view = new OrgDestroyedWindowView(root, _loc);
			view.RefreshStaticTexts((key, fallback) => GetText(key, fallback));
			view.Refresh(new OrgDestroyedSnapshotState(orgId));
			stage.Add(root);
		}

		string GetText(string key, string fallback) {
			string value = _loc?.Get(key) ?? "";
			return string.IsNullOrEmpty(value) || value == key ? fallback : value;
		}
	}
}

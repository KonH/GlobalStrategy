using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UIElements;
using GS.Game.Common;
using GS.Game.Configs;
using GS.Unity.Common;
using GS.Unity.Map;
using GS.Unity.UI;

namespace GS.Unity.Gallery {
	/// <summary>
	/// HUD panel gallery blocks (Docs/Specs/26_08_28_16_ui-refactoring phase 7, "HUD panel Gallery
	/// blocks" batch). Each clones its panel's real root out of HUD.uxml via HudGalleryPreview,
	/// constructs the same binding/view classes HUDDocument uses, and feeds them hand-built
	/// VisualState substates from HudSampleData - no running game, no ECS world, no save.
	/// TooltipController itself is an overlay-positioning class, not a content view - its content
	/// shape is already covered by the Phase 3 TooltipBodyGalleryBlock (TooltipBodyBuilder), so it
	/// is not duplicated here.
	/// </summary>
	static class HudConfigLoader {
		public static ResourceConfig LoadResourceConfig(TextAsset asset) {
			return asset != null ? JsonConvert.DeserializeObject<ResourceConfig>(asset.text) : null;
		}

		public static CharacterConfig LoadCharacterConfig(TextAsset asset) {
			return asset != null ? JsonConvert.DeserializeObject<CharacterConfig>(asset.text) : null;
		}

		public static ActionConfig LoadActionConfig(TextAsset asset) {
			return asset != null ? JsonConvert.DeserializeObject<ActionConfig>(asset.text) : null;
		}

		public static CountryConfig LoadCountryConfig(TextAsset asset) {
			return asset != null ? JsonConvert.DeserializeObject<CountryConfig>(asset.text) : null;
		}

		/// <summary>
		/// Matches what the real game shows/uses: only CountryConfig.Countries[].IsAvailable == true
		/// countries (VisualStateConverter.cs:726, InitSystem.cs). CountryVisualConfig only carries
		/// flag/visual metadata, no availability flag, so every Gallery block that lists country ids
		/// for its instance dropdown filters through this. A null config (not wired) fails open -
		/// no filtering - rather than silently emptying every country dropdown in the Gallery; a
		/// missing/not-found entry fails closed (unavailable), per the coordinator's instruction.
		/// </summary>
		public static bool IsCountryAvailable(CountryConfig config, string countryId) {
			if (config == null) {
				return true;
			}
			CountryEntry entry = config.FindByCountryId(countryId);
			return entry != null && entry.IsAvailable;
		}
	}

	public class CountryInfoGalleryBlock : GalleryBlockBase {
		static readonly List<string> _states = new List<string> { "Default" };

		readonly ILocalization _loc;
		readonly VisualTreeAsset _hudUxml;
		readonly ResourceConfig _resourceConfig;
		readonly CharacterConfig _characterConfig;
		readonly ActionConfig _actionConfig;
		readonly ActionVisualConfig _actionVisualConfig;
		readonly CharacterVisualConfig _characterVisualConfig;
		readonly CountryVisualConfig _countryVisualConfig;
		readonly OrgVisualConfig _orgVisualConfig;
		readonly List<string> _countryIds = new();

		public override string Id => "hud-country-info";
		public override string Title => "HUD: CountryInfo";
		protected override IReadOnlyList<string> InstanceChoices => _countryIds;
		protected override IReadOnlyList<string> StateChoices => _states;
		protected override string InstanceLabel => "Country";
		protected override bool IsFullSurface => true;

		public CountryInfoGalleryBlock(
			ILocalization loc, VisualTreeAsset hudUxml, TextAsset resourceConfigAsset, TextAsset characterConfigAsset,
			TextAsset actionConfigAsset, ActionVisualConfig actionVisualConfig, CharacterVisualConfig characterVisualConfig,
			CountryVisualConfig countryVisualConfig, OrgVisualConfig orgVisualConfig, CountryConfig countryConfig) {
			_loc = loc;
			_hudUxml = hudUxml;
			_resourceConfig = HudConfigLoader.LoadResourceConfig(resourceConfigAsset);
			_characterConfig = HudConfigLoader.LoadCharacterConfig(characterConfigAsset);
			_actionConfig = HudConfigLoader.LoadActionConfig(actionConfigAsset);
			_actionVisualConfig = actionVisualConfig;
			_characterVisualConfig = characterVisualConfig;
			_countryVisualConfig = countryVisualConfig;
			_orgVisualConfig = orgVisualConfig;
			if (_countryVisualConfig != null) {
				foreach (CountryVisualEntry entry in _countryVisualConfig.Entries) {
					if (!HudConfigLoader.IsCountryAvailable(countryConfig, entry.countryId)) {
						continue;
					}
					_countryIds.Add(entry.countryId);
					if (_countryIds.Count >= 6) { break; }
				}
			}
		}

		protected override void Render(VisualElement stage, string countryId, int stateIndex) {
			VisualElement root = HudGalleryPreview.CloneNamed(_hudUxml, "country-info", resetToRelative: false);
			if (root == null || _resourceConfig == null || _actionConfig == null || _characterConfig == null) {
				return;
			}
			var tooltip = new TooltipSystem(root);
			var view = new CountryInfoView(
				root, _loc, _resourceConfig, _characterConfig, tooltip, _characterVisualConfig,
				_actionConfig, _actionVisualConfig, _countryVisualConfig, _orgVisualConfig);

			string dominantOrgId = "player_org";
			string dominantOrgName = "Freedonia Council";
			if (_orgVisualConfig != null && _orgVisualConfig.Entries.Count > 0) {
				dominantOrgId = _orgVisualConfig.Entries[0].orgId;
			}

			var friends = new List<string>();
			var rivals = new List<string>();
			var wars = new List<string>();
			for (int i = 0; i < _countryIds.Count; i++) {
				if (_countryIds[i] == countryId) { continue; }
				if (friends.Count == 0) { friends.Add(_countryIds[i]); continue; }
				if (rivals.Count == 0) { rivals.Add(_countryIds[i]); continue; }
				if (wars.Count == 0) { wars.Add(_countryIds[i]); break; }
			}

			var selected = HudSampleData.BuildSelectedCountry(countryId, _resourceConfig, friends, rivals, wars, dominantOrgId, dominantOrgName);
			var playerResources = HudSampleData.BuildResources(_resourceConfig, "player_org", 500);
			view.Refresh(selected, selected.Resources, selected.Control, selected.Characters, selected.CountryActions, playerResources);
			stage.Add(root);
		}
	}

	public class ProvinceInfoGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Owned only", "Occupied" };
		static readonly List<string> _states = new List<string> { "Default" };
		const string SampleProvinceId = "Afghanistan__Andkhvoy";

		readonly ILocalization _loc;
		readonly VisualTreeAsset _hudUxml;
		readonly ResourceConfig _resourceConfig;
		readonly CountryVisualConfig _countryVisualConfig;
		readonly string _ownerCountryId;
		readonly string _occupierCountryId;

		public override string Id => "hud-province-info";
		public override string Title => "HUD: ProvinceInfo";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;
		protected override bool IsFullSurface => true;

		public ProvinceInfoGalleryBlock(ILocalization loc, VisualTreeAsset hudUxml, TextAsset resourceConfigAsset, CountryVisualConfig countryVisualConfig) {
			_loc = loc;
			_hudUxml = hudUxml;
			_resourceConfig = HudConfigLoader.LoadResourceConfig(resourceConfigAsset);
			_countryVisualConfig = countryVisualConfig;
			_ownerCountryId = "Afghanistan";
			_occupierCountryId = "Germany";
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			VisualElement root = HudGalleryPreview.CloneNamed(_hudUxml, "province-info", resetToRelative: false);
			if (root == null || _resourceConfig == null) {
				return;
			}
			var tooltip = new TooltipSystem(root);
			var view = new ProvinceInfoView(root, _loc, _resourceConfig, tooltip, _countryVisualConfig);
			bool occupied = instanceId == "Occupied";
			var resources = HudSampleData.BuildResources(_resourceConfig, SampleProvinceId, 30);
			view.Refresh(true, SampleProvinceId, _ownerCountryId, occupied ? _occupierCountryId : "", resources);
			stage.Add(root);
		}
	}

	public class HudResourcesGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Sample resources" };
		static readonly List<string> _states = new List<string> { "Default" };

		readonly ILocalization _loc;
		readonly ResourceConfig _resourceConfig;

		public override string Id => "hud-resources";
		public override string Title => "HUD: Resources";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;

		public HudResourcesGalleryBlock(ILocalization loc, TextAsset resourceConfigAsset) {
			_loc = loc;
			_resourceConfig = HudConfigLoader.LoadResourceConfig(resourceConfigAsset);
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			if (_resourceConfig == null) {
				return;
			}
			var container = new VisualElement();
			container.AddToClassList("resources-container");
			var tooltip = new TooltipSystem(container);
			var view = new ResourcesView(container, _loc, _resourceConfig, tooltip);
			view.Refresh(HudSampleData.BuildResources(_resourceConfig));
			stage.Add(container);
		}
	}

	public class HudPlayerOrgGalleryBlock : GalleryBlockBase {
		static readonly List<string> _states = new List<string> { "Default" };

		readonly ILocalization _loc;
		readonly VisualTreeAsset _hudUxml;
		readonly ResourceConfig _resourceConfig;
		readonly OrgVisualConfig _orgVisualConfig;
		readonly List<string> _orgIds = new();

		public override string Id => "hud-player-org";
		public override string Title => "HUD: PlayerOrg";
		protected override IReadOnlyList<string> InstanceChoices => _orgIds;
		protected override IReadOnlyList<string> StateChoices => _states;
		protected override string InstanceLabel => "Org";

		public HudPlayerOrgGalleryBlock(ILocalization loc, VisualTreeAsset hudUxml, TextAsset resourceConfigAsset, OrgVisualConfig orgVisualConfig) {
			_loc = loc;
			_hudUxml = hudUxml;
			_resourceConfig = HudConfigLoader.LoadResourceConfig(resourceConfigAsset);
			_orgVisualConfig = orgVisualConfig;
			if (_orgVisualConfig != null) {
				foreach (OrgVisualEntry entry in _orgVisualConfig.Entries) {
					_orgIds.Add(entry.orgId);
					if (_orgIds.Count >= 6) { break; }
				}
			}
		}

		protected override void Render(VisualElement stage, string orgId, int stateIndex) {
			VisualElement root = HudGalleryPreview.CloneNamed(_hudUxml, "player-country");
			if (root == null || _resourceConfig == null) {
				return;
			}
			var tooltip = new TooltipSystem(root);
			var view = new PlayerOrgView(root, _loc, _resourceConfig, tooltip, _orgVisualConfig);
			var state = HudSampleData.BuildPlayerOrganization(orgId, _loc.Get($"organization_name.{orgId}"), "Afghanistan", _resourceConfig);
			view.Refresh(state, state.Resources);
			stage.Add(root);
		}
	}

	public class HudPlayerTasksGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Tasks" };
		static readonly List<string> _states = new List<string> { "Default" };

		readonly ILocalization _loc;
		readonly VisualTreeAsset _hudUxml;
		readonly ResourceConfig _resourceConfig;

		public override string Id => "hud-player-tasks";
		public override string Title => "HUD: PlayerTasks";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;

		public HudPlayerTasksGalleryBlock(ILocalization loc, VisualTreeAsset hudUxml, TextAsset resourceConfigAsset) {
			_loc = loc;
			_hudUxml = hudUxml;
			_resourceConfig = HudConfigLoader.LoadResourceConfig(resourceConfigAsset);
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			VisualElement root = HudGalleryPreview.CloneNamed(_hudUxml, "player-tasks");
			if (root == null) {
				return;
			}
			var view = new PlayerTasksView(root, _loc, _resourceConfig);
			view.Refresh(HudSampleData.BuildActiveTasks());
			stage.Add(root);
		}
	}

	public class HudTimeGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Time panel" };
		static readonly List<string> _states = new List<string> { "Playing x1", "Playing x3", "Paused" };

		readonly VisualTreeAsset _hudUxml;

		public override string Id => "hud-time";
		public override string Title => "HUD: Time";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;

		public HudTimeGalleryBlock(VisualTreeAsset hudUxml) {
			_hudUxml = hudUxml;
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			VisualElement root = HudGalleryPreview.CloneNamed(_hudUxml, "time-panel");
			if (root == null) {
				return;
			}
			var view = new TimeView(root, () => { }, _ => { });
			bool paused = stateIndex == 2;
			int multiplierIndex = stateIndex == 1 ? 2 : 0;
			var state = new GS.Main.TimeState();
			state.Set(new System.DateTime(1880, 6, 15, 14, 0, 0), paused, multiplierIndex);
			view.Refresh(state);
			stage.Add(root);
		}
	}

	public class HudLensSwitcherGalleryBlock : GalleryBlockBase {
		static readonly List<string> _states = new List<string> { "Political", "Geographic", "Org", "Province" };

		readonly ILocalization _loc;
		readonly VisualTreeAsset _hudUxml;

		public override string Id => "hud-lens-switcher";
		public override string Title => "HUD: LensSwitcher";
		protected override IReadOnlyList<string> InstanceChoices => _singleInstance;
		protected override IReadOnlyList<string> StateChoices => _states;

		static readonly List<string> _singleInstance = new List<string> { "Lens switcher" };

		public HudLensSwitcherGalleryBlock(ILocalization loc, VisualTreeAsset hudUxml) {
			_loc = loc;
			_hudUxml = hudUxml;
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			VisualElement root = HudGalleryPreview.CloneNamed(_hudUxml, "lens-switcher");
			if (root == null) {
				return;
			}
			var tooltip = new TooltipSystem(root);
			var view = new LensSwitcherView(root, tooltip, _loc);
			MapLens lens = (MapLens)Mathf.Clamp(stateIndex, 0, _states.Count - 1);
			view.Refresh(lens);
			stage.Add(root);
		}
	}

	public class HudOrgLensCountryGalleryBlock : GalleryBlockBase {
		static readonly List<string> _states = new List<string> { "Dominant org", "No dominant org" };

		readonly ILocalization _loc;
		readonly VisualTreeAsset _hudUxml;
		readonly ResourceConfig _resourceConfig;
		readonly OrgVisualConfig _orgVisualConfig;
		readonly List<string> _countryIds = new();

		public override string Id => "hud-org-lens-country";
		public override string Title => "HUD: OrgLensCountryInfo";
		protected override IReadOnlyList<string> InstanceChoices => _countryIds;
		protected override IReadOnlyList<string> StateChoices => _states;
		protected override string InstanceLabel => "Country";
		protected override bool IsFullSurface => true;

		public HudOrgLensCountryGalleryBlock(
			ILocalization loc, VisualTreeAsset hudUxml, TextAsset resourceConfigAsset,
			CountryVisualConfig countryVisualConfig, OrgVisualConfig orgVisualConfig, CountryConfig countryConfig) {
			_loc = loc;
			_hudUxml = hudUxml;
			_resourceConfig = HudConfigLoader.LoadResourceConfig(resourceConfigAsset);
			_orgVisualConfig = orgVisualConfig;
			if (countryVisualConfig != null) {
				foreach (CountryVisualEntry entry in countryVisualConfig.Entries) {
					if (!HudConfigLoader.IsCountryAvailable(countryConfig, entry.countryId)) {
						continue;
					}
					_countryIds.Add(entry.countryId);
					if (_countryIds.Count >= 6) { break; }
				}
			}
		}

		protected override void Render(VisualElement stage, string countryId, int stateIndex) {
			VisualElement root = HudGalleryPreview.CloneNamed(_hudUxml, "org-lens-country-info", resetToRelative: false);
			if (root == null || _resourceConfig == null) {
				return;
			}
			var tooltip = new TooltipSystem(root);
			var view = new OrgLensCountryView(root, _loc, _resourceConfig, tooltip, _orgVisualConfig);
			var control = HudSampleData.BuildSelectedCountry(countryId, _resourceConfig, new List<string>(), new List<string>(), new List<string>());
			bool hasDominant = stateIndex == 0;
			string topOrgId = _orgVisualConfig != null && _orgVisualConfig.Entries.Count > 0 ? _orgVisualConfig.Entries[0].orgId : "player_org";
			var orgMap = hasDominant ? HudSampleData.BuildOrgMap(countryId, topOrgId) : new GS.Main.OrgMapState();
			var orgResources = HudSampleData.BuildResources(_resourceConfig, topOrgId, 380);
			view.Refresh(control, orgMap, control.Control, orgResources);
			stage.Add(root);
		}
	}

	public class HudActionLogGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Sample log" };
		static readonly List<string> _states = new List<string> { "Default" };

		readonly ILocalization _loc;
		readonly VisualTreeAsset _hudUxml;
		readonly CountryVisualConfig _countryVisualConfig;
		readonly OrgVisualConfig _orgVisualConfig;

		public override string Id => "hud-action-log";
		public override string Title => "HUD: ActionLog";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;

		public HudActionLogGalleryBlock(ILocalization loc, VisualTreeAsset hudUxml, CountryVisualConfig countryVisualConfig, OrgVisualConfig orgVisualConfig) {
			_loc = loc;
			_hudUxml = hudUxml;
			_countryVisualConfig = countryVisualConfig;
			_orgVisualConfig = orgVisualConfig;
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			VisualElement hudRoot = _hudUxml != null ? _hudUxml.CloneTree() : null;
			VisualElement logRoot = hudRoot?.Q("action-log");
			VisualElement topRightPanel = hudRoot?.Q("top-right-panel");
			if (logRoot == null || topRightPanel == null) {
				return;
			}
			logRoot.RemoveFromHierarchy();
			logRoot.style.display = DisplayStyle.Flex;
			logRoot.style.position = Position.Relative;
			logRoot.style.left = StyleKeyword.Null;
			logRoot.style.right = StyleKeyword.Null;
			logRoot.style.top = StyleKeyword.Null;
			logRoot.style.bottom = StyleKeyword.Null;
			logRoot.style.width = 320;

			string orgId = _orgVisualConfig != null && _orgVisualConfig.Entries.Count > 0 ? _orgVisualConfig.Entries[0].orgId : "player_org";
			string countryId = "Afghanistan";
			string targetCountryId = "Germany";
			var view = new ActionLogView(hudRoot, logRoot, topRightPanel, _loc, _countryVisualConfig, _orgVisualConfig);
			view.Refresh(HudSampleData.BuildGameLog(orgId, countryId, targetCountryId));
			stage.Add(logRoot);
		}
	}

	public class HudWarIconsGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "One war" };
		static readonly List<string> _states = new List<string> { "Default" };

		readonly ILocalization _loc;
		readonly VisualTreeAsset _hudUxml;
		readonly CountryVisualConfig _countryVisualConfig;

		public override string Id => "hud-war-icons";
		public override string Title => "HUD: WarIcons";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;

		public HudWarIconsGalleryBlock(ILocalization loc, VisualTreeAsset hudUxml, CountryVisualConfig countryVisualConfig) {
			_loc = loc;
			_hudUxml = hudUxml;
			_countryVisualConfig = countryVisualConfig;
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			VisualElement root = HudGalleryPreview.CloneNamed(_hudUxml, "war-icons");
			if (root == null) {
				return;
			}
			var tooltip = new TooltipSystem(root);
			var view = new WarIconsView(root, _loc, _countryVisualConfig, tooltip, _ => { });
			view.Refresh(HudSampleData.BuildWarIcons("Afghanistan", "Germany"));
			stage.Add(root);
		}
	}

	public class HudTutorialHighlightGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Highlighting goals button" };
		static readonly List<string> _states = new List<string> { "Default" };

		readonly VisualTreeAsset _hudUxml;

		public override string Id => "hud-tutorial-highlight";
		public override string Title => "HUD: TutorialHighlight";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;
		protected override bool IsFullSurface => true;

		public HudTutorialHighlightGalleryBlock(VisualTreeAsset hudUxml) {
			_hudUxml = hudUxml;
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			VisualElement hudRoot = _hudUxml != null ? _hudUxml.CloneTree() : null;
			VisualElement highlightRoot = hudRoot?.Q("tutorial-highlight");
			VisualElement timePanel = hudRoot?.Q("time-panel");
			if (highlightRoot == null || timePanel == null) {
				return;
			}
			stage.Add(hudRoot);
			var view = new TutorialHighlightView(highlightRoot, targetId => targetId == "time_panel" ? timePanel : null);
			view.Refresh(HudSampleData.BuildActiveTasks(includeTutorial: true, highlightTargetId: "time_panel"));
		}
	}

	public class HudFlyTextGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Sample notification" };
		static readonly List<string> _states = new List<string> { "Default" };

		readonly VisualTreeAsset _flyTextUxml;

		public override string Id => "hud-fly-text";
		public override string Title => "HUD: FlyText";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;
		protected override bool IsFullSurface => true;

		public HudFlyTextGalleryBlock(VisualTreeAsset flyTextUxml) {
			_flyTextUxml = flyTextUxml;
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			// FlyTextNotifierDocument is a MonoBehaviour driving a fade in/hold/fade out state
			// machine over time - not something a static Gallery block can host. This previews the
			// same UXML/label the document renders into, fully opaque, so the shape/style is
			// checkable without the timed animation.
			VisualElement root = HudGalleryPreview.CloneNamed(_flyTextUxml, "fly-text-root", resetToRelative: false);
			if (root == null) {
				return;
			}
			Label label = root.Q<Label>("fly-text-label");
			if (label != null) {
				label.enableRichText = true;
				label.text = "<color=#4CAF50>+25 Gold</color> from completed task";
			}
			root.style.opacity = 1f;
			stage.Add(root);
		}
	}
}

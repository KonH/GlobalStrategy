using UnityEngine.UIElements;
using GS.Main;
using GS.Game.Configs;
using GS.Unity.Common;

namespace GS.Unity.UI {
	class CountryInfoView {
		readonly VisualElement _root;
		readonly Label _name;
		readonly Label _influenceLabel;
		readonly VisualElement _influenceRow;
		readonly VisualElement _charsSlide;
		readonly Button _charsToggleBtn;
		readonly ILocalization _loc;
		readonly ResourcesView _resourcesView;
		readonly CharactersView _charactersView;
		CountryInfluenceState _influenceState;
		bool _charsOpen;
		string _lastCountryId;

		public CountryInfoView(VisualElement root, ILocalization loc, ResourceConfig resourceConfig, CharacterConfig characterConfig, TooltipSystem tooltip, CharacterVisualConfig characterVisualConfig) {
			_root = root;
			_name = root.Q<Label>("country-name");
			_influenceRow = root.Q("influence-row");
			_influenceLabel = root.Q<Label>("influence-label");
			_charsSlide = root.Q("characters-slide");
			_charsToggleBtn = root.Q<Button>("chars-toggle-btn");
			_loc = loc;
			_resourcesView = new ResourcesView(root.Q("resources-container"), loc, resourceConfig, tooltip);
			_charactersView = new CharactersView(root.Q("characters-container"), loc, characterConfig, tooltip, characterVisualConfig);

			if (_influenceRow != null) {
				tooltip.RegisterTrigger(_influenceRow, "country-influence", BuildInfluenceTooltip, new System.Collections.Generic.HashSet<string>());
			}
			if (_charsSlide != null) {
				_charsSlide.pickingMode = PickingMode.Ignore;
			}
			if (_charsToggleBtn != null) {
				_charsToggleBtn.clicked += ToggleChars;
			}
		}

		public void Refresh(SelectedCountryState selected, PlayerCountryState player, CountryResourcesState resources, CountryInfluenceState influence, CountryCharactersState characters) {
			_root.style.display = selected.IsValid ? DisplayStyle.Flex : DisplayStyle.None;
			if (selected.IsValid) {
				_name.text = _loc.Get($"country_name.{selected.CountryId}");
			}

			if (selected.CountryId != _lastCountryId) {
				_lastCountryId = selected.CountryId;
				SetCharsOpen(false);
			}

			bool hasChars = characters.Characters.Count > 0;
			if (_charsToggleBtn != null) {
				_charsToggleBtn.style.display = hasChars ? DisplayStyle.Flex : DisplayStyle.None;
			}

			_influenceState = influence;
			RefreshInfluence(influence);
			_resourcesView.Refresh(resources);
			_charactersView.Refresh(characters);
		}

		void ToggleChars() {
			SetCharsOpen(!_charsOpen);
		}

		void SetCharsOpen(bool open) {
			_charsOpen = open;
			if (_charsSlide != null) {
				if (open) {
					_charsSlide.AddToClassList("characters-slide--open");
					_charsSlide.pickingMode = PickingMode.Position;
				} else {
					_charsSlide.RemoveFromClassList("characters-slide--open");
					_charsSlide.pickingMode = PickingMode.Ignore;
				}
			}
			if (_charsToggleBtn != null) {
				_charsToggleBtn.text = open ? "▼ Characters" : "▲ Characters";
			}
		}

		void RefreshInfluence(CountryInfluenceState influence) {
			if (_influenceRow == null) {
				return;
			}
			_influenceRow.style.display = DisplayStyle.Flex;
			int used = influence != null ? influence.UsedInfluence : 0;
			int pool = influence != null ? influence.PoolSize : 100;
			_influenceLabel.text = $"{_loc.Get("hud.country_influence")}: {used}/{pool}";
		}

		VisualElement BuildInfluenceTooltip(TooltipContext ctx) {
			var root = new VisualElement();

			var header = new Label(_loc.Get("hud.influence_tooltip_title"));
			header.AddToClassList("tooltip-header");
			root.Add(header);

			var influence = _influenceState;
			if (influence == null) {
				return root;
			}

			foreach (var entry in influence.OrgEntries) {
				var row = new Label($"{entry.DisplayName}: {entry.Influence}");
				row.AddToClassList("tooltip-effect-name");
				row.AddToClassList("tooltip-effect-positive");
				row.AddToClassList("tooltip-inner-trigger");
				root.Add(row);

				var capturedEntry = entry;
				ctx.RegisterInnerTrigger(row, $"org-influence-{entry.OrgId}", _ =>
					BuildOrgInfluenceInnerTooltip(capturedEntry));
			}

			return root;
		}

		VisualElement BuildOrgInfluenceInnerTooltip(OrgInfluenceEntry entry) {
			var root = new VisualElement();

			var header = new Label(entry.DisplayName);
			header.AddToClassList("tooltip-header");
			root.Add(header);

			var influenceRow = new Label($"{_loc.Get("hud.country_influence")}: {entry.Influence}");
			influenceRow.AddToClassList("tooltip-effect-name");
			root.Add(influenceRow);

			var baseRow = new Label($"  {_loc.Get("hud.influence_tooltip_base")} +{entry.BaseInfluence}");
			baseRow.AddToClassList("tooltip-effect-name");
			baseRow.AddToClassList("tooltip-effect-positive");
			root.Add(baseRow);

			if (entry.PermanentInfluence > 0) {
				var permRow = new Label($"  {_loc.Get("hud.influence_tooltip_permanent")} +{entry.PermanentInfluence}");
				permRow.AddToClassList("tooltip-effect-name");
				permRow.AddToClassList("tooltip-effect-positive");
				root.Add(permRow);
			}

			var leadsTo = new Label(_loc.Get("hud.influence_tooltip_leads_to"));
			leadsTo.AddToClassList("tooltip-effect-name");
			root.Add(leadsTo);

			var incomeRow = new Label($"  {_loc.Get("hud.influence_tooltip_income")} +{entry.EstimatedMonthlyGold:F1}/month");
			incomeRow.AddToClassList("tooltip-effect-name");
			incomeRow.AddToClassList("tooltip-effect-positive");
			root.Add(incomeRow);

			return root;
		}
	}
}

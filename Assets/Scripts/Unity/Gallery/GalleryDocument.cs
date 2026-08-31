using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UIElements;
using GS.Game.Configs;
using GS.Main;
using GS.Unity.Common;
using GS.Unity.Map;
using GS.Unity.Save;
using GS.Unity.UI;

namespace GS.Unity.Gallery {
	/// <summary>
	/// Option A prototype from Docs/Specs/26_08_28_16_ui-refactoring/analysis.md, scoped to a
	/// single element: the action card, rendered by the very same ActionCardBuilder the HUD uses,
	/// from a hand-built ActionCardEntry instead of a running game. Two dropdowns pick which card
	/// and which state.
	///
	/// Deliberately DI-free - no LifetimeScope, no GameLogic, no world, no bots, no save. Press
	/// Play on Assets/Scenes/Gallery.unity and the card is on screen immediately. That is the
	/// whole point of the prototype: judge whether this feedback loop is fast enough before
	/// deciding whether option E (native UXML bindings) is worth its cost.
	/// </summary>
	public class GalleryDocument : MonoBehaviour {
		// Ordered to match _stateChoices below - the dropdown's index selects the enum.
		enum CardState {
			Playable,
			UnaffordableGold,
			RequirementsFailed,
			OnCooldown,
			WarOdds,
			MultiCountryTarget,
			DiscardHint
		}

		static readonly List<string> _stateChoices = new List<string> {
			"Playable",
			"Unaffordable gold",
			"Requirements failed",
			"On cooldown",
			"War odds badge",
			"Multi-country target",
			"Discard hint"
		};

		[SerializeField] UIDocument _document;
		[SerializeField] TextAsset _actionConfigAsset;
		[SerializeField] LocalizationConfig _localizationConfig;
		[SerializeField] ActionVisualConfig _actionVisualConfig;
		[SerializeField] CountryVisualConfig _countryVisualConfig;
		[SerializeField] double _discardGoldCost = 50;
		[SerializeField] string _sampleTargetCountryId = "france";

		// Serialized so the current selection also survives the domain reload a script recompile
		// causes, not just a UXML/USS re-import.
		[SerializeField, HideInInspector] string _selectedActionId = "";
		[SerializeField, HideInInspector] int _selectedStateIndex;
		[SerializeField, HideInInspector] bool _cardBlockExpanded = true;

		ILocalization _loc;
		ActionConfig _config;
		VisualElement _cardHost;
		Label _summary;
		DropdownField _cardDropdown;
		DropdownField _stateDropdown;

		void OnEnable() {
			if (_document == null) {
				_document = GetComponent<UIDocument>();
			}
			if (_document == null || _actionConfigAsset == null || _localizationConfig == null) {
				Debug.LogError("[Gallery] GalleryDocument is missing a serialized reference - check the Gallery scene wiring.");
				return;
			}

			_loc = new CustomLocalization(_localizationConfig, new SettingsStorage(new PersistentStorage()));
			_config = JsonConvert.DeserializeObject<ActionConfig>(_actionConfigAsset.text);
			if (_config == null) {
				Debug.LogError("[Gallery] Failed to parse the action config TextAsset.");
				return;
			}

			Bind();
		}

		void Update() {
			// Editing the gallery's UXML or USS while it runs makes UIDocument rebuild its whole
			// tree from the source asset, which detaches every element bound below - hence the
			// dropdowns appearing to reset mid-styling. Rebind to the fresh tree and restore the
			// selection rather than letting it snap back to the first entry.
			if (_cardDropdown != null && _cardDropdown.panel != null) {
				return;
			}
			Bind();
		}

		void Bind() {
			VisualElement root = _document.rootVisualElement;
			if (root == null) {
				return;
			}
			DropdownField cardDropdown = root.Q<DropdownField>("card-dropdown");
			DropdownField stateDropdown = root.Q<DropdownField>("state-dropdown");
			VisualElement cardHost = root.Q<VisualElement>("card-host");
			if (cardDropdown == null || stateDropdown == null || cardHost == null) {
				// UIDocument has cleared the tree but not re-instantiated it yet; retry next frame.
				return;
			}

			_cardDropdown = cardDropdown;
			_stateDropdown = stateDropdown;
			_cardHost = cardHost;
			_summary = root.Q<Label>("gallery-summary");

			var cardBlock = root.Q<Foldout>("card-block");
			if (cardBlock != null) {
				cardBlock.value = _cardBlockExpanded;
				cardBlock.RegisterValueChangedCallback(evt => {
					// A Foldout also receives bool change events bubbling up from its content, so
					// only its own toggle counts as the block being expanded or collapsed.
					if (evt.target == cardBlock) {
						_cardBlockExpanded = evt.newValue;
					}
				});
			}

			var actionIds = new List<string>(_config.Actions.Count);
			foreach (ActionDefinition definition in _config.Actions) {
				actionIds.Add(definition.ActionId);
			}
			_cardDropdown.choices = actionIds;
			int selectedCard = actionIds.IndexOf(_selectedActionId);
			_cardDropdown.index = selectedCard >= 0 ? selectedCard : (actionIds.Count > 0 ? 0 : -1);

			_stateDropdown.choices = new List<string>(_stateChoices);
			_stateDropdown.index = Mathf.Clamp(_selectedStateIndex, 0, _stateChoices.Count - 1);

			FitDropdownToWidestChoice(_cardDropdown);
			FitDropdownToWidestChoice(_stateDropdown);

			// Registered after the indices are assigned above, so restoring a selection does not
			// re-enter OnSelectionChanged.
			_cardDropdown.RegisterValueChangedCallback(_ => OnSelectionChanged());
			_stateDropdown.RegisterValueChangedCallback(_ => OnSelectionChanged());

			Rebuild();
		}

		void OnSelectionChanged() {
			_selectedActionId = _cardDropdown.value ?? "";
			_selectedStateIndex = _stateDropdown.index;
			Rebuild();
		}

		void Rebuild() {
			if (_cardHost == null || _cardDropdown.index < 0) {
				return;
			}
			_cardHost.Clear();

			string actionId = _cardDropdown.value;
			var state = (CardState)Mathf.Clamp(_stateDropdown.index, 0, _stateChoices.Count - 1);

			ActionCardEntry entry = BuildEntry(actionId, state);
			ActionCardBuilder.CountryCardFace face = ComposeFace(entry);
			ActionCardBuilder.CardResult result = ActionCardBuilder.Build(face, includeDiscardHint: true);

			result.Card.AddToClassList(entry.CanPlay ? "action-card--available" : "action-card--unavailable");

			if (result.CostLabel != null && state == CardState.UnaffordableGold) {
				result.CostLabel.AddToClassList("action-card-cost-label--unaffordable");
			}
			if (result.DiscardHintLabel != null) {
				result.DiscardHintLabel.text = _loc.Get("action.discard.hint");
			}
			if (result.DiscardHintPrice != null) {
				result.DiscardHintPrice.text = FormatNumber(_discardGoldCost);
			}
			if (result.DiscardHint != null) {
				// The HUD shows this only mid-gesture; here it is just another previewable state.
				result.DiscardHint.style.display =
					state == CardState.DiscardHint ? DisplayStyle.Flex : DisplayStyle.None;
			}

			_cardHost.Add(result.Card);

			if (_summary != null) {
				_summary.text = $"{actionId} - {_stateChoices[(int)state]}";
			}
		}

		/// <summary>
		/// The gallery's whole state layer: an ActionCardEntry built by hand in a few lines,
		/// no ECS world required. Every state the HUD can put a card in is one case here.
		/// </summary>
		ActionCardEntry BuildEntry(string actionId, CardState state) {
			ActionDefinition definition = _config.Find(actionId);
			double goldCost = GetGoldCost(definition);
			double cooldownDays = definition?.CooldownDays ?? 3;

			switch (state) {
				case CardState.UnaffordableGold:
					return new ActionCardEntry(
						actionId, 0, isInHand: true, canPlay: false,
						conditions: new List<ActionConditionDebugEntry> {
							new ActionConditionDebugEntry(
								"gold", false, "action.requirement.gold",
								new[] { FormatNumber(goldCost) })
						});

				case CardState.RequirementsFailed:
					return new ActionCardEntry(
						actionId, 0, isInHand: true, canPlay: false,
						conditions: new List<ActionConditionDebugEntry> {
							new ActionConditionDebugEntry(
								"control", false, "action.requirement.control_min", new[] { "25" }),
							new ActionConditionDebugEntry(
								"opinion", false, "action.requirement.opinion_min_role",
								new[] { _loc.Get("character.role.ruler.name"), "40" }),
							new ActionConditionDebugEntry(
								"capacity", false, "action.requirement.control_capacity")
						});

				case CardState.OnCooldown:
					return new ActionCardEntry(
						actionId, 0, isInHand: true, canPlay: false,
						cooldownRemainingDays: cooldownDays,
						cooldownFractionRemaining: 0.6);

				case CardState.WarOdds:
					return new ActionCardEntry(
						actionId, 0, isInHand: true, canPlay: true,
						targetCountryId: _sampleTargetCountryId,
						warWinChancePercent: 42);

				case CardState.MultiCountryTarget:
					return new ActionCardEntry(
						actionId, 0, isInHand: true, canPlay: true,
						playableCountryIds: SampleCountryIds(3));

				case CardState.DiscardHint:
				case CardState.Playable:
				default:
					return new ActionCardEntry(actionId, 0, isInHand: true, canPlay: true);
			}
		}

		/// <summary>
		/// Mirrors CountryActionsView.ComposeFaceData. Kept as a copy rather than shared: the
		/// production version is a private member of a view that also owns gestures, tooltips and
		/// a hand container, none of which a single-card gallery has. If option A graduates past
		/// the prototype, extract the shared version instead of growing this one.
		/// </summary>
		ActionCardBuilder.CountryCardFace ComposeFace(ActionCardEntry card) {
			ActionDefinition definition = _config.Find(card.ActionId);
			string name;
			if (definition == null) {
				name = card.ActionId;
			} else if (!string.IsNullOrEmpty(card.TargetCountryId)) {
				name = string.Format(
					_loc.Get(definition.NameKey), _loc.Get($"country_name.{card.TargetCountryId}"));
			} else {
				name = _loc.Get(definition.NameKey);
			}

			var requirements = new List<ActionCardBuilder.RequirementRow>();
			foreach (ActionConditionDebugEntry condition in card.Conditions) {
				if (condition.Passed) {
					continue;
				}
				requirements.Add(new ActionCardBuilder.RequirementRow(
					ActionConditionText.Localize(_loc, condition), condition.Passed));
			}

			var playableCountries = new List<ActionCardBuilder.PlayableCountryBadgeItem>(card.PlayableCountryIds.Count);
			foreach (string countryId in card.PlayableCountryIds) {
				Sprite flag = _countryVisualConfig?.Find(countryId)?.flag;
				playableCountries.Add(new ActionCardBuilder.PlayableCountryBadgeItem(countryId, flag));
			}

			return new ActionCardBuilder.CountryCardFace(
				name,
				definition != null ? _loc.Get(definition.DescKey) : "",
				GetGoldCostText(definition),
				_actionVisualConfig?.FindFront(card.ActionId),
				card.WarWinChancePercent,
				card.CooldownFractionRemaining,
				card.CooldownRemainingDays,
				requirements,
				playableCountries);
		}

		List<string> SampleCountryIds(int count) {
			var ids = new List<string>(count);
			if (_countryVisualConfig == null) {
				return ids;
			}
			foreach (CountryVisualEntry entry in _countryVisualConfig.Entries) {
				if (ids.Count >= count) {
					break;
				}
				ids.Add(entry.countryId);
			}
			return ids;
		}

		/// <summary>
		/// Sizes a dropdown to its widest choice instead of a fixed width, so adding a longer
		/// action id or state name never truncates the popup text. Measures with the popup's own
		/// text element so the field's real font and size are used, and re-measures on the first
		/// geometry pass because fonts do not resolve until the field is laid out in a panel.
		/// </summary>
		static void FitDropdownToWidestChoice(DropdownField dropdown) {
			if (dropdown == null) {
				return;
			}
			float applied = -1f;

			void Apply() {
				var text = dropdown.Q<TextElement>(className: "unity-base-popup-field__text");
				if (text == null || dropdown.choices == null || dropdown.choices.Count == 0) {
					return;
				}
				float widest = 0f;
				foreach (string choice in dropdown.choices) {
					Vector2 size = text.MeasureTextSize(
						choice, 0f, VisualElement.MeasureMode.Undefined,
						0f, VisualElement.MeasureMode.Undefined);
					if (size.x > widest) {
						widest = size.x;
					}
				}
				if (widest <= 0f) {
					return;
				}
				// A few px so the longest entry never sits flush against the dropdown arrow.
				float target = Mathf.Ceil(widest) + 8f;
				// Guard against re-entering from the geometry change this very assignment causes.
				if (Mathf.Abs(applied - target) < 0.5f) {
					return;
				}
				applied = target;
				text.style.minWidth = target;
			}

			dropdown.RegisterCallback<GeometryChangedEvent>(_ => Apply());
			Apply();
		}

		static double GetGoldCost(ActionDefinition definition) {
			if (definition == null) {
				return 0;
			}
			foreach (ActionCost cost in definition.Cost) {
				if (cost.ResourceId == "gold") {
					return cost.Amount;
				}
			}
			return 0;
		}

		static string GetGoldCostText(ActionDefinition definition) {
			double gold = GetGoldCost(definition);
			return gold == 0 ? null : FormatNumber(gold);
		}

		static string FormatNumber(double value) =>
			value.ToString("0.##", CultureInfo.InvariantCulture);
	}
}

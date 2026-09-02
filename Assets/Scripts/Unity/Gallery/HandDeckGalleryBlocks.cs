using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using GS.Game.Common;
using GS.Game.Configs;
using GS.Main;
using GS.Unity.Common;
using GS.Unity.Map;
using GS.Unity.UI;

namespace GS.Unity.Gallery {
	/// <summary>
	/// Hand/deck and animation gallery blocks (Docs/Specs/26_08_28_16_ui-refactoring phase 7,
	/// "Hand/deck and animation blocks" batch). Same approach as HudPanelGalleryBlocks: clone the
	/// real named sub-element out of its owning UXML via HudGalleryPreview, construct the same
	/// view class the shipping UI uses, and feed it hand-built VisualState substates from
	/// HudSampleData - no running game, no ECS world, no save.
	///
	/// CardPlayAnimator and CardDrawAnimator are MonoBehaviours/orchestrators with heavy DI
	/// dependencies (VisualState, IWriteOnlyCommandAccessor, ModalState, live UIDocument) that
	/// cannot be sensibly constructed outside a running game. Per the plan's own carve-out for
	/// "genuinely can't be meaningfully previewed" targets: CardPlayAnimator's one independent
	/// visual surface - the "card-test-overlay" mid-resolve card - is previewed directly below
	/// with the same ActionCardBuilder/UXML it renders into (CardPlayTestOverlayGalleryBlock),
	/// mirroring how Batch 1's HudFlyTextGalleryBlock covered FlyTextNotifierDocument without
	/// constructing the MonoBehaviour. CardDrawAnimator has no such independent visual surface -
	/// everything it shows is either the CardDrawView offer screen (covered by
	/// CardDrawOfferGalleryBlock below) or CountryActionsView's own presentation-busy shield
	/// (covered by CountryActionsHandGalleryBlock); its own contribution is sorting-order/
	/// ModalState bookkeeping, which is not visual. It is therefore deliberately not given its
	/// own block - recorded here rather than left silently undone.
	/// </summary>
	public class CountryActionsHandGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Sample hand" };
		static readonly List<string> _states = new List<string> { "Mixed hand", "Empty hand, can draw", "Full hand, can't draw" };

		readonly ILocalization _loc;
		readonly VisualTreeAsset _hudUxml;
		readonly ActionConfig _actionConfig;
		readonly ActionVisualConfig _actionVisualConfig;
		readonly CountryVisualConfig _countryVisualConfig;
		readonly ResourceConfig _resourceConfig;
		readonly double _discardGoldCost;
		readonly List<string> _countryActionIds = new();

		public override string Id => "country-actions-hand";
		public override string Title => "CountryActionsView: Hand";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;

		public CountryActionsHandGalleryBlock(
			ILocalization loc, VisualTreeAsset hudUxml, TextAsset actionConfigAsset, ActionVisualConfig actionVisualConfig,
			CountryVisualConfig countryVisualConfig, TextAsset resourceConfigAsset, double discardGoldCost) {
			_loc = loc;
			_hudUxml = hudUxml;
			_actionConfig = HudConfigLoader.LoadActionConfig(actionConfigAsset);
			_actionVisualConfig = actionVisualConfig;
			_countryVisualConfig = countryVisualConfig;
			_resourceConfig = HudConfigLoader.LoadResourceConfig(resourceConfigAsset);
			_discardGoldCost = discardGoldCost;
			if (_actionConfig != null) {
				foreach (ActionDefinition definition in _actionConfig.Actions) {
					if (definition.OwnerType != "country") {
						continue;
					}
					_countryActionIds.Add(definition.ActionId);
					if (_countryActionIds.Count >= 3) {
						break;
					}
				}
			}
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			VisualElement root = HudGalleryPreview.CloneNamed(_hudUxml, "hand-container");
			if (root == null || _actionConfig == null || _countryActionIds.Count == 0) {
				return;
			}
			var tooltip = new TooltipSystem(root);
			var view = new CountryActionsView(root, _loc, _actionConfig, _actionVisualConfig, _countryVisualConfig, tooltip, _discardGoldCost);
			CountryActionsState state = BuildState(stateIndex);
			CountryResourcesState playerResources = HudSampleData.BuildResources(_resourceConfig, "player_org", 500);
			view.Refresh(state, playerResources);
			stage.Add(root);
		}

		CountryActionsState BuildState(int stateIndex) {
			var state = new CountryActionsState();
			switch (stateIndex) {
				case 1:
					state.Set(
						new List<ActionCardEntry>(), new List<ActionCardEntry>(), new List<CardDrawChoiceEntry>(),
						handSize: 3, hasPendingDraw: false, canStartDraw: true, currentTime: DateTime.UtcNow);
					break;
				case 2: {
					var fullHand = new List<ActionCardEntry>();
					for (int i = 0; i < 3; i++) {
						fullHand.Add(new ActionCardEntry(_countryActionIds[i % _countryActionIds.Count], i, isInHand: true, canPlay: true));
					}
					state.Set(
						fullHand, new List<ActionCardEntry>(), new List<CardDrawChoiceEntry>(),
						handSize: 3, hasPendingDraw: false, canStartDraw: false, currentTime: DateTime.UtcNow);
					break;
				}
				default: {
					var mixedHand = new List<ActionCardEntry> {
						new ActionCardEntry(_countryActionIds[0], 0, isInHand: true, canPlay: true),
						new ActionCardEntry(_countryActionIds[1 % _countryActionIds.Count], 1, isInHand: true, canPlay: false,
							conditions: new List<ActionConditionDebugEntry> {
								new ActionConditionDebugEntry("gold", false, "action.requirement.gold", new[] { "10" }),
							}),
						new ActionCardEntry(_countryActionIds[2 % _countryActionIds.Count], 2, isInHand: true, canPlay: false,
							cooldownRemainingDays: 2, cooldownFractionRemaining: 0.4),
					};
					var deck = new List<ActionCardEntry> {
						new ActionCardEntry(_countryActionIds[0], 0, isInHand: false),
						new ActionCardEntry(_countryActionIds[0], 0, isInHand: false),
					};
					state.Set(
						mixedHand, deck, new List<CardDrawChoiceEntry>(),
						handSize: 3, hasPendingDraw: false, canStartDraw: true, currentTime: DateTime.UtcNow);
					break;
				}
			}
			return state;
		}
	}

	public class OrgActionsGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Sample hand" };
		static readonly List<string> _states = new List<string> { "Default" };

		readonly ILocalization _loc;
		readonly VisualTreeAsset _orgInfoUxml;
		readonly ActionConfig _actionConfig;
		readonly ActionVisualConfig _actionVisualConfig;
		readonly ResourceConfig _resourceConfig;
		readonly List<string> _orgActionIds = new();

		public override string Id => "org-actions-hand";
		public override string Title => "OrgActionsView: Hand";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;

		public OrgActionsGalleryBlock(
			ILocalization loc, VisualTreeAsset orgInfoUxml, TextAsset actionConfigAsset,
			ActionVisualConfig actionVisualConfig, TextAsset resourceConfigAsset) {
			_loc = loc;
			_orgInfoUxml = orgInfoUxml;
			_actionConfig = HudConfigLoader.LoadActionConfig(actionConfigAsset);
			_actionVisualConfig = actionVisualConfig;
			_resourceConfig = HudConfigLoader.LoadResourceConfig(resourceConfigAsset);
			if (_actionConfig != null) {
				foreach (ActionDefinition definition in _actionConfig.Actions) {
					if (definition.OwnerType != "org") {
						continue;
					}
					_orgActionIds.Add(definition.ActionId);
					if (_orgActionIds.Count >= 3) {
						break;
					}
				}
			}
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			VisualElement root = HudGalleryPreview.CloneNamed(_orgInfoUxml, "hand-container");
			if (root == null || _actionConfig == null || _orgActionIds.Count == 0) {
				return;
			}
			var tooltip = new TooltipSystem(root);
			var view = new OrgActionsView(root, _loc, _actionConfig, _actionVisualConfig, _resourceConfig, tooltip);
			var hand = new List<ActionCardEntry>();
			for (int i = 0; i < _orgActionIds.Count; i++) {
				hand.Add(new ActionCardEntry(_orgActionIds[i], i, isInHand: true));
			}
			var deck = new List<ActionCardEntry> {
				new ActionCardEntry(_orgActionIds[0], 0, isInHand: false),
			};
			var state = new OrgActionsState();
			state.Set(hand, deck, hand.Count);
			CountryResourcesState resources = HudSampleData.BuildResources(_resourceConfig, "player_org", 200);
			view.Refresh(state, resources);
			stage.Add(root);
		}
	}

	public class CardTransitionGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Country card in transit" };
		static readonly List<string> _states = new List<string> { "Default" };

		readonly ILocalization _loc;
		readonly ActionConfig _actionConfig;
		readonly ActionVisualConfig _actionVisualConfig;
		readonly CountryVisualConfig _countryVisualConfig;

		public override string Id => "card-transition-view";
		public override string Title => "CardTransitionView";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;

		public CardTransitionGalleryBlock(
			ILocalization loc, TextAsset actionConfigAsset, ActionVisualConfig actionVisualConfig, CountryVisualConfig countryVisualConfig) {
			_loc = loc;
			_actionConfig = HudConfigLoader.LoadActionConfig(actionConfigAsset);
			_actionVisualConfig = actionVisualConfig;
			_countryVisualConfig = countryVisualConfig;
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			string actionId = FirstCountryActionId();
			if (_actionConfig == null || actionId == null) {
				return;
			}

			var overlay = new VisualElement();
			overlay.style.position = Position.Relative;
			overlay.style.height = 420;
			overlay.style.width = 480;
			overlay.pickingMode = PickingMode.Ignore;
			stage.Add(overlay);

			var destination = new VisualElement();
			destination.style.position = Position.Absolute;
			destination.style.left = 200;
			destination.style.top = 40;
			destination.style.width = 240;
			destination.style.height = 360;
			overlay.Add(destination);

			var entry = new ActionCardEntry(actionId, 0, isInHand: true, canPlay: true);
			ActionCardBuilder.CountryCardFace face = ActionCardBuilder.ComposeFace(
				_loc, _actionConfig, _actionVisualConfig, _countryVisualConfig, entry);

			var view = new CardTransitionView(overlay);
			var fromRect = new Rect(0, 40, 240, 360);
			// duration 0 lands the card copy immediately - the "static frame" this class has no
			// rest state of its own beyond, since its entire purpose is the flying animation.
			view.ShowCountry(face, fromRect, destination, 0f).Forget();
		}

		string FirstCountryActionId() {
			if (_actionConfig == null) {
				return null;
			}
			foreach (ActionDefinition definition in _actionConfig.Actions) {
				if (definition.OwnerType == "country") {
					return definition.ActionId;
				}
			}
			return null;
		}
	}

	public class CardDrawOfferGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "3 choices" };
		static readonly List<string> _states = new List<string> { "Default" };

		readonly ILocalization _loc;
		readonly ActionConfig _actionConfig;
		readonly ActionVisualConfig _actionVisualConfig;
		readonly CountryVisualConfig _countryVisualConfig;
		readonly List<string> _countryActionIds = new();
		CancellationTokenSource _cts;

		public override string Id => "card-draw-view";
		public override string Title => "CardDrawView: Offer";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;

		public CardDrawOfferGalleryBlock(
			ILocalization loc, TextAsset actionConfigAsset, ActionVisualConfig actionVisualConfig, CountryVisualConfig countryVisualConfig) {
			_loc = loc;
			_actionConfig = HudConfigLoader.LoadActionConfig(actionConfigAsset);
			_actionVisualConfig = actionVisualConfig;
			_countryVisualConfig = countryVisualConfig;
			if (_actionConfig != null) {
				foreach (ActionDefinition definition in _actionConfig.Actions) {
					if (definition.OwnerType != "country") {
						continue;
					}
					_countryActionIds.Add(definition.ActionId);
					if (_countryActionIds.Count >= 3) {
						break;
					}
				}
			}
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			if (_actionConfig == null || _countryActionIds.Count == 0) {
				return;
			}
			_cts?.Cancel();
			_cts?.Dispose();
			_cts = new CancellationTokenSource();

			var overlay = new VisualElement();
			overlay.style.position = Position.Relative;
			overlay.style.height = 420;
			overlay.style.width = 640;
			stage.Add(overlay);

			var row = new VisualElement();
			row.AddToClassList("card-draw-row");
			row.style.position = Position.Relative;
			overlay.Add(row);

			// A minimal stand-in for the real deck pile element - ShowChoicesAsync only needs it
			// attached to a panel, it is never rendered here since animateDeal is false below.
			var deckElement = new VisualElement();
			deckElement.style.width = 4;
			deckElement.style.height = 4;
			overlay.Add(deckElement);

			var view = new CardDrawView(overlay, row, _actionVisualConfig);

			var choices = new List<CardDrawChoiceEntry>();
			for (int i = 0; i < _countryActionIds.Count; i++) {
				choices.Add(new CardDrawChoiceEntry(i, new ActionCardEntry(_countryActionIds[i], 0, isInHand: false)));
			}

			ActionCardBuilder.CountryCardFace BuildFace(ActionCardEntry card) =>
				ActionCardBuilder.ComposeFace(_loc, _actionConfig, _actionVisualConfig, _countryVisualConfig, card);

			CancellationToken token = _cts.Token;

			void Begin() {
				if (token.IsCancellationRequested) {
					return;
				}
				// animateDeal: false shows every choice face-up immediately - the representative
				// static frame for a class whose entire other behaviour is dealing/flip/hover animation.
				view.ShowChoicesAsync(choices, deckElement, BuildFace, animateDeal: false, token)
					.SuppressCancellationThrow()
					.Forget();
			}

			// The block is built the moment its Foldout is created, even while still collapsed -
			// a collapsed Foldout gives every descendant a zero worldBound until it is expanded,
			// which would otherwise make CardDrawView.WaitForSlotGeometryAsync spin for its 2s
			// timeout and throw before the user ever opens this block. Defer the async call until
			// the overlay actually has real layout, using the same GeometryChangedEvent pattern
			// .claude/rules/unity/uitoolkit.md's "Tooltip Positioning" section documents for
			// "don't read worldBound before the panel has laid out" - one-shot, not the recurring
			// re-clamp that pattern uses for repositioning.
			if (overlay.worldBound.width > 0f) {
				Begin();
			} else {
				EventCallback<GeometryChangedEvent> onReady = null;
				onReady = _ => {
					overlay.UnregisterCallback(onReady);
					Begin();
				};
				overlay.RegisterCallback(onReady);
			}
		}
	}

	public class CardPlayTestOverlayGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Card resolving" };
		static readonly List<string> _states = new List<string> { "Default" };

		readonly ILocalization _loc;
		readonly VisualTreeAsset _hudUxml;
		readonly ActionConfig _actionConfig;
		readonly ActionVisualConfig _actionVisualConfig;
		readonly CountryVisualConfig _countryVisualConfig;

		public override string Id => "card-play-test-overlay";
		public override string Title => "CardPlayAnimator: Resolving card";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;
		protected override bool IsFullSurface => true;

		public CardPlayTestOverlayGalleryBlock(
			ILocalization loc, VisualTreeAsset hudUxml, TextAsset actionConfigAsset,
			ActionVisualConfig actionVisualConfig, CountryVisualConfig countryVisualConfig) {
			_loc = loc;
			_hudUxml = hudUxml;
			_actionConfig = HudConfigLoader.LoadActionConfig(actionConfigAsset);
			_actionVisualConfig = actionVisualConfig;
			_countryVisualConfig = countryVisualConfig;
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			VisualElement overlay = HudGalleryPreview.CloneNamed(_hudUxml, "card-test-overlay", resetToRelative: false);
			if (overlay == null || _actionConfig == null) {
				return;
			}
			overlay.style.display = DisplayStyle.Flex;
			VisualElement cardSlot = overlay.Q("card-test-card");
			string actionId = FirstCountryActionId();
			if (cardSlot != null && actionId != null) {
				var entry = new ActionCardEntry(actionId, 0, isInHand: true, canPlay: true);
				ActionCardBuilder.CountryCardFace face = ActionCardBuilder.ComposeFace(
					_loc, _actionConfig, _actionVisualConfig, _countryVisualConfig, entry);
				ActionCardBuilder.PopulateSlot(cardSlot, face, includeDiscardHint: false);
				cardSlot.AddToClassList("action-card--available");
			}
			stage.Add(overlay);
		}

		string FirstCountryActionId() {
			if (_actionConfig == null) {
				return null;
			}
			foreach (ActionDefinition definition in _actionConfig.Actions) {
				if (definition.OwnerType == "country") {
					return definition.ActionId;
				}
			}
			return null;
		}
	}

	public class CountryCharactersGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Sample characters" };
		static readonly List<string> _states = new List<string> { "Default" };

		readonly ILocalization _loc;
		readonly VisualTreeAsset _hudUxml;
		readonly CharacterConfig _characterConfig;
		readonly CharacterVisualConfig _characterVisualConfig;
		readonly ActionConfig _actionConfig;
		readonly ActionVisualConfig _actionVisualConfig;

		public override string Id => "country-characters-view";
		public override string Title => "CharactersView";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;

		public CountryCharactersGalleryBlock(
			ILocalization loc, VisualTreeAsset hudUxml, TextAsset characterConfigAsset, CharacterVisualConfig characterVisualConfig,
			TextAsset actionConfigAsset, ActionVisualConfig actionVisualConfig) {
			_loc = loc;
			_hudUxml = hudUxml;
			_characterConfig = HudConfigLoader.LoadCharacterConfig(characterConfigAsset);
			_characterVisualConfig = characterVisualConfig;
			_actionConfig = HudConfigLoader.LoadActionConfig(actionConfigAsset);
			_actionVisualConfig = actionVisualConfig;
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			VisualElement root = HudGalleryPreview.CloneNamed(_hudUxml, "characters-container");
			if (root == null || _characterConfig == null) {
				return;
			}
			var tooltip = new TooltipSystem(root);
			var view = new CharactersView(root, _loc, _characterConfig, tooltip, _characterVisualConfig, _actionConfig, _actionVisualConfig);
			var state = new CountryCharactersState();
			state.Set(HudSampleData.BuildSampleCountryCharacters(_characterConfig));
			view.Refresh(state);
			stage.Add(root);
		}
	}

	public class OrgCharactersGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Sample org roster" };
		static readonly List<string> _states = new List<string> { "Default" };

		readonly ILocalization _loc;
		readonly VisualTreeAsset _orgInfoUxml;
		readonly CharacterConfig _characterConfig;
		readonly CharacterVisualConfig _characterVisualConfig;

		public override string Id => "org-characters-view";
		public override string Title => "OrgCharactersView";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;

		public OrgCharactersGalleryBlock(
			ILocalization loc, VisualTreeAsset orgInfoUxml, TextAsset characterConfigAsset, CharacterVisualConfig characterVisualConfig) {
			_loc = loc;
			_orgInfoUxml = orgInfoUxml;
			_characterConfig = HudConfigLoader.LoadCharacterConfig(characterConfigAsset);
			_characterVisualConfig = characterVisualConfig;
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			VisualElement root = HudGalleryPreview.CloneNamed(_orgInfoUxml, "characters-container");
			if (root == null || _characterConfig == null) {
				return;
			}
			var tooltip = new TooltipSystem(root);
			var view = new OrgCharactersView(root, _loc, _characterConfig, tooltip, _characterVisualConfig);
			var state = new OrgCharactersState();
			state.Set(HudSampleData.BuildSampleOrgCharacterSlots(_characterConfig));
			view.Refresh(state);
			stage.Add(root);
		}
	}

	public class OrgInfoGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Sample org" };
		static readonly List<string> _states = new List<string> { "Closed", "Characters open", "Actions open" };

		readonly ILocalization _loc;
		readonly VisualTreeAsset _orgInfoUxml;
		readonly ResourceConfig _resourceConfig;
		readonly CharacterConfig _characterConfig;
		readonly CharacterVisualConfig _characterVisualConfig;
		readonly OrgVisualConfig _orgVisualConfig;
		readonly ActionConfig _actionConfig;
		readonly ActionVisualConfig _actionVisualConfig;

		public override string Id => "org-info-overlay";
		public override string Title => "OrgInfoDocument: Overlay";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;
		protected override bool IsFullSurface => true;

		public OrgInfoGalleryBlock(
			ILocalization loc, VisualTreeAsset orgInfoUxml, TextAsset resourceConfigAsset, TextAsset characterConfigAsset,
			CharacterVisualConfig characterVisualConfig, OrgVisualConfig orgVisualConfig,
			TextAsset actionConfigAsset, ActionVisualConfig actionVisualConfig) {
			_loc = loc;
			_orgInfoUxml = orgInfoUxml;
			_resourceConfig = HudConfigLoader.LoadResourceConfig(resourceConfigAsset);
			_characterConfig = HudConfigLoader.LoadCharacterConfig(characterConfigAsset);
			_characterVisualConfig = characterVisualConfig;
			_orgVisualConfig = orgVisualConfig;
			_actionConfig = HudConfigLoader.LoadActionConfig(actionConfigAsset);
			_actionVisualConfig = actionVisualConfig;
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			VisualElement root = HudGalleryPreview.CloneNamed(_orgInfoUxml, "org-info-root", resetToRelative: false);
			if (root == null || _resourceConfig == null || _characterConfig == null) {
				return;
			}

			var tooltip = new TooltipSystem(root);
			string orgId = _orgVisualConfig != null && _orgVisualConfig.Entries.Count > 0
				? _orgVisualConfig.Entries[0].orgId
				: "player_org";

			Label orgName = root.Q<Label>("org-name");
			if (orgName != null) {
				orgName.text = _loc.Get($"organization_name.{orgId}");
			}
			VisualElement orgFlag = root.Q("org-flag");
			Sprite flagSprite = _orgVisualConfig?.Find(orgId)?.flag;
			if (orgFlag != null && flagSprite != null) {
				orgFlag.style.backgroundImage = new StyleBackground(flagSprite);
			}

			var resourcesView = new ResourcesView(root.Q("resources-container"), _loc, _resourceConfig, tooltip);
			resourcesView.Refresh(HudSampleData.BuildResources(_resourceConfig, orgId, 480));

			var charactersView = new OrgCharactersView(root.Q("characters-container"), _loc, _characterConfig, tooltip, _characterVisualConfig);
			var charState = new OrgCharactersState();
			charState.Set(HudSampleData.BuildSampleOrgCharacterSlots(_characterConfig));
			charactersView.Refresh(charState);

			VisualElement actionsInstance = root.Q("org-actions-instance");
			if (actionsInstance != null && _actionConfig != null) {
				var actionsView = new OrgActionsView(actionsInstance.Q("hand-container"), _loc, _actionConfig, _actionVisualConfig, _resourceConfig, tooltip);
				var hand = new List<ActionCardEntry>();
				int slotIndex = 0;
				foreach (ActionDefinition definition in _actionConfig.Actions) {
					if (definition.OwnerType != "org") {
						continue;
					}
					hand.Add(new ActionCardEntry(definition.ActionId, slotIndex, isInHand: true));
					slotIndex++;
					if (slotIndex >= 2) {
						break;
					}
				}
				var actionsState = new OrgActionsState();
				actionsState.Set(hand, new List<ActionCardEntry>(), hand.Count);
				actionsView.Refresh(actionsState, HudSampleData.BuildResources(_resourceConfig, orgId, 480));
			}

			VisualElement charsSlide = root.Q("characters-slide");
			VisualElement actionsSlide = root.Q("actions-slide");
			bool charsOpen = stateIndex == 1;
			bool actionsOpen = stateIndex == 2;
			SetSlideOpen(charsSlide, "org-characters-slide--open", charsOpen);
			SetSlideOpen(actionsSlide, "org-actions-slide--open", actionsOpen);

			Button charsToggle = root.Q<Button>("chars-toggle-btn");
			if (charsToggle != null) {
				Label label = charsToggle.Q<Label>();
				if (label != null) {
					label.text = _loc.Get("hud.org_characters");
				}
				charsToggle.EnableInClassList("gs-toggle-on", charsOpen);
				charsToggle.EnableInClassList("gs-toggle-off", !charsOpen);
			}
			Button actionsToggle = root.Q<Button>("actions-toggle-btn");
			if (actionsToggle != null) {
				Label label = actionsToggle.Q<Label>();
				if (label != null) {
					label.text = _loc.Get("hud.actions");
				}
				actionsToggle.EnableInClassList("gs-toggle-on", actionsOpen);
				actionsToggle.EnableInClassList("gs-toggle-off", !actionsOpen);
			}

			stage.Add(root);
		}

		static void SetSlideOpen(VisualElement slide, string openClass, bool open) {
			if (slide == null) {
				return;
			}
			if (open) {
				slide.AddToClassList(openClass);
				SetPickingModeRecursive(slide, PickingMode.Position);
			} else {
				slide.RemoveFromClassList(openClass);
				SetPickingModeRecursive(slide, PickingMode.Ignore);
			}
		}

		static void SetPickingModeRecursive(VisualElement element, PickingMode mode) {
			element.pickingMode = mode;
			foreach (VisualElement child in element.Children()) {
				SetPickingModeRecursive(child, mode);
			}
		}
	}
}

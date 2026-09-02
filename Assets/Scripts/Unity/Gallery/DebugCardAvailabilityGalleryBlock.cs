using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UIElements;
using GS.Game.Configs;
using GS.Main;
using GS.Unity.DebugTools;
using GS.Unity.UI;

namespace GS.Unity.Gallery {
	/// <summary>Previews DebugCardAvailabilityView's deck/hand listing (Docs/Specs/26_08_28_16_ui-refactoring phase 5), with a hand-built deck/hand instead of a running game.</summary>
	public class DebugCardAvailabilityGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Deck + Hand" };
		static readonly List<string> _states = new List<string> { "Default" };

		readonly ILocalization _loc;
		readonly ActionConfig _config;

		public override string Id => "debug-card-availability";
		public override string Title => "Debug: Card availability";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;

		public DebugCardAvailabilityGalleryBlock(ILocalization loc, TextAsset actionConfigAsset) {
			_loc = loc;
			_config = actionConfigAsset != null ? JsonConvert.DeserializeObject<ActionConfig>(actionConfigAsset.text) : null;
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			var deckContainer = new VisualElement();
			var handContainer = new VisualElement();
			var view = new DebugCardAvailabilityView(deckContainer, handContainer, _loc, _config);

			string sampleActionId = FirstActionId();
			var deck = new List<ActionCardEntry> {
				new ActionCardEntry(sampleActionId, 0, isInHand: false),
			};
			var hand = new List<ActionCardEntry> {
				new ActionCardEntry(sampleActionId, 0, isInHand: true),
				new ActionCardEntry(sampleActionId, 1, isInHand: true, isUnplayable: true, unplayableReason: "unaffordable"),
			};
			view.RefreshDeck(deck);
			view.RefreshHand(hand, availableGold: 100);

			stage.Add(deckContainer);
			stage.Add(handContainer);
		}

		string FirstActionId() {
			if (_config?.Actions == null) {
				return "sample_action";
			}
			foreach (var action in _config.Actions) {
				return action.ActionId;
			}
			return "sample_action";
		}
	}
}

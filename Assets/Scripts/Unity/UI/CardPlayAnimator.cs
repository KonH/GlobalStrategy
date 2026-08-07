using System;
using System.Collections.Generic;
using System.ComponentModel;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;
using GS.Main;
using GS.Game.Commands;
using GS.Game.Configs;
using GS.Unity.Common;

namespace GS.Unity.UI {
	public class CardPlayAnimator : MonoBehaviour {
		UIDocument _hudDocument;
		VisualState _state;
		IWriteOnlyCommandAccessor _commands;
		CountryConfig _domainConfig;
		ActionConfig _actionConfig;
		EffectConfig _effectConfig;
		ActionVisualConfig _visualConfig;
		ILocalization _loc;
		ModalState _modalState;
		bool _isPlaying;
		CardTransitionView _transitionView;
		OrgActionsView _actionsView;
		CountryActionsView _countryActionsView;
		bool _resultReady;
		bool _lastActionSuccess;
		CardPlayBarriersHolder _barrierHolder;

		public bool IsPlaying => _isPlaying;
		public event Action OnCardPlayComplete;

		[Inject]
		void Construct(VisualState state, IWriteOnlyCommandAccessor commands,
			CountryConfig domainConfig,
			ActionConfig actionConfig, EffectConfig effectConfig,
			ActionVisualConfig visualConfig, ILocalization loc, ModalState modalState) {
			_state = state;
			_commands = commands;
			_domainConfig = domainConfig;
			_actionConfig = actionConfig;
			_effectConfig = effectConfig;
			_visualConfig = visualConfig;
			_loc = loc;
			_modalState = modalState;
		}

		void Awake() {
			_hudDocument = GetComponent<UIDocument>();
			var overlay = _hudDocument.rootVisualElement.Q("card-transition-overlay");
			if (overlay == null) {
				Debug.LogError("[CardPlayAnimator] card-transition-overlay not found in UIDocument.", this);
			}
			_transitionView = new CardTransitionView(overlay);
		}

		void OnEnable() {
			if (_state != null) {
				_state.LastFrameEffects.PropertyChanged += HandleLastFrameEffectsChanged;
			}
		}

		void OnDisable() {
			if (_state != null) {
				_state.LastFrameEffects.PropertyChanged -= HandleLastFrameEffectsChanged;
			}
		}

		void HandleLastFrameEffectsChanged(object sender, PropertyChangedEventArgs e) {
			if (_state == null || _state.LastFrameEffects.Effects.Count == 0) { return; }

			// Only the player's own card-play sequence (PlaySequence/PlayCountrySequence) ever
			// releases or cancels these barriers. Effects from bot-driven plays reach this handler
			// too (LastFrameEffects is global, not player-scoped), but with no matching Animate/CancelAll
			// call to follow, a barrier created here would sit on the currently selected country's
			// UsedControl forever, permanently offsetting its Display value.
			if (!_isPlaying) { return; }

			_barrierHolder = new CardPlayBarriersHolder();
			_lastActionSuccess = true;

			foreach (var effect in _state.LastFrameEffects.Effects) {
				if (effect.ResourceId == "gold" && effect.OwnerId == _state.PlayerOrganization.OrgId) {
					AnimatableDouble goldAnimatable = null;
					foreach (var res in _state.PlayerOrganization.Resources.Resources) {
						if (res.ResourceId == "gold") { goldAnimatable = res.Value; break; }
					}
					if (goldAnimatable != null) {
						_barrierHolder.AddDouble("gold", goldAnimatable, -effect.Amount);
					}
				} else if (effect.ResourceId.StartsWith("control_")) {
					var usedControl = _state.SelectedCountry.Control.UsedControl;
					if (usedControl != null) {
						_barrierHolder.AddInt("control", usedControl, -(int)effect.Amount);
					}
				} else if (effect.ResourceId.StartsWith("opinion_")) {
					foreach (var entry in _state.SelectedCountry.Characters.Characters) {
						if (entry.CharacterId == effect.OwnerId) {
							_barrierHolder.AddInt("opinion", entry.Opinion, -(int)effect.Amount);
							break;
						}
					}
				}
			}

			_resultReady = true;
		}

		public void StartCardPlay(string orgId, string actionId, int slotIndex, VisualElement clickedCard) {
			if (_isPlaying) { return; }
			PlaySequence(orgId, actionId, slotIndex, clickedCard).Forget();
		}

		internal void SetActionsView(OrgActionsView view) {
			_actionsView = view;
		}

		internal void SetCountryActionsView(CountryActionsView view) {
			_countryActionsView = view;
		}

		public void StartCountryCardPlay(
			string orgId,
			string countryId,
			string actionId,
			int slotIndex,
			VisualElement clickedCard,
			string targetCountryId = "") {
			if (_isPlaying) { return; }
			PlayCountrySequence(orgId, countryId, actionId, slotIndex, clickedCard, targetCountryId).Forget();
		}

		async UniTaskVoid PlaySequence(string orgId, string actionId, int slotIndex, VisualElement clickedCard) {
			_isPlaying = true;
			_resultReady = false;
			_lastActionSuccess = false;
			_barrierHolder = null;
			_modalState.Lock(this);
			bool issuedPause = !_state.Time.IsPaused;
			if (_actionsView != null) { _actionsView.SuppressRefresh = true; }

			try {
				// Push action before pause so both are processed in the same game tick
				_commands.Push(new PlayCardActionCommand { OrgId = orgId, ActionId = actionId, SlotIndex = slotIndex });
				if (issuedPause) {
					_commands.Push(new PauseCommand());
				}

				var root = _hudDocument.rootVisualElement;
				var overlay = root.Q("card-test-overlay");
				var cardTestCard = root.Q("card-test-card");

				if (overlay != null) {
					PopulateTestCard(cardTestCard, actionId);
					overlay.style.display = DisplayStyle.Flex;
					overlay.style.opacity = 0f;
					if (cardTestCard != null) {
						cardTestCard.style.opacity = 0f;
					}
				}

				var fromRect = clickedCard.worldBound;
				clickedCard.style.opacity = 0f;

				// Capture deck rect before any state change
				var deckRect = _actionsView?.DeckPileElement?.worldBound ?? Rect.zero;

				await _transitionView.Show(actionId, fromRect, cardTestCard, 0.7f, _actionConfig, _visualConfig, _loc);

				if (overlay != null) {
					overlay.style.opacity = 1f;
				}
				if (cardTestCard != null) {
					cardTestCard.style.opacity = 1f;
				}
				_transitionView.Hide();

				float startTime = Time.time;
				while (!_resultReady) {
					await UniTask.Delay(330);
					if (Time.time - startTime > 10f) { break; }
				}

				if (!_resultReady) {
					Debug.LogWarning("[CardPlayAnimator] Timed out waiting for action result.");
				}
				bool success = _lastActionSuccess;

				// Release or cancel gold barrier based on outcome.
				// Barrier was created in HandleLastFrameEffectsChanged before SetActual fired.
				UniTask goldTask = UniTask.CompletedTask;
				if (success && _barrierHolder != null && _barrierHolder.Has("gold")) {
					goldTask = _barrierHolder.Animate("gold", 0.5f);
				} else {
					_barrierHolder?.CancelAll();
					_barrierHolder = null;
				}

				await UniTask.Delay(700);

				// Start card-to-deck transition, then hide overlay concurrently before awaiting
				var fromTestRect = cardTestCard != null ? cardTestCard.worldBound : Rect.zero;
				var deckElement = _actionsView?.DeckPileElement;
				var deckTransitionTask = _transitionView.Show(actionId, fromTestRect, deckElement ?? cardTestCard, 0.77f, _actionConfig, _visualConfig, _loc);
				if (overlay != null) { overlay.style.display = DisplayStyle.None; }
				await deckTransitionTask;
				_transitionView.Hide();

				// Allow one Refresh() to rebuild hand with new card, then re-suppress
				if (_actionsView != null) { _actionsView.SuppressRefresh = false; }
				await UniTask.NextFrame();
				if (_actionsView != null) { _actionsView.SuppressRefresh = true; }

				VisualElement newHandCard = null;
				if (_actionsView != null) {
					var handContainer = _actionsView.HandContainer;
					int childCount = handContainer.childCount;
					if (childCount > 1) {
						var lastWrapper = handContainer[childCount - 1];
						newHandCard = lastWrapper.Q(className: "action-card");
					}
					if (newHandCard != null) {
						newHandCard.style.opacity = 0f;
					}
				}

				if (newHandCard != null) {
					string newActionId = "";
					if (_state.PlayerOrganization.Actions.Hand.Count > 0) {
						newActionId = _state.PlayerOrganization.Actions.Hand[_state.PlayerOrganization.Actions.Hand.Count - 1].ActionId;
					}
					await _transitionView.Show(newActionId, deckRect, newHandCard, 0.5f, _actionConfig, _visualConfig, _loc);
					newHandCard.style.opacity = 1f;
					_transitionView.Hide();
				}
				if (_actionsView != null) {
					_actionsView.SuppressRefresh = false;
				}

				_modalState.Unlock(this);
				if (issuedPause) {
					_commands.Push(new UnpauseCommand());
					issuedPause = false;
				}
				await goldTask;
				_barrierHolder = null;
				_isPlaying = false;
			} finally {
				_barrierHolder?.CancelAll();
				_barrierHolder = null;
				_transitionView.Hide();
				_modalState.Unlock(this);
				if (issuedPause) {
					_commands.Push(new UnpauseCommand());
				}
				if (_actionsView != null) {
					_actionsView.SuppressRefresh = false;
				}
				_isPlaying = false;
				OnCardPlayComplete?.Invoke();
			}
		}

		async UniTaskVoid PlayCountrySequence(
			string orgId,
			string countryId,
			string actionId,
			int slotIndex,
			VisualElement clickedCard,
			string targetCountryId = "") {
			_isPlaying = true;
			_resultReady = false;
			_lastActionSuccess = false;
			_barrierHolder = null;
			_modalState.Lock(this);
			bool issuedPause = !_state.Time.IsPaused;

			if (_countryActionsView != null) { _countryActionsView.SuppressRefresh = true; }

			try {
				int? warWinChancePercent = null;
				foreach (var handCard in _state.SelectedCountry.CountryActions.Hand) {
					if (handCard.ActionId == actionId
						&& handCard.TargetCountryId == targetCountryId
						&& handCard.SlotIndex == slotIndex) {
						warWinChancePercent = handCard.WarWinChancePercent;
						break;
					}
				}

				_commands.Push(new PlayCardActionCommand {
					OrgId = orgId,
					CountryId = countryId,
					ActionId = actionId,
					TargetCountryId = targetCountryId,
					SlotIndex = slotIndex
				});
				if (issuedPause) {
					_commands.Push(new PauseCommand());
				}

				var root = _hudDocument.rootVisualElement;
				var overlay = root.Q("card-test-overlay");
				var cardTestCard = root.Q("card-test-card");

				if (overlay != null) {
					PopulateCountryTestCard(cardTestCard, actionId, targetCountryId, warWinChancePercent);
					overlay.style.display = DisplayStyle.Flex;
					overlay.style.opacity = 0f;
					if (cardTestCard != null) { cardTestCard.style.opacity = 0f; }
				}

				var fromRect = clickedCard.worldBound;
				clickedCard.style.opacity = 0f;
				var deckRect = _countryActionsView?.DeckPileElement?.worldBound ?? Rect.zero;

				await _transitionView.ShowCountry(actionId, fromRect, cardTestCard, 0.7f, _actionConfig, _visualConfig, _loc, targetCountryId, warWinChancePercent);

				if (overlay != null) { overlay.style.opacity = 1f; }
				if (cardTestCard != null) { cardTestCard.style.opacity = 1f; }
				_transitionView.Hide();

				float startTime = Time.time;
				while (!_resultReady) {
					await UniTask.Delay(330);
					if (Time.time - startTime > 10f) { break; }
				}

				if (!_resultReady) { Debug.LogWarning("[CardPlayAnimator] Country action timed out waiting for result."); }
				bool success = _lastActionSuccess;

				await UniTask.Delay(700);

				// Start card-to-deck transition, then hide overlay concurrently before awaiting
				var fromTestRect = cardTestCard != null ? cardTestCard.worldBound : Rect.zero;
				var deckElement = _countryActionsView?.DeckPileElement;
				var deckTransitionTask = _transitionView.ShowCountry(actionId, fromTestRect, deckElement ?? cardTestCard, 0.77f, _actionConfig, _visualConfig, _loc, targetCountryId, warWinChancePercent);
				if (overlay != null) { overlay.style.display = DisplayStyle.None; }
				await deckTransitionTask;
				_transitionView.Hide();

				// Release or cancel gold/control/opinion barriers based on outcome.
				UniTask barrierTask = UniTask.CompletedTask;
				if (success && _barrierHolder != null) {
					var barrierTasks = new List<UniTask>();
					if (_barrierHolder.Has("gold")) {
						barrierTasks.Add(_barrierHolder.Animate("gold", 0.5f));
					}
					if (_barrierHolder.Has("control")) {
						barrierTasks.Add(_barrierHolder.Animate("control", 1.0f));
					}
					if (_barrierHolder.Has("opinion")) {
						barrierTasks.Add(_barrierHolder.Animate("opinion", 1.0f));
					}
					if (barrierTasks.Count > 0) {
						barrierTask = UniTask.WhenAll(barrierTasks);
					}
				} else {
					_barrierHolder?.CancelAll();
					_barrierHolder = null;
				}

				// Allow one Refresh to rebuild hand, then animate new card
				if (_countryActionsView != null) { _countryActionsView.SuppressRefresh = false; }
				await UniTask.NextFrame();
				if (_countryActionsView != null) { _countryActionsView.SuppressRefresh = true; }

				VisualElement newHandCard = null;
				if (_countryActionsView != null) {
					var handContainer = _countryActionsView.HandContainer;
					int childCount = handContainer?.childCount ?? 0;
					if (childCount > 1) {
						var lastWrapper = handContainer[childCount - 1];
						newHandCard = lastWrapper?.Q(className: "action-card");
					}
					if (newHandCard != null) { newHandCard.style.opacity = 0f; }
				}

				if (newHandCard != null) {
					string newActionId = "";
					string newTargetCountryId = "";
					int? newWarWinChancePercent = null;
					if (_state.SelectedCountry.CountryActions.Hand.Count > 0) {
						var newCard = _state.SelectedCountry.CountryActions.Hand[_state.SelectedCountry.CountryActions.Hand.Count - 1];
						newActionId = newCard.ActionId;
						newTargetCountryId = newCard.TargetCountryId;
						newWarWinChancePercent = newCard.WarWinChancePercent;
					}
					await _transitionView.ShowCountry(newActionId, deckRect, newHandCard, 0.5f, _actionConfig, _visualConfig, _loc, newTargetCountryId, newWarWinChancePercent);
					newHandCard.style.opacity = 1f;
					_transitionView.Hide();
				}

				if (_countryActionsView != null) { _countryActionsView.SuppressRefresh = false; }
				_modalState.Unlock(this);
				if (issuedPause) {
					_commands.Push(new UnpauseCommand());
					issuedPause = false;
				}
				await barrierTask;
				_barrierHolder = null;
				_isPlaying = false;
			} finally {
				_barrierHolder?.CancelAll();
				_barrierHolder = null;
				_transitionView.Hide();
				_modalState.Unlock(this);
				if (issuedPause) {
					_commands.Push(new UnpauseCommand());
				}
				if (_countryActionsView != null) {
					_countryActionsView.SuppressRefresh = false;
				}
				_isPlaying = false;
				OnCardPlayComplete?.Invoke();
			}
		}

		void PopulateCountryTestCard(VisualElement cardSlot, string actionId, string targetCountryId = "", int? warWinChancePercent = null) {
			if (cardSlot == null) { return; }
			var def = _actionConfig?.Find(actionId);
			string name;
			if (def == null) {
				name = actionId;
			} else if (!string.IsNullOrEmpty(targetCountryId)) {
				name = string.Format(_loc.Get(def.NameKey), _loc.Get($"country_name.{targetCountryId}"));
			} else {
				name = _loc.Get(def.NameKey);
			}
			string desc = def != null ? _loc.Get(def.DescKey) : "";
			string goldCostText = GetGoldCostText(def);
			ActionCardBuilder.PopulateSlot(cardSlot, name, desc, goldCostText, _visualConfig?.FindFront(actionId), warWinChancePercent);
		}

		void PopulateTestCard(VisualElement cardSlot, string actionId) {
			var def = _actionConfig?.Find(actionId);
			string name = def != null ? _loc.Get(def.NameKey) : actionId;
			string desc = def != null ? _loc.Get(def.DescKey) : "";
			string goldCostText = GetGoldCostText(def);
			ActionCardBuilder.PopulateSlot(cardSlot, name, desc, goldCostText, _visualConfig?.FindFront(actionId));
		}

		static string GetGoldCostText(GS.Game.Configs.ActionDefinition def) {
			if (def == null) { return null; }
			foreach (var c in def.Cost) {
				if (c.ResourceId == "gold") {
					return c.Amount == System.Math.Floor(c.Amount) ? $"{(int)c.Amount}" : $"{c.Amount:F1}";
				}
			}
			return null;
		}
	}
}

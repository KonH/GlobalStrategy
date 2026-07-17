using System;
using System.ComponentModel;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;
using GS.Main;
using GS.Game.Commands;
using GS.Game.Configs;
using GS.Unity.Common;
using GS.Unity.Map;

namespace GS.Unity.UI {
	public class CardPlayAnimator : MonoBehaviour {
		UIDocument _hudDocument;
		VisualState _state;
		IWriteOnlyCommandAccessor _commands;
		MapCameraController _cameraController;
		CountryConfig _domainConfig;
		ActionConfig _actionConfig;
		EffectConfig _effectConfig;
		ActionVisualConfig _visualConfig;
		ILocalization _loc;
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
			MapCameraController cameraController, CountryConfig domainConfig,
			ActionConfig actionConfig, EffectConfig effectConfig,
			ActionVisualConfig visualConfig, ILocalization loc) {
			_state = state;
			_commands = commands;
			_cameraController = cameraController;
			_domainConfig = domainConfig;
			_actionConfig = actionConfig;
			_effectConfig = effectConfig;
			_visualConfig = visualConfig;
			_loc = loc;
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
				if (effect.ResourceId == "gold") {
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

		public void StartCardPlay(string orgId, string actionId, VisualElement clickedCard) {
			if (_isPlaying) { return; }
			PlaySequence(orgId, actionId, clickedCard).Forget();
		}

		internal void SetActionsView(OrgActionsView view) {
			_actionsView = view;
		}

		internal void SetCountryActionsView(CountryActionsView view) {
			_countryActionsView = view;
		}

		public void StartCountryCardPlay(string orgId, string countryId, string actionId, VisualElement clickedCard) {
			if (_isPlaying) { return; }
			PlayCountrySequence(orgId, countryId, actionId, clickedCard).Forget();
		}

		async UniTaskVoid PlaySequence(string orgId, string actionId, VisualElement clickedCard) {
			_isPlaying = true;
			_resultReady = false;
			_lastActionSuccess = false;
			_barrierHolder = null;
			ModalState.IsModalOpen = true;
			if (_actionsView != null) { _actionsView.SuppressRefresh = true; }

			// Push action before pause so both are processed in the same game tick
			_commands.Push(new PlayCardActionCommand { OrgId = orgId, ActionId = actionId });
			_commands.Push(new PauseCommand());

			var root = _hudDocument.rootVisualElement;
			var overlay = root.Q("card-test-overlay");
			var cardTestCard = root.Q("card-test-card");
			var flyText = root.Q<Label>("fly-text");

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

			string discoveredCountryId = _state.DiscoveredCountries.RecentlyDiscovered;

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

			if (success && !string.IsNullOrEmpty(discoveredCountryId)) {
				_cameraController?.PanToCountry(discoveredCountryId);
				await UniTask.Delay(1000);

				if (flyText != null) {
					string localizedName = _loc.Get($"country_name.{discoveredCountryId}");
					if (string.IsNullOrEmpty(localizedName) || localizedName == $"country_name.{discoveredCountryId}") {
						localizedName = discoveredCountryId.Replace("_", " ");
					}
					flyText.text = $"Discovered: {localizedName}!";
					flyText.style.display = DisplayStyle.Flex;
					flyText.style.opacity = 0f;
					flyText.style.translate = new StyleTranslate(new Translate(Length.Percent(-50), Length.Percent(0)));

					float t = 0f;
					while (t < 0.5f) {
						t += Time.deltaTime;
						flyText.style.opacity = Mathf.Clamp01(t / 0.5f);
						await UniTask.NextFrame();
					}
					flyText.style.opacity = 1f;

					await UniTask.Delay(2000);

					t = 0f;
					while (t < 0.5f) {
						t += Time.deltaTime;
						flyText.style.opacity = 1f - Mathf.Clamp01(t / 0.5f);
						await UniTask.NextFrame();
					}
					flyText.style.display = DisplayStyle.None;
				}
			}

			_state.DiscoveredCountries.ClearRecentlyDiscovered();
			ModalState.IsModalOpen = false;
			_commands.Push(new UnpauseCommand());
			await goldTask;
			_barrierHolder = null;
			_isPlaying = false;
			OnCardPlayComplete?.Invoke();
		}

		async UniTaskVoid PlayCountrySequence(string orgId, string countryId, string actionId, VisualElement clickedCard) {
			_isPlaying = true;
			_resultReady = false;
			_lastActionSuccess = false;
			_barrierHolder = null;
			ModalState.IsModalOpen = true;

			if (_countryActionsView != null) { _countryActionsView.SuppressRefresh = true; }

			_commands.Push(new PlayCardActionCommand { OrgId = orgId, CountryId = countryId, ActionId = actionId });
			_commands.Push(new PauseCommand());

			var root = _hudDocument.rootVisualElement;
			var overlay = root.Q("card-test-overlay");
			var cardTestCard = root.Q("card-test-card");

			if (overlay != null) {
				PopulateCountryTestCard(cardTestCard, actionId);
				overlay.style.display = DisplayStyle.Flex;
				overlay.style.opacity = 0f;
				if (cardTestCard != null) { cardTestCard.style.opacity = 0f; }
			}

			var fromRect = clickedCard.worldBound;
			clickedCard.style.opacity = 0f;
			var deckRect = _countryActionsView?.DeckPileElement?.worldBound ?? Rect.zero;

			await _transitionView.ShowCountry(actionId, fromRect, cardTestCard, 0.7f, _actionConfig, _visualConfig, _loc);

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
			var deckTransitionTask = _transitionView.ShowCountry(actionId, fromTestRect, deckElement ?? cardTestCard, 0.77f, _actionConfig, _visualConfig, _loc);
			if (overlay != null) { overlay.style.display = DisplayStyle.None; }
			await deckTransitionTask;
			_transitionView.Hide();

			// Release or cancel control/opinion barriers based on outcome.
			UniTask barrierTask = UniTask.CompletedTask;
			if (success && _barrierHolder != null) {
				bool hasControl = _barrierHolder.Has("control");
				bool hasOpinion = _barrierHolder.Has("opinion");
				if (hasControl && hasOpinion) {
					barrierTask = UniTask.WhenAll(_barrierHolder.Animate("control", 1.0f), _barrierHolder.Animate("opinion", 1.0f));
				} else if (hasControl) {
					barrierTask = _barrierHolder.Animate("control", 1.0f);
				} else if (hasOpinion) {
					barrierTask = _barrierHolder.Animate("opinion", 1.0f);
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
				if (_state.SelectedCountry.CountryActions.Hand.Count > 0) {
					newActionId = _state.SelectedCountry.CountryActions.Hand[_state.SelectedCountry.CountryActions.Hand.Count - 1].ActionId;
				}
				await _transitionView.ShowCountry(newActionId, deckRect, newHandCard, 0.5f, _actionConfig, _visualConfig, _loc);
				newHandCard.style.opacity = 1f;
				_transitionView.Hide();
			}

			if (_countryActionsView != null) { _countryActionsView.SuppressRefresh = false; }
			ModalState.IsModalOpen = false;
			_commands.Push(new UnpauseCommand());
			await barrierTask;
			_barrierHolder = null;
			_isPlaying = false;
			OnCardPlayComplete?.Invoke();
		}

		void PopulateCountryTestCard(VisualElement cardSlot, string actionId) {
			if (cardSlot == null) { return; }
			var def = _actionConfig?.Find(actionId);
			string name = def != null ? _loc.Get(def.NameKey) : actionId;
			string desc = def != null ? _loc.Get(def.DescKey) : "";
			string goldCostText = GetGoldCostText(def);
			ActionCardBuilder.PopulateSlot(cardSlot, name, desc, goldCostText, _visualConfig?.FindFront(actionId));
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

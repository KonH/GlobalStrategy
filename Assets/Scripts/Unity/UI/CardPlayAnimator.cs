using System;
using System.Collections;
using System.ComponentModel;
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
		ActionVisualConfig _visualConfig;
		ILocalization _loc;
		bool _isPlaying;
		CardTransitionView _transitionView;
		OrgActionsView _actionsView;
		bool _resultReady;

		[Inject]
		void Construct(VisualState state, IWriteOnlyCommandAccessor commands,
			MapCameraController cameraController, CountryConfig domainConfig,
			ActionConfig actionConfig, ActionVisualConfig visualConfig, ILocalization loc) {
			_state = state;
			_commands = commands;
			_cameraController = cameraController;
			_domainConfig = domainConfig;
			_actionConfig = actionConfig;
			_visualConfig = visualConfig;
			_loc = loc;
		}

		void Awake() {
			_hudDocument = GetComponent<UIDocument>();
			var overlay = _hudDocument.rootVisualElement.Q("card-transition-overlay");
			if (overlay == null) {
				Debug.LogError("[CardPlayAnimator] card-transition-overlay not found in UIDocument.", this);
			}
			_transitionView = new CardTransitionView(overlay, this);
		}

		void OnEnable() {
			if (_state != null) {
				_state.LastAction.PropertyChanged += HandleLastActionChanged;
			}
		}

		void OnDisable() {
			if (_state != null) {
				_state.LastAction.PropertyChanged -= HandleLastActionChanged;
			}
		}

		void HandleLastActionChanged(object sender, PropertyChangedEventArgs e) {
			if (_state != null && _state.LastAction.HasResult) {
				_resultReady = true;
			}
		}

		public void StartCardPlay(string orgId, string actionId, VisualElement clickedCard) {
			if (_isPlaying) { return; }
			StartCoroutine(PlaySequence(orgId, actionId, clickedCard));
		}

		internal void SetActionsView(OrgActionsView view) {
			_actionsView = view;
		}

		IEnumerator PlaySequence(string orgId, string actionId, VisualElement clickedCard) {
			_isPlaying = true;
			_resultReady = false;
			ModalState.IsModalOpen = true;
			if (_actionsView != null) { _actionsView.SuppressRefresh = true; }
			// Push action before pause so both are processed in the same game tick
			_commands.Push(new PlayActionCommand { OwnerId = orgId, ActionId = actionId });
			_commands.Push(new PauseCommand());

			// Step 2: prepare card-test-overlay (visible but transparent)
			var root = _hudDocument.rootVisualElement;
			var overlay = root.Q("card-test-overlay");
			var cardTestCard = root.Q("card-test-card");
			var rollBlock = root.Q("roll-block");
			var rollLabel = root.Q<Label>("roll-result-label");
			var flyText = root.Q<Label>("fly-text");

			if (overlay != null) {
				PopulateTestCard(cardTestCard, actionId);
				overlay.style.display = DisplayStyle.Flex;
				overlay.style.opacity = 0f;
				if (cardTestCard != null) {
					cardTestCard.style.opacity = 0f;
				}
			}

			// Step 3: transition card from hand position to test slot
			bool transitionDone = false;
			var fromRect = clickedCard.worldBound;
			clickedCard.style.opacity = 0f;

			// Step 5: capture deck rect before any state change
			var deckRect = _actionsView?.DeckPileElement?.worldBound ?? Rect.zero;

			_transitionView.Show(actionId, fromRect, cardTestCard, 0.77f,
				_actionConfig, _visualConfig, _loc, () => transitionDone = true);

			// Step 6: wait for card to reach test position
			while (!transitionDone) { yield return null; }

			if (overlay != null) {
				overlay.style.opacity = 1f;
			}
			if (cardTestCard != null) {
				cardTestCard.style.opacity = 1f;
			}
			_transitionView.Hide();

			// Step 7: animate roll, wait for result
			if (rollBlock != null) {
				rollBlock.style.display = DisplayStyle.Flex;
				rollBlock.style.opacity = 0f;
				float t = 0f;
				while (t < 0.3f) {
					t += Time.deltaTime;
					rollBlock.style.opacity = Mathf.Clamp01(t / 0.3f);
					yield return null;
				}
				rollBlock.style.opacity = 1f;
			}

			float startTime = Time.time;
			while (Time.time - startTime < 2f || !_resultReady) {
				if (rollLabel != null) {
					rollLabel.text = $"{UnityEngine.Random.Range(1, 101)}%";
				}
				yield return new WaitForSeconds(0.33f);
				if (Time.time - startTime > 10f) { break; }
			}

			if (!_resultReady) {
				Debug.LogWarning("[CardPlayAnimator] Timed out waiting for action result.");
			}
			bool success = _state.LastAction.Success;
			string discoveredCountryId = _state.DiscoveredCountries.RecentlyDiscovered;

			if (rollLabel != null) {
				rollLabel.text = success ? "Success!" : "Fail!";
				rollLabel.style.color = success
					? new StyleColor(new Color(0.4f, 0.9f, 0.4f))
					: new StyleColor(new Color(0.9f, 0.3f, 0.3f));
			}
			yield return new WaitForSeconds(0.5f);

			// Step 8: transition card from test slot to deck
			transitionDone = false;
			var fromTestRect = cardTestCard != null ? cardTestCard.worldBound : Rect.zero;
			var deckElement = _actionsView?.DeckPileElement;

			_transitionView.Show(actionId, fromTestRect, deckElement ?? cardTestCard, 0.77f,
				_actionConfig, _visualConfig, _loc, () => transitionDone = true);

			// Step 9: hide test overlay and roll block
			if (overlay != null) {
				overlay.style.display = DisplayStyle.None;
			}
			if (rollBlock != null) {
				rollBlock.style.display = DisplayStyle.None;
			}

			// Step 10: wait for card-to-deck transition
			while (!transitionDone) { yield return null; }
			_transitionView.Hide();

			// Step 11: allow one Refresh() to rebuild hand with new card, then re-suppress
			if (_actionsView != null) { _actionsView.SuppressRefresh = false; }
			yield return null;
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

			// Step 12: transition from deck to new hand card position
			if (newHandCard != null) {
				transitionDone = false;
				string newActionId = "";
				if (_state.PlayerOrgActions.Hand.Count > 0) {
					newActionId = _state.PlayerOrgActions.Hand[_state.PlayerOrgActions.Hand.Count - 1].ActionId;
				}
				_transitionView.Show(newActionId, deckRect, newHandCard, 0.5f,
					_actionConfig, _visualConfig, _loc, () => transitionDone = true);

				// Step 13: wait for transition, then show new card
				while (!transitionDone) { yield return null; }
				newHandCard.style.opacity = 1f;
				_transitionView.Hide();
			}
			if (_actionsView != null) {
				_actionsView.SuppressRefresh = false;
			}

			// Step 14: show discovery fly-text if success
			if (success && !string.IsNullOrEmpty(discoveredCountryId)) {
				_cameraController?.PanToCountry(discoveredCountryId);
				yield return new WaitForSeconds(1f);

				if (flyText != null) {
					string name = discoveredCountryId.Replace("_", " ");
					flyText.text = $"Discovered: {name}!";
					flyText.style.display = DisplayStyle.Flex;
					flyText.style.opacity = 0f;
					flyText.style.translate = new StyleTranslate(new Translate(Length.Percent(-50), Length.Percent(20)));

					float t = 0f;
					while (t < 0.4f) {
						t += Time.deltaTime;
						float progress = Mathf.Clamp01(t / 0.4f);
						flyText.style.opacity = progress;
						float yPercent = Mathf.Lerp(20f, -50f, progress);
						flyText.style.translate = new StyleTranslate(new Translate(Length.Percent(-50), Length.Percent(yPercent)));
						yield return null;
					}
					flyText.style.opacity = 1f;
					flyText.style.translate = new StyleTranslate(new Translate(Length.Percent(-50), Length.Percent(-50)));

					yield return new WaitForSeconds(1.5f);

					t = 0f;
					while (t < 0.4f) {
						t += Time.deltaTime;
						float progress = Mathf.Clamp01(t / 0.4f);
						flyText.style.opacity = 1f - progress;
						float yPercent = Mathf.Lerp(-50f, -120f, progress);
						flyText.style.translate = new StyleTranslate(new Translate(Length.Percent(-50), Length.Percent(yPercent)));
						yield return null;
					}
					flyText.style.display = DisplayStyle.None;
				}
			}

			// Step 15: unlock
			_state.DiscoveredCountries.ClearRecentlyDiscovered();
			_state.LastAction.Clear();
			ModalState.IsModalOpen = false;
			_commands.Push(new UnpauseCommand());
			_isPlaying = false;
		}

		void PopulateTestCard(VisualElement cardSlot, string actionId) {
			cardSlot.Clear();

			var def = _actionConfig?.Find(actionId);
			string name = def != null ? _loc.Get(def.NameKey) : actionId;

			var nameLabel = new Label(name);
			nameLabel.AddToClassList("action-card-name");
			cardSlot.Add(nameLabel);

			var img = new VisualElement();
			img.AddToClassList("action-card-image");
			var sprite = _visualConfig?.FindFront(actionId);
			if (sprite != null) {
				img.style.backgroundImage = new StyleBackground(sprite);
			}
			cardSlot.Add(img);
		}
	}
}

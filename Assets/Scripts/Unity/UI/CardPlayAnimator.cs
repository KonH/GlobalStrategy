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
			if (_state == null || !_state.LastAction.HasResult || _isPlaying) { return; }
			string discoveredCountryId = _state.DiscoveredCountries.RecentlyDiscovered;
			StartCoroutine(PlaySequence(_state.LastAction.Success, _state.LastAction.ActionId, discoveredCountryId));
		}

		IEnumerator PlaySequence(bool success, string actionId, string discoveredCountryId) {
			_isPlaying = true;
			ModalState.IsModalOpen = true;
			_commands.Push(new PauseCommand());

			if (_hudDocument != null) {
				var root = _hudDocument.rootVisualElement;
				var overlay = root.Q("card-test-overlay");
				var rollBlock = root.Q("roll-block");
				var rollLabel = root.Q<Label>("roll-result-label");
				var flyText = root.Q<Label>("fly-text");
				var cardSlot = root.Q("card-test-card");

				if (overlay != null) {
					if (cardSlot != null) {
						PopulateTestCard(cardSlot, actionId);
					}

					overlay.style.display = DisplayStyle.Flex;
					overlay.style.opacity = 0f;

					float t = 0f;
					while (t < 0.4f) {
						t += Time.deltaTime;
						overlay.style.opacity = Mathf.Clamp01(t / 0.4f);
						yield return null;
					}
					overlay.style.opacity = 1f;

					if (rollBlock != null) {
						rollBlock.style.display = DisplayStyle.Flex;
						rollBlock.style.opacity = 0f;
						t = 0f;
						while (t < 0.3f) {
							t += Time.deltaTime;
							rollBlock.style.opacity = Mathf.Clamp01(t / 0.3f);
							yield return null;
						}
					}

					float elapsed = 0f;
					while (elapsed < 2f) {
						if (rollLabel != null) {
							rollLabel.text = $"{UnityEngine.Random.Range(1, 101)}%";
						}
						yield return new WaitForSeconds(0.33f);
						elapsed += 0.33f;
					}

					if (rollLabel != null) {
						rollLabel.text = success ? "Success!" : "Fail!";
						rollLabel.style.color = success
							? new StyleColor(new Color(0.4f, 0.9f, 0.4f))
							: new StyleColor(new Color(0.9f, 0.3f, 0.3f));
					}
					yield return new WaitForSeconds(0.5f);

					if (rollBlock != null) {
						rollBlock.style.display = DisplayStyle.None;
					}
					yield return new WaitForSeconds(0.3f);

					t = 0f;
					while (t < 0.4f) {
						t += Time.deltaTime;
						overlay.style.opacity = Mathf.Clamp01(1f - t / 0.4f);
						yield return null;
					}
					overlay.style.display = DisplayStyle.None;

					if (success && !string.IsNullOrEmpty(discoveredCountryId)) {
						_cameraController?.PanToCountry(discoveredCountryId);
						yield return new WaitForSeconds(1f);

						if (flyText != null) {
							string name = discoveredCountryId.Replace("_", " ");
							flyText.text = $"Discovered: {name}!";
							flyText.style.display = DisplayStyle.Flex;
							flyText.style.opacity = 0f;
							flyText.style.translate = new StyleTranslate(new Translate(Length.Percent(-50), Length.Percent(20)));

							t = 0f;
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
				}
			}

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

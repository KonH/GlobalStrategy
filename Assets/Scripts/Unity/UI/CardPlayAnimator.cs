using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
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
		PanelRenderer _hudDocument;
		VisualElement _root;
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
		FlowContext _activeContext;

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
			_hudDocument = GetComponent<PanelRenderer>();
			if (_hudDocument == null) {
				Debug.LogError("[CardPlayAnimator] missing PanelRenderer.", this);
				return;
			}
		}

		void OnDestroy() {
			CancelAndWaitAsync().Forget();
		}

		// HUDDocument is the sole reload coordinator: it calls this only after any old play flow has
		// been cancelled and awaited via CancelAndWaitAsync, so _activeContext is guaranteed null here.
		internal void BindRoot(VisualElement rootElement) {
			_root = rootElement;
			var overlay = _root.Q("card-transition-overlay");
			if (overlay == null) {
				Debug.LogError("[CardPlayAnimator] card-transition-overlay not found in PanelRenderer.", this);
			}
			_transitionView = new CardTransitionView(overlay);
		}

		void OnEnable() {
			if (_root == null && _hudDocument != null) {
				BindRoot(PanelRendererRoot.Get(_hudDocument));
			}
			if (_state != null) {
				_state.LastFrameEffects.PropertyChanged += HandleLastFrameEffectsChanged;
			}
		}

		void OnDisable() {
			if (_state != null) {
				_state.LastFrameEffects.PropertyChanged -= HandleLastFrameEffectsChanged;
			}
			CancelAndWaitAsync().Forget();
		}

		public async UniTask CancelAndWaitAsync() {
			FlowContext context = _activeContext;
			if (context == null) {
				return;
			}
			context.Cancellation.Cancel();
			await context.Completion.Task;
		}

		void HandleLastFrameEffectsChanged(object sender, PropertyChangedEventArgs e) {
			if (_state == null || _state.LastFrameEffects.Effects.Count == 0) { return; }

			// Only the player's own card-play sequence (PlaySequence/PlayCountrySequence) ever
			// releases or cancels these barriers. Effects from bot-driven plays reach this handler
			// too (LastFrameEffects is global, not player-scoped), but with no matching Animate/CancelAll
			// call to follow, a barrier created here would sit on the currently selected country's
			// UsedControl forever, permanently offsetting its Display value.
			if (!_isPlaying) { return; }

			// Reuse the in-flight holder across multiple fires instead of replacing it: this handler
			// re-runs on *any* LastFrameEffects change while a card is playing, including ones wholly
			// unrelated to the player's own card (e.g. a bot's card resolving in the same window).
			// Unconditionally assigning a fresh CardPlayBarriersHolder here discarded the reference to
			// any barrier already added for the player's own card, orphaning it on the underlying
			// AnimatableDouble/AnimatableInt forever — no code path is left to Animate/CancelAll it,
			// so its offset never decays and the HUD counter sticks at the stale, pre-change value.
			if (_barrierHolder == null) {
				_barrierHolder = new CardPlayBarriersHolder();
			}
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
			ActionCardBuilder.CountryCardFace faceData,
			string targetCountryId = "") {
			if (_isPlaying) { return; }
			if (faceData == null) {
				throw new ArgumentNullException(nameof(faceData));
			}
			PlayCountrySequence(orgId, countryId, actionId, slotIndex, clickedCard, faceData, targetCountryId).Forget();
		}

		FlowContext BeginFlow(
			VisualElement clickedCard,
			VisualElement overlay,
			VisualElement testCard,
			OrgActionsView orgActionsView,
			CountryActionsView countryActionsView,
			bool priorSuppressRefresh) {
			// _isPlaying already blocks StartCardPlay/StartCountryCardPlay from starting an overlapping
			// flow, so _activeContext should never be non-null here; dispose defensively anyway,
			// mirroring CardDrawAnimator.BeginFlow, in case that guard is ever bypassed.
			if (_activeContext != null) {
				_activeContext.Cancellation.Cancel();
				_activeContext.Cancellation.Dispose();
				_activeContext.Completion.TrySetResult();
			}
			var cancellation = new CancellationTokenSource();
			var completion = new UniTaskCompletionSource();
			var context = new FlowContext(
				cancellation,
				completion,
				_transitionView,
				clickedCard,
				overlay,
				testCard,
				orgActionsView,
				countryActionsView,
				priorSuppressRefresh);
			_activeContext = context;
			return context;
		}

		// The single cleanup owner for both PlaySequence and PlayCountrySequence's finally blocks.
		// Uses only the flow context's captured references, never the animator's live _transitionView/
		// _actionsView/_countryActionsView fields, which may have been rebound by a reload since this
		// flow started.
		void FinishFlow(FlowContext context) {
			_barrierHolder?.CancelAll();
			_barrierHolder = null;

			context.TransitionView.Hide();

			// Only touch the captured test-overlay/test-card if they still belong to the flow's old
			// root; a reload since this flow started may have torn the whole tree down already.
			if (context.Overlay != null && context.Overlay.panel != null) {
				context.Overlay.style.display = DisplayStyle.None;
				context.Overlay.style.opacity = 0f;
				if (context.TestCard != null && context.TestCard.panel != null) {
					context.TestCard.style.opacity = 1f;
				}
			}

			// Restore the hidden source card only if it is still attached; otherwise the authoritative
			// refresh already rebuilt (or removed) it and there is nothing stale left to fix up.
			if (context.ClickedCard != null && context.ClickedCard.panel != null) {
				context.ClickedCard.style.opacity = 1f;
			}

			// Restore only the exact view instance this flow captured - never the animator's current
			// _actionsView/_countryActionsView fields, which may differ after a rebind.
			if (context.OrgActionsView != null) {
				context.OrgActionsView.SuppressRefresh = context.PriorSuppressRefresh;
			}
			if (context.CountryActionsView != null) {
				context.CountryActionsView.SuppressRefresh = context.PriorSuppressRefresh;
			}

			_modalState.Unlock(this);
			if (context.IssuedPause) {
				_commands.Push(new UnpauseCommand());
			}

			_isPlaying = false;
			// _isPlaying already prevents a second flow from starting while this one is in flight, and
			// BeginFlow defensively finishes any stale context before handing out a new one, so this
			// context is always still the current one here - the ReferenceEquals check below is a
			// defensive no-op guard rather than a real generation mechanism.
			if (ReferenceEquals(_activeContext, context)) {
				_activeContext = null;
			}
			context.Cancellation.Dispose();
			context.Completion.TrySetResult();
			OnCardPlayComplete?.Invoke();
		}

		async UniTaskVoid PlaySequence(string orgId, string actionId, int slotIndex, VisualElement clickedCard) {
			_isPlaying = true;
			_resultReady = false;
			_lastActionSuccess = false;
			_barrierHolder = null;

			var root = _root ?? PanelRendererRoot.Get(_hudDocument);
			var overlay = root.Q("card-test-overlay");
			var cardTestCard = root.Q("card-test-card");
			bool priorSuppressRefresh = _actionsView != null && _actionsView.SuppressRefresh;
			FlowContext context = BeginFlow(clickedCard, overlay, cardTestCard, _actionsView, null, priorSuppressRefresh);

			_modalState.Lock(this);
			context.IssuedPause = !_state.Time.IsPaused;
			if (_actionsView != null) { _actionsView.SuppressRefresh = true; }

			try {
				// Push action before pause so both are processed in the same game tick
				_commands.Push(new PlayCardActionCommand { OrgId = orgId, ActionId = actionId, SlotIndex = slotIndex });
				if (context.IssuedPause) {
					_commands.Push(new PauseCommand());
				}

				if (context.Overlay != null) {
					PopulateTestCard(context.TestCard, actionId);
					context.Overlay.style.display = DisplayStyle.Flex;
					context.Overlay.style.opacity = 0f;
					if (context.TestCard != null) {
						context.TestCard.style.opacity = 0f;
					}
				}

				var fromRect = clickedCard.worldBound;
				clickedCard.style.opacity = 0f;

				// Capture deck rect before any state change
				var deckRect = _actionsView?.DeckPileElement?.worldBound ?? Rect.zero;

				await context.TransitionView.Show(
					actionId, fromRect, context.TestCard, 0.7f, _actionConfig, _visualConfig, _loc, context.Token);

				if (context.Overlay != null) {
					context.Overlay.style.opacity = 1f;
				}
				if (context.TestCard != null) {
					context.TestCard.style.opacity = 1f;
				}
				context.TransitionView.Hide();

				float startTime = Time.realtimeSinceStartup;
				while (!_resultReady) {
					await UniTask.Delay(330, DelayType.UnscaledDeltaTime, cancellationToken: context.Token);
					if (Time.realtimeSinceStartup - startTime > 10f) { break; }
				}

				if (!_resultReady) {
					Debug.LogWarning("[CardPlayAnimator] Timed out waiting for action result.");
				}
				bool success = _lastActionSuccess;

				// Release or cancel gold barrier based on outcome.
				// Barrier was created in HandleLastFrameEffectsChanged before SetActual fired.
				UniTask goldTask = UniTask.CompletedTask;
				if (success && _barrierHolder != null && _barrierHolder.Has("gold")) {
					goldTask = _barrierHolder.Animate("gold", 0.5f, context.Token);
				} else {
					_barrierHolder?.CancelAll();
					_barrierHolder = null;
				}

				await UniTask.Delay(700, DelayType.UnscaledDeltaTime, cancellationToken: context.Token);

				// Start card-to-deck transition, then hide overlay concurrently before awaiting
				var fromTestRect = context.TestCard != null ? context.TestCard.worldBound : Rect.zero;
				var deckElement = _actionsView?.DeckPileElement;
				var deckTransitionTask = context.TransitionView.Show(
					actionId, fromTestRect, deckElement ?? context.TestCard, 0.77f, _actionConfig, _visualConfig, _loc, context.Token);
				if (context.Overlay != null) { context.Overlay.style.display = DisplayStyle.None; }
				await deckTransitionTask;
				context.TransitionView.Hide();

				// Rebuild hand with the new card synchronously (bypassing SuppressRefresh just for this
				// call) so it can be hidden again before any frame renders it at full opacity.
				VisualElement newHandCard = null;
				if (_actionsView != null) {
					_actionsView.SuppressRefresh = false;
					_actionsView.Refresh(_state.PlayerOrganization.Actions, _state.PlayerOrganization.Resources);
					_actionsView.SuppressRefresh = true;

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
				// Settle layout for the now-hidden card before reading its worldBound below.
				await UniTask.NextFrame(cancellationToken: context.Token);

				if (newHandCard != null) {
					string newActionId = "";
					if (_state.PlayerOrganization.Actions.Hand.Count > 0) {
						newActionId = _state.PlayerOrganization.Actions.Hand[_state.PlayerOrganization.Actions.Hand.Count - 1].ActionId;
					}
					await context.TransitionView.Show(
						newActionId, deckRect, newHandCard, 0.5f, _actionConfig, _visualConfig, _loc, context.Token);
					newHandCard.style.opacity = 1f;
					context.TransitionView.Hide();
				}
				if (_actionsView != null) {
					_actionsView.SuppressRefresh = false;
				}

				// Unlock the modal and unpause as soon as the visible sequence is done, without waiting
				// for the gold barrier's cosmetic release animation below - FinishFlow's matching
				// restores are guarded to no-op for what is already restored here. _isPlaying stays true
				// until the barrier finishes so a second play can't start and race this one's
				// _barrierHolder (see FinishFlow's cancellation of it below).
				_modalState.Unlock(this);
				if (context.IssuedPause) {
					_commands.Push(new UnpauseCommand());
					context.IssuedPause = false;
				}

				await goldTask;
			} catch (OperationCanceledException) {
				// Expected on cancellation/detachment; FinishFlow below owns all cleanup.
			} finally {
				FinishFlow(context);
			}
		}

		async UniTaskVoid PlayCountrySequence(
			string orgId,
			string countryId,
			string actionId,
			int slotIndex,
			VisualElement clickedCard,
			ActionCardBuilder.CountryCardFace faceData,
			string targetCountryId = "") {
			_isPlaying = true;
			_resultReady = false;
			_lastActionSuccess = false;
			_barrierHolder = null;

			var root = _root ?? PanelRendererRoot.Get(_hudDocument);
			var overlay = root.Q("card-test-overlay");
			var cardTestCard = root.Q("card-test-card");
			bool priorSuppressRefresh = _countryActionsView != null && _countryActionsView.SuppressRefresh;
			FlowContext context = BeginFlow(clickedCard, overlay, cardTestCard, null, _countryActionsView, priorSuppressRefresh);

			_modalState.Lock(this);
			context.IssuedPause = !_state.Time.IsPaused;

			if (_countryActionsView != null) { _countryActionsView.SuppressRefresh = true; }

			try {
				_commands.Push(new PlayCardActionCommand {
					OrgId = orgId,
					CountryId = countryId,
					ActionId = actionId,
					TargetCountryId = targetCountryId,
					SlotIndex = slotIndex
				});
				if (context.IssuedPause) {
					_commands.Push(new PauseCommand());
				}

				if (context.Overlay != null) {
					PopulateCountryTestCard(context.TestCard, faceData);
					context.Overlay.style.display = DisplayStyle.Flex;
					context.Overlay.style.opacity = 0f;
					if (context.TestCard != null) { context.TestCard.style.opacity = 0f; }
				}

				var fromRect = clickedCard.worldBound;
				clickedCard.style.opacity = 0f;

				await context.TransitionView.ShowCountry(faceData, fromRect, context.TestCard, 0.7f, context.Token);

				if (context.Overlay != null) { context.Overlay.style.opacity = 1f; }
				if (context.TestCard != null) { context.TestCard.style.opacity = 1f; }
				context.TransitionView.Hide();

				float startTime = Time.realtimeSinceStartup;
				while (!_resultReady) {
					await UniTask.Delay(330, DelayType.UnscaledDeltaTime, cancellationToken: context.Token);
					if (Time.realtimeSinceStartup - startTime > 10f) { break; }
				}

				if (!_resultReady) { Debug.LogWarning("[CardPlayAnimator] Country action timed out waiting for result."); }
				bool success = _lastActionSuccess;

				await UniTask.Delay(700, DelayType.UnscaledDeltaTime, cancellationToken: context.Token);

				// Start card-to-deck transition, then hide overlay concurrently before awaiting
				var fromTestRect = context.TestCard != null ? context.TestCard.worldBound : Rect.zero;
				var deckElement = _countryActionsView?.DeckPileElement;
				var deckTransitionTask = context.TransitionView.ShowCountry(
					faceData, fromTestRect, deckElement ?? context.TestCard, 0.77f, context.Token);
				if (context.Overlay != null) { context.Overlay.style.display = DisplayStyle.None; }
				await deckTransitionTask;
				context.TransitionView.Hide();

				// Release or cancel gold/control/opinion barriers based on outcome.
				UniTask barrierTask = UniTask.CompletedTask;
				if (success && _barrierHolder != null) {
					var barrierTasks = new List<UniTask>();
					if (_barrierHolder.Has("gold")) {
						barrierTasks.Add(_barrierHolder.Animate("gold", 0.5f, context.Token));
					}
					if (_barrierHolder.Has("control")) {
						barrierTasks.Add(_barrierHolder.Animate("control", 1.0f, context.Token));
					}
					if (_barrierHolder.Has("opinion")) {
						barrierTasks.Add(_barrierHolder.Animate("opinion", 1.0f, context.Token));
					}
					if (barrierTasks.Count > 0) {
						barrierTask = UniTask.WhenAll(barrierTasks);
					}
				} else {
					_barrierHolder?.CancelAll();
					_barrierHolder = null;
				}

				// The country-card vacancy remains until the player explicitly draws.
				if (_countryActionsView != null) { _countryActionsView.SuppressRefresh = false; }
				await UniTask.NextFrame(cancellationToken: context.Token);

				// Unlock the modal and unpause as soon as the visible sequence is done, without waiting
				// for the barriers' cosmetic release animations below - FinishFlow's matching restores
				// are guarded to no-op for what is already restored here. _isPlaying stays true until
				// the barriers finish so a second play can't start and race this one's _barrierHolder
				// (see FinishFlow's cancellation of it below).
				_modalState.Unlock(this);
				if (context.IssuedPause) {
					_commands.Push(new UnpauseCommand());
					context.IssuedPause = false;
				}

				await barrierTask;
			} catch (OperationCanceledException) {
				// Expected on cancellation/detachment; FinishFlow below owns all cleanup.
			} finally {
				FinishFlow(context);
			}
		}

		void PopulateCountryTestCard(VisualElement cardSlot, ActionCardBuilder.CountryCardFace faceData) {
			if (cardSlot == null) { return; }
			ActionCardBuilder.PopulateSlot(cardSlot, faceData, false);
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

		// Holds exactly what one PlaySequence/PlayCountrySequence invocation needs for cancellation and
		// invocation-safe cleanup - the transition view active when the flow started (which _transitionView
		// may later be replaced out from under, via OnUIReload), the real source card element, the
		// card-test-overlay/card-test-card elements from that flow's root, and the one org/country actions
		// view whose refresh suppression this flow changed, plus its prior value.
		sealed class FlowContext {
			public CancellationTokenSource Cancellation { get; }
			public UniTaskCompletionSource Completion { get; }
			public CancellationToken Token => Cancellation.Token;
			public CardTransitionView TransitionView { get; }
			public VisualElement ClickedCard { get; }
			public VisualElement Overlay { get; }
			public VisualElement TestCard { get; }
			public OrgActionsView OrgActionsView { get; }
			public CountryActionsView CountryActionsView { get; }
			public bool PriorSuppressRefresh { get; }
			public bool IssuedPause { get; set; }

			public FlowContext(
				CancellationTokenSource cancellation,
				UniTaskCompletionSource completion,
				CardTransitionView transitionView,
				VisualElement clickedCard,
				VisualElement overlay,
				VisualElement testCard,
				OrgActionsView orgActionsView,
				CountryActionsView countryActionsView,
				bool priorSuppressRefresh) {
				Cancellation = cancellation;
				Completion = completion;
				TransitionView = transitionView;
				ClickedCard = clickedCard;
				Overlay = overlay;
				TestCard = testCard;
				OrgActionsView = orgActionsView;
				CountryActionsView = countryActionsView;
				PriorSuppressRefresh = priorSuppressRefresh;
			}
		}
	}
}

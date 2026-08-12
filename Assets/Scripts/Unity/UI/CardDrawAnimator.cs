using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using GS.Game.Commands;
using GS.Main;
using GS.Unity.Common;

namespace GS.Unity.UI {
	class CardDrawAnimator {
		const float DrawModalSortingOrder = 1050f;
		const float CommandTimeoutSeconds = 10f;
		const float PauseReleaseTimeoutSeconds = 2f;

		readonly VisualState _state;
		readonly IWriteOnlyCommandAccessor _commands;
		readonly ModalState _modalState;
		readonly UIDocument _document;
		readonly CountryInfoView _countryInfo;
		readonly CountryActionsView _actionsView;
		readonly CardDrawView _view;
		CancellationTokenSource _flowCancellation;
		UniTaskCompletionSource _flowCompletion;
		FlowContext _activeContext;
		object _resumeBarrierOwner;
		float _resumePreviousSortingOrder;
		int _generation;
		bool _isPlaying;
		bool _restorationEnabled;

		public bool IsPlaying => _isPlaying;

		public void SetRestorationEnabled(bool enabled) {
			_restorationEnabled = enabled;
		}

		public void BeginResumeBarrier() {
			CountryActionsState actions = _state.SelectedCountry.CountryActions;
			if (_resumeBarrierOwner != null
				|| (!_isPlaying && (!actions.HasPendingDraw || actions.DrawChoices.Count == 0))) {
				return;
			}
			_resumeBarrierOwner = new object();
			_resumePreviousSortingOrder = _activeContext?.PreviousSortingOrder ?? _document.sortingOrder;
			_modalState.Lock(_resumeBarrierOwner);
			_document.sortingOrder = DrawModalSortingOrder;
			_actionsView.SetPresentationBusy(true);
			_view.ShowShield();
		}

		public void EndResumeBarrier() {
			if (_resumeBarrierOwner == null) {
				return;
			}
			_modalState.Unlock(_resumeBarrierOwner);
			_resumeBarrierOwner = null;
			_view.Cleanup();
			_document.sortingOrder = _resumePreviousSortingOrder;
			_actionsView.SetPresentationBusy(false);
		}

		public CardDrawAnimator(
			VisualState state,
			IWriteOnlyCommandAccessor commands,
			ModalState modalState,
			UIDocument document,
			CountryInfoView countryInfo,
			CountryActionsView actionsView,
			CardDrawView view) {
			_state = state ?? throw new ArgumentNullException(nameof(state));
			_commands = commands ?? throw new ArgumentNullException(nameof(commands));
			_modalState = modalState ?? throw new ArgumentNullException(nameof(modalState));
			_document = document ?? throw new ArgumentNullException(nameof(document));
			_countryInfo = countryInfo ?? throw new ArgumentNullException(nameof(countryInfo));
			_actionsView = actionsView ?? throw new ArgumentNullException(nameof(actionsView));
			_view = view ?? throw new ArgumentNullException(nameof(view));
		}

		public bool StartExplicitDraw() {
			if (!CanStartFlow() || !_state.SelectedCountry.CountryActions.CanStartDraw) {
				return false;
			}
			FlowContext context = BeginFlow();
			RunExplicitDrawAsync(context).Forget();
			return true;
		}

		public bool StartPaidDiscard(
			string orgId,
			string countryId,
			string actionId,
			int slotIndex,
			VisualElement clickedCard,
			ActionCardBuilder.CountryCardFace face,
			string targetCountryId) {
			if (!CanStartFlow() || clickedCard == null || face == null) {
				return false;
			}
			FlowContext context = BeginFlow();
			context.HiddenElement = clickedCard;
			clickedCard.style.opacity = 0f;
			RunPaidDiscardAsync(
				context,
				orgId,
				countryId,
				actionId,
				slotIndex,
				clickedCard.worldBound,
				face,
				targetCountryId ?? "").Forget();
			return true;
		}

		public void RestorePendingOfferIfIdle() {
			CountryActionsState actions = _state.SelectedCountry.CountryActions;
			if (!_restorationEnabled
				|| _isPlaying
				|| !_state.PlayerOrganization.IsValid
				|| !_state.SelectedCountry.IsValid
				|| !actions.HasPendingDraw
				|| actions.DrawChoices.Count == 0) {
				return;
			}
			_countryInfo.EnsureActionsOpen();
			_actionsView.Refresh(actions, _state.PlayerOrganization.Resources);
			FlowContext context = BeginFlow();
			RunRestoredOfferAsync(context).Forget();
		}

		public void RefreshPendingOfferPresentation() {
			if (!_state.SelectedCountry.CountryActions.HasPendingDraw) {
				return;
			}
			if (_isPlaying) {
				_view.RefreshChoiceFaces(BuildFace);
				return;
			}
			RestorePendingOfferIfIdle();
		}

		public async UniTask CancelAndWaitAsync() {
			CancellationTokenSource cancellation = _flowCancellation;
			UniTaskCompletionSource completion = _flowCompletion;
			if (cancellation == null || completion == null) {
				return;
			}
			cancellation.Cancel();
			await completion.Task;
		}

		bool CanStartFlow() =>
			!_isPlaying
			&& _state.PlayerOrganization.IsValid
			&& _state.SelectedCountry.IsValid
			&& _actionsView.DeckPileElement != null;

		FlowContext BeginFlow() {
			_generation++;
			_flowCancellation?.Dispose();
			var cancellation = new CancellationTokenSource();
			var completion = new UniTaskCompletionSource();
			_flowCancellation = cancellation;
			_flowCompletion = completion;
			var context = new FlowContext(
				_generation,
				new FlowOwner(_generation),
				cancellation,
				completion,
				_document.sortingOrder,
				!_state.Time.IsPaused);
			_activeContext = context;
			_isPlaying = true;
			_actionsView.SetPresentationBusy(true);
			_actionsView.SuppressRefresh = true;
			_document.sortingOrder = DrawModalSortingOrder;
			_view.ShowShield();
			_modalState.Lock(context.Owner);
			return context;
		}

		async UniTaskVoid RunExplicitDrawAsync(FlowContext context) {
			try {
				_commands.Push(new DrawCardsCommand {
					OrgId = _state.PlayerOrganization.OrgId
				});
				PushPauseIfOwned(context);
				bool hasOffer = await WaitForOfferAsync(context.Token);
				if (!hasOffer) {
					Debug.LogWarning("[CardDrawAnimator] Timed out waiting for a draw offer.");
					return;
				}
				await ResolveOfferAsync(context, animateDeal: true);
			} catch (OperationCanceledException) {
				// Cleanup below restores only this flow's presentation ownership.
			} catch (Exception exception) {
				Debug.LogError($"[CardDrawAnimator] Explicit draw failed: {exception}");
			} finally {
				await FinishFlowAsync(context);
			}
		}

		async UniTaskVoid RunRestoredOfferAsync(FlowContext context) {
			try {
				PushPauseIfOwned(context);
				await ResolveOfferAsync(context, animateDeal: false);
			} catch (OperationCanceledException) {
				// Cleanup below restores only this flow's presentation ownership.
			} catch (Exception exception) {
				Debug.LogError($"[CardDrawAnimator] Restored draw offer failed: {exception}");
			} finally {
				await FinishFlowAsync(context);
			}
		}

		async UniTaskVoid RunPaidDiscardAsync(
			FlowContext context,
			string orgId,
			string countryId,
			string actionId,
			int slotIndex,
			Rect originalRect,
			ActionCardBuilder.CountryCardFace face,
			string targetCountryId) {
			try {
				_commands.Push(new DiscardCardCommand {
					OrgId = orgId,
					CountryId = countryId,
					ActionId = actionId,
					TargetCountryId = targetCountryId,
					SlotIndex = slotIndex
				});
				PushPauseIfOwned(context);
				await _view.AnimatePaidDiscardAsync(
					face,
					originalRect,
					_actionsView.DeckPileElement,
					context.Token);

				DiscardOutcome outcome = await WaitForDiscardOutcomeAsync(
					actionId,
					targetCountryId,
					slotIndex,
					context.Token);
				if (outcome == DiscardOutcome.OfferReady) {
					await ResolveOfferAsync(context, animateDeal: true);
				} else if (outcome == DiscardOutcome.RejectedOrTimedOut) {
					Debug.LogWarning("[CardDrawAnimator] Paid discard was rejected or timed out.");
				}
			} catch (OperationCanceledException) {
				// Cleanup below restores only this flow's presentation ownership.
			} catch (Exception exception) {
				Debug.LogError($"[CardDrawAnimator] Paid discard flow failed: {exception}");
			} finally {
				await FinishFlowAsync(context);
			}
		}

		async UniTask ResolveOfferAsync(FlowContext context, bool animateDeal) {
			bool shouldAnimate = animateDeal;
			while (_state.SelectedCountry.CountryActions.HasPendingDraw) {
				IReadOnlyList<CardDrawChoiceEntry> choices =
					_state.SelectedCountry.CountryActions.DrawChoices;
				if (choices.Count == 0) {
					return;
				}
				CardDrawView.Selection selection = await _view.ShowChoicesAsync(
					choices,
					_actionsView.DeckPileElement,
					BuildFace,
					shouldAnimate,
					context.Token);
				var occupiedSlots = new HashSet<int>();
				foreach (ActionCardEntry card in _state.SelectedCountry.CountryActions.Hand) {
					occupiedSlots.Add(card.SlotIndex);
				}

				await _view.ReturnUnselectedAsync(
					selection,
					_actionsView.DeckPileElement,
					context.Token);
				_commands.Push(new ReceiveCardCommand {
					OrgId = _state.PlayerOrganization.OrgId,
					ChoiceIndex = selection.Choice.ChoiceIndex
				});

				ActionCardEntry received = await WaitForReceiptAsync(
					selection.Choice.Card,
					occupiedSlots,
					context.Token);
				if (received != null) {
					_countryInfo.EnsureActionsOpen();
					_actionsView.SuppressRefresh = false;
					_actionsView.Refresh(
						_state.SelectedCountry.CountryActions,
						_state.PlayerOrganization.Resources);
					_actionsView.SuppressRefresh = true;
					if (!_actionsView.TryGetRenderedCard(received.SlotIndex, out VisualElement destination)) {
						throw new InvalidOperationException(
							$"Received card slot {received.SlotIndex} was not rendered.");
					}
					context.HiddenElement = destination;
					destination.style.opacity = 0f;
					// Settle layout for the now-hidden card before reading its worldBound below.
					await UniTask.NextFrame(cancellationToken: context.Token);
					await _view.MoveSelectedAsync(selection, destination, context.Token);
					destination.style.opacity = 1f;
					context.HiddenElement = null;
					return;
				}

				if (!_state.SelectedCountry.CountryActions.HasPendingDraw) {
					Debug.LogWarning("[CardDrawAnimator] Receipt cleared the offer without a matching new hand slot.");
					return;
				}
				_view.ClearChoiceCards();
				shouldAnimate = false;
			}
		}

		ActionCardBuilder.CountryCardFace BuildFace(ActionCardEntry card) {
			if (!_actionsView.TryGetFaceData(card, out ActionCardBuilder.CountryCardFace face)) {
				throw new InvalidOperationException($"No face data exists for offered card '{card?.ActionId}'.");
			}
			return face;
		}

		async UniTask<bool> WaitForOfferAsync(CancellationToken cancellationToken) {
			float deadline = Time.realtimeSinceStartup + CommandTimeoutSeconds;
			while (Time.realtimeSinceStartup < deadline) {
				cancellationToken.ThrowIfCancellationRequested();
				CountryActionsState actions = _state.SelectedCountry.CountryActions;
				if (actions.HasPendingDraw && actions.DrawChoices.Count > 0) {
					return true;
				}
				await UniTask.NextFrame(cancellationToken: cancellationToken);
			}
			CountryActionsState finalState = _state.SelectedCountry.CountryActions;
			return finalState.HasPendingDraw && finalState.DrawChoices.Count > 0;
		}

		async UniTask<ActionCardEntry> WaitForReceiptAsync(
			ActionCardEntry selected,
			HashSet<int> occupiedSlots,
			CancellationToken cancellationToken) {
			float deadline = Time.realtimeSinceStartup + CommandTimeoutSeconds;
			while (Time.realtimeSinceStartup < deadline) {
				cancellationToken.ThrowIfCancellationRequested();
				CountryActionsState actions = _state.SelectedCountry.CountryActions;
				if (!actions.HasPendingDraw) {
					foreach (ActionCardEntry card in actions.Hand) {
						if (!occupiedSlots.Contains(card.SlotIndex) && SamePhysicalIdentity(card, selected)) {
							return card;
						}
					}
				}
				await UniTask.NextFrame(cancellationToken: cancellationToken);
			}
			return null;
		}

		async UniTask<DiscardOutcome> WaitForDiscardOutcomeAsync(
			string actionId,
			string targetCountryId,
			int slotIndex,
			CancellationToken cancellationToken) {
			float deadline = Time.realtimeSinceStartup + CommandTimeoutSeconds;
			while (Time.realtimeSinceStartup < deadline) {
				cancellationToken.ThrowIfCancellationRequested();
				CountryActionsState actions = _state.SelectedCountry.CountryActions;
				if (actions.HasPendingDraw && actions.DrawChoices.Count > 0) {
					return DiscardOutcome.OfferReady;
				}
				if (!ContainsCard(actions.Hand, actionId, targetCountryId, slotIndex)) {
					return DiscardOutcome.RemovedWithoutOffer;
				}
				await UniTask.NextFrame(cancellationToken: cancellationToken);
			}
			return DiscardOutcome.RejectedOrTimedOut;
		}

		void PushPauseIfOwned(FlowContext context) {
			if (context.IssuedPause) {
				_commands.Push(new PauseCommand());
			}
		}

		async UniTask FinishFlowAsync(FlowContext context) {
			if (!context.Token.IsCancellationRequested
				&& _restorationEnabled
				&& _state.SelectedCountry.CountryActions.HasPendingDraw
				&& _state.SelectedCountry.CountryActions.DrawChoices.Count > 0) {
				try {
					await ResolveOfferAsync(context, animateDeal: false);
				} catch (OperationCanceledException) {
					// Continue with generation-owned cleanup.
				} catch (Exception exception) {
					Debug.LogError($"[CardDrawAnimator] Late draw-offer restoration failed: {exception}");
				}
			}
			_modalState.Unlock(context.Owner);
			if (context.Generation != _generation) {
				context.Cancellation.Dispose();
				context.Completion.TrySetResult();
				return;
			}
			if (context.HiddenElement != null && context.HiddenElement.panel != null) {
				context.HiddenElement.style.opacity = 1f;
			}
			_view.Cleanup();
			_document.sortingOrder = context.PreviousSortingOrder;
			_actionsView.SuppressRefresh = false;
			_actionsView.SetPresentationBusy(false);
			_actionsView.Refresh(
				_state.SelectedCountry.CountryActions,
				_state.PlayerOrganization.Resources);
			if (_resumeBarrierOwner != null) {
				_document.sortingOrder = DrawModalSortingOrder;
				_actionsView.SetPresentationBusy(true);
				_view.ShowShield();
			}
			if (context.IssuedPause) {
				_commands.Push(new UnpauseCommand());
				// Cross a complete game-processing frame even when the original PauseCommand
				// has not projected yet. TimeSystem groups pause before unpause commands, so
				// a replacement flow must not enqueue its Pause in that same drain.
				await UniTask.NextFrame();
				await UniTask.NextFrame();
				float deadline = Time.realtimeSinceStartup + PauseReleaseTimeoutSeconds;
				while (_state.Time.IsPaused && Time.realtimeSinceStartup < deadline) {
					await UniTask.NextFrame();
				}
			}
			_isPlaying = false;
			if (ReferenceEquals(_flowCancellation, context.Cancellation)) {
				_flowCancellation = null;
			}
			if (ReferenceEquals(_flowCompletion, context.Completion)) {
				_flowCompletion = null;
			}
			if (ReferenceEquals(_activeContext, context)) {
				_activeContext = null;
			}
			context.Cancellation.Dispose();
			context.Completion.TrySetResult();
			RestorePendingOfferIfIdle();
		}

		static bool SamePhysicalIdentity(ActionCardEntry left, ActionCardEntry right) =>
			left.ActionId == right.ActionId
			&& left.CountryContextId == right.CountryContextId
			&& left.TargetCountryId == right.TargetCountryId;

		static bool ContainsCard(
			IReadOnlyList<ActionCardEntry> cards,
			string actionId,
			string targetCountryId,
			int slotIndex) {
			foreach (ActionCardEntry card in cards) {
				if (card.ActionId == actionId
					&& card.TargetCountryId == targetCountryId
					&& card.SlotIndex == slotIndex) {
					return true;
				}
			}
			return false;
		}

		sealed class FlowOwner {
			readonly int _generation;

			public FlowOwner(int generation) {
				_generation = generation;
			}

			public override string ToString() => $"CardDrawFlow({_generation})";
		}

		sealed class FlowContext {
			public int Generation { get; }
			public FlowOwner Owner { get; }
			public CancellationTokenSource Cancellation { get; }
			public UniTaskCompletionSource Completion { get; }
			public CancellationToken Token => Cancellation.Token;
			public float PreviousSortingOrder { get; }
			public bool IssuedPause { get; }
			public VisualElement HiddenElement { get; set; }

			public FlowContext(
				int generation,
				FlowOwner owner,
				CancellationTokenSource cancellation,
				UniTaskCompletionSource completion,
				float previousSortingOrder,
				bool issuedPause) {
				Generation = generation;
				Owner = owner;
				Cancellation = cancellation;
				Completion = completion;
				PreviousSortingOrder = previousSortingOrder;
				IssuedPause = issuedPause;
			}
		}

		enum DiscardOutcome {
			OfferReady,
			RemovedWithoutOffer,
			RejectedOrTimedOut
		}
	}
}

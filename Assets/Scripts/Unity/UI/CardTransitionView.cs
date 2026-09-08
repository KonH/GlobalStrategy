using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using GS.Game.Configs;
using GS.Main;
using GS.Unity.Common;

namespace GS.Unity.UI {
	// Public so the gallery scene (GS.Unity.Gallery) can preview a card mid-transition
	// (Docs/Specs/26_08_28_16_ui-refactoring phase 7 "Hand/deck and animation blocks" batch).
	public class CardTransitionView {
		const float GeometryTimeoutSeconds = 2f;

		VisualElement _overlay;
		// Owns the currently in-flight Show/ShowCountry invocation, if any. Show/ShowCountry create a
		// fresh copy/source pair per call (cancelling/disposing the previous one first) so an older
		// continuation can never move or hide a newer invocation's card - see the reference-equality
		// guard in the finally block of PlaceAndAnimate.
		VisualElement _activeCardCopy;
		CancellationTokenSource _activeCancellation;

		public CardTransitionView(VisualElement overlay) {
			_overlay = overlay;
		}

		public async UniTask Show(
			string actionId,
			Rect fromRect,
			VisualElement toElement,
			float duration,
			ActionConfig actionConfig,
			ActionVisualConfig visualConfig,
			ILocalization loc,
			CancellationToken cancellationToken) {
			var def = actionConfig?.Find(actionId);
			string nameText = def != null ? loc.Get(def.NameKey) : actionId;
			string descText = def != null ? loc.Get(def.DescKey) : "";
			string goldCostText = GetGoldCostText(def);
			var sprite = visualConfig?.FindFront(actionId);

			var built = ActionCardBuilder.Build(nameText, descText, goldCostText, sprite);
			var cardCopy = built.Card;
			cardCopy.AddToClassList("action-card--available");
			await BeginAndAnimate(cardCopy, fromRect, toElement, duration, cancellationToken);
		}

		public async UniTask ShowCountry(
			ActionCardBuilder.CountryCardFace faceData,
			Rect fromRect,
			VisualElement toElement,
			float duration,
			CancellationToken cancellationToken) {
			if (faceData == null) {
				throw new ArgumentNullException(nameof(faceData));
			}

			var built = ActionCardBuilder.Build(faceData, false);
			var cardCopy = built.Card;
			cardCopy.AddToClassList("action-card--available");
			await BeginAndAnimate(cardCopy, fromRect, toElement, duration, cancellationToken);
		}

		async UniTask BeginAndAnimate(
			VisualElement cardCopy,
			Rect fromRect,
			VisualElement toElement,
			float duration,
			CancellationToken cancellationToken) {
			// Cancel/clear any prior invocation before claiming ownership of the active fields,
			// mirroring CardDrawAnimator.BeginFlow's serialized-flow-ownership pattern.
			CancelActive();
			var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			_activeCardCopy = cardCopy;
			_activeCancellation = cancellation;

			try {
				await PlaceAndAnimate(cardCopy, fromRect, toElement, duration, cancellation.Token);
			} finally {
				// Do NOT clear _activeCardCopy/_activeCancellation here on this invocation's own
				// completion (success or its own cancellation) - the caller still calls Hide() right
				// after awaiting Show/ShowCountry to remove the placed copy, and Hide() needs to find
				// it here. If a newer invocation has already superseded this one, ITS CancelActive()
				// call already cancelled/disposed this token source and removed/cleared the copy, so
				// only avoid a double-dispose in that case.
				if (ReferenceEquals(_activeCancellation, cancellation)) {
					// Still this invocation's own fields - leave them set for Hide() to clean up.
				} else {
					cancellation.Dispose();
				}
			}
		}

		async UniTask PlaceAndAnimate(
			VisualElement cardCopy,
			Rect fromRect,
			VisualElement toElement,
			float duration,
			CancellationToken cancellationToken) {
			cardCopy.style.position = Position.Absolute;
			cardCopy.style.width = 240f;
			cardCopy.style.height = 360f;

			var fromLocal = _overlay.WorldToLocal(new Vector2(fromRect.x, fromRect.y));
			cardCopy.style.left = fromLocal.x;
			cardCopy.style.top = fromLocal.y;

			_overlay.Add(cardCopy);
			SetPickingIgnoreRecursive(cardCopy);

			await WaitForGeometryAsync(cardCopy, cancellationToken);

			if (toElement == null || toElement.panel == null) {
				throw new InvalidOperationException("The card-transition destination is not attached to the HUD.");
			}
			var toWorld = toElement.worldBound;
			var toLocal = _overlay.WorldToLocal(new Vector2(toWorld.x, toWorld.y));
			await UiTween.MoveAsync(cardCopy, fromLocal, toLocal, duration, cancellationToken);
		}

		// Cancellation- and detachment-safe replacement for a raw one-shot GeometryChangedEvent wait:
		// registers geometry/detach/cancellation completion paths before adding the copy, then
		// immediately rechecks synchronously to close the race where geometry already fired (or the
		// element detached) before registration. A single guarded completion source ensures only the
		// first of the three paths wins, and every registration is unwound in `finally`.
		async UniTask WaitForGeometryAsync(VisualElement cardCopy, CancellationToken cancellationToken) {
			var completion = new UniTaskCompletionSource();

			EventCallback<GeometryChangedEvent> onGeometry = _ => completion.TrySetResult();
			EventCallback<DetachFromPanelEvent> onDetach = _ =>
				completion.TrySetException(new OperationCanceledException("Card-transition copy detached before layout."));
			cardCopy.RegisterCallback(onGeometry);
			cardCopy.RegisterCallback(onDetach);
			using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));

			try {
				// Close the missed-event race: geometry may already be valid, or the copy may already be
				// detached, before the callbacks above were registered.
				if (cardCopy.panel == null) {
					completion.TrySetException(new OperationCanceledException("Card-transition copy detached before layout."));
				} else if (cardCopy.worldBound.width > 0f && cardCopy.worldBound.height > 0f) {
					completion.TrySetResult();
				}

				float deadline = Time.realtimeSinceStartup + GeometryTimeoutSeconds;
				int winner = await UniTask.WhenAny(completion.Task, TimeoutAsync(deadline, cancellationToken));
				if (winner != 0) {
					throw new TimeoutException("Timed out waiting for card-transition copy geometry.");
				}
			} finally {
				cardCopy.UnregisterCallback(onGeometry);
				cardCopy.UnregisterCallback(onDetach);
			}
		}

		// Bounded real-time (not scaled/paused Time.deltaTime) wait local to this class - separate from
		// CardDrawView.WaitForSlotGeometryAsync's own ~2s constant, per the plan's no-cross-dependency note.
		static async UniTask TimeoutAsync(float deadline, CancellationToken cancellationToken) {
			while (Time.realtimeSinceStartup < deadline) {
				await UniTask.NextFrame(cancellationToken: cancellationToken);
			}
		}

		public void Hide() {
			CancelActive();
		}

		// Cancels the active invocation's token (if any) and removes its placed copy from the overlay -
		// shared by Hide() and BeginAndAnimate's own supersede-on-start call, so an older invocation
		// superseded by a newer Show/ShowCountry call never leaves its copy orphaned in the overlay.
		void CancelActive() {
			if (_activeCancellation != null) {
				_activeCancellation.Cancel();
				_activeCancellation.Dispose();
				_activeCancellation = null;
			}
			if (_activeCardCopy != null) {
				if (_activeCardCopy.parent != null) {
					_overlay.Remove(_activeCardCopy);
				}
				_activeCardCopy = null;
			}
		}

		static void SetPickingIgnoreRecursive(VisualElement el) {
			el.pickingMode = PickingMode.Ignore;
			foreach (var child in el.Children()) {
				SetPickingIgnoreRecursive(child);
			}
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

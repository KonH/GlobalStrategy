using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using GS.Game.Configs;
using GS.Main;
using GS.Unity.Common;

namespace GS.Unity.UI {
	// Public so the gallery scene (GS.Unity.Gallery) can preview the draw-choice offer screen
	// (Docs/Specs/26_08_28_16_ui-refactoring phase 7 "Hand/deck and animation blocks" batch).
	public class CardDrawView {
		public const float DealDuration = 0.25f;
		public const float FlipDuration = 0.2f;
		public const float HoverDuration = 0.2f;
		public const float ReturnDuration = 0.25f;
		public const float ReceiptDuration = 0.3f;
		public const float DiscardDuration = 0.55f;

		const float HoverScale = 1.3f;
		const float CardSpacing = 72f;
		const float GeometryTimeoutSeconds = 2f;

		readonly VisualElement _overlay;
		readonly VisualElement _row;
		readonly ActionVisualConfig _visualConfig;
		readonly List<ChoiceCopy> _copies = new();
		readonly List<VisualElement> _slots = new();
		VisualElement _discardCopy;
		bool _selectionLocked;

		public CardDrawView(
			VisualElement overlay,
			VisualElement row,
			ActionVisualConfig visualConfig) {
			_overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
			_row = row ?? throw new ArgumentNullException(nameof(row));
			_visualConfig = visualConfig;
			_overlay.style.display = DisplayStyle.None;
		}

		public void ShowShield() {
			_overlay.style.display = DisplayStyle.Flex;
			_overlay.pickingMode = PickingMode.Position;
		}

		public async UniTask<Selection> ShowChoicesAsync(
			IReadOnlyList<CardDrawChoiceEntry> choices,
			VisualElement deckElement,
			Func<ActionCardEntry, ActionCardBuilder.CountryCardFace> buildFace,
			bool animateDeal,
			CancellationToken cancellationToken) {
			if (choices == null || choices.Count == 0) {
				throw new ArgumentException("At least one authoritative draw choice is required.", nameof(choices));
			}
			if (deckElement == null || deckElement.panel == null) {
				throw new InvalidOperationException("The country-card deck is not attached to the HUD.");
			}
			if (buildFace == null) {
				throw new ArgumentNullException(nameof(buildFace));
			}

			ClearChoiceCards();
			ShowShield();
			BuildSlots(choices.Count);
			await WaitForSlotGeometryAsync(cancellationToken);
			Vector2 deckPosition = WorldTopLeftToOverlay(deckElement.worldBound);

			for (int i = 0; i < choices.Count; i++) {
				CardDrawChoiceEntry choice = choices[i];
				var copy = BuildChoiceCopy(choice, buildFace(choice.Card));
				_copies.Add(copy);
				_overlay.Add(copy.Root);
				Vector2 target = WorldTopLeftToOverlay(_slots[i].worldBound);
				copy.Position = target;
				SetPosition(copy.Root, animateDeal ? deckPosition : target);
				if (animateDeal) {
					await AnimatePositionAsync(
						copy.Root,
						deckPosition,
						target,
						DealDuration,
						cancellationToken);
				}
			}

			if (animateDeal) {
				foreach (ChoiceCopy copy in _copies) {
					await FlipAsync(copy, cancellationToken);
				}
			} else {
				foreach (ChoiceCopy copy in _copies) {
					ShowFace(copy);
				}
			}

			return await WaitForSelectionAsync(cancellationToken);
		}

		public async UniTask ReturnUnselectedAsync(
			Selection selection,
			VisualElement deckElement,
			CancellationToken cancellationToken) {
			_selectionLocked = true;
			foreach (ChoiceCopy copy in _copies) {
				copy.HoverGeneration++;
			}
			Vector2 deckPosition = WorldTopLeftToOverlay(deckElement.worldBound);
			var ordered = new List<ChoiceCopy>(_copies);
			ordered.Sort((left, right) => left.Choice.ChoiceIndex.CompareTo(right.Choice.ChoiceIndex));
			foreach (ChoiceCopy copy in ordered) {
				if (copy == selection.Copy) {
					continue;
				}
				await AnimateScaleAsync(copy, 1f, cancellationToken);
				await AnimatePositionAsync(
					copy.Root,
					copy.Position,
					deckPosition,
					ReturnDuration,
					cancellationToken);
				copy.Root.RemoveFromHierarchy();
				_copies.Remove(copy);
			}
		}

		public async UniTask MoveSelectedAsync(
			Selection selection,
			VisualElement destination,
			CancellationToken cancellationToken) {
			if (selection?.Copy?.Root == null || destination == null || destination.panel == null) {
				throw new InvalidOperationException("The received card destination is not attached to the HUD.");
			}
			ChoiceCopy copy = selection.Copy;
			await AnimateScaleAsync(copy, 1f, cancellationToken);
			Vector2 destinationPosition = WorldTopLeftToOverlay(destination.worldBound);
			await AnimatePositionAsync(
				copy.Root,
				copy.Position,
				destinationPosition,
				ReceiptDuration,
				cancellationToken);
			copy.Root.RemoveFromHierarchy();
			_copies.Remove(copy);
		}

		public async UniTask AnimatePaidDiscardAsync(
			ActionCardBuilder.CountryCardFace face,
			Rect fromRect,
			VisualElement deckElement,
			CancellationToken cancellationToken) {
			if (face == null) {
				throw new ArgumentNullException(nameof(face));
			}
			var built = ActionCardBuilder.Build(face, includeDiscardHint: false);
			_discardCopy = built.Card;
			_discardCopy.AddToClassList("action-card--available");
			_discardCopy.AddToClassList("card-draw-card-copy");
			SetPickingIgnoreRecursive(_discardCopy);
			_overlay.Add(_discardCopy);
			Vector2 from = WorldTopLeftToOverlay(fromRect);
			Vector2 to = WorldTopLeftToOverlay(deckElement.worldBound);
			SetPosition(_discardCopy, from);
			await AnimatePositionAsync(
				_discardCopy,
				from,
				to,
				DiscardDuration,
				cancellationToken);
			_discardCopy.RemoveFromHierarchy();
			_discardCopy = null;
		}

		public void ClearChoiceCards() {
			_selectionLocked = false;
			foreach (ChoiceCopy copy in _copies) {
				copy.Root.RemoveFromHierarchy();
			}
			_copies.Clear();
			foreach (VisualElement slot in _slots) {
				slot.RemoveFromHierarchy();
			}
			_slots.Clear();
		}

		public void RefreshChoiceFaces(
			Func<ActionCardEntry, ActionCardBuilder.CountryCardFace> buildFace) {
			if (buildFace == null) {
				throw new ArgumentNullException(nameof(buildFace));
			}
			foreach (ChoiceCopy copy in _copies) {
				bool faceVisible = copy.Back.style.display == DisplayStyle.None;
				var built = ActionCardBuilder.Build(
					buildFace(copy.Choice.Card),
					includeDiscardHint: false);
				VisualElement refreshedFace = built.Card;
				refreshedFace.AddToClassList("action-card--available");
				refreshedFace.AddToClassList("card-draw-card-face");
				SetPickingIgnoreRecursive(refreshedFace);
				refreshedFace.style.display = faceVisible ? DisplayStyle.Flex : DisplayStyle.None;
				copy.Face.RemoveFromHierarchy();
				copy.Root.Insert(0, refreshedFace);
				copy.Face = refreshedFace;
			}
		}

		public void Cleanup() {
			ClearChoiceCards();
			if (_discardCopy != null) {
				_discardCopy.RemoveFromHierarchy();
				_discardCopy = null;
			}
			_overlay.style.display = DisplayStyle.None;
		}

		ChoiceCopy BuildChoiceCopy(
			CardDrawChoiceEntry choice,
			ActionCardBuilder.CountryCardFace face) {
			if (face == null) {
				throw new InvalidOperationException($"No face data is available for draw choice {choice.ChoiceIndex}.");
			}
			var root = new VisualElement();
			root.AddToClassList("card-draw-card-copy");
			root.pickingMode = PickingMode.Position;

			var built = ActionCardBuilder.Build(face, includeDiscardHint: false);
			VisualElement faceElement = built.Card;
			faceElement.AddToClassList("action-card--available");
			faceElement.AddToClassList("card-draw-card-face");
			SetPickingIgnoreRecursive(faceElement);
			root.Add(faceElement);

			var back = new VisualElement();
			back.AddToClassList("action-card");
			back.AddToClassList("action-card--available");
			back.AddToClassList("card-draw-card-back");
			back.pickingMode = PickingMode.Ignore;
			if (_visualConfig?.defaultBackImage != null) {
				back.style.backgroundImage = new StyleBackground(_visualConfig.defaultBackImage);
				back.style.backgroundSize = new StyleBackgroundSize(
					new BackgroundSize(BackgroundSizeType.Cover));
			}
			root.Add(back);
			faceElement.style.display = DisplayStyle.None;

			return new ChoiceCopy(choice, root, faceElement, back);
		}

		void BuildSlots(int count) {
			for (int i = 0; i < count; i++) {
				var slot = new VisualElement();
				slot.AddToClassList("card-draw-slot");
				slot.pickingMode = PickingMode.Ignore;
				if (i > 0) {
					slot.style.marginLeft = CardSpacing;
				}
				_slots.Add(slot);
				_row.Add(slot);
			}
		}

		async UniTask WaitForSlotGeometryAsync(CancellationToken cancellationToken) {
			float deadline = Time.realtimeSinceStartup + GeometryTimeoutSeconds;
			while (true) {
				cancellationToken.ThrowIfCancellationRequested();
				bool ready = _overlay.panel != null;
				foreach (VisualElement slot in _slots) {
					ready &= slot.panel != null
						&& slot.worldBound.width > 0f
						&& slot.worldBound.height > 0f;
				}
				if (ready) {
					return;
				}
				if (Time.realtimeSinceStartup >= deadline) {
					throw new TimeoutException("Timed out waiting for card-draw overlay geometry.");
				}
				await UniTask.NextFrame(cancellationToken: cancellationToken);
			}
		}

		async UniTask FlipAsync(ChoiceCopy copy, CancellationToken cancellationToken) {
			float halfDuration = FlipDuration * 0.5f;
			await AnimateHorizontalScaleAsync(copy.Root, 1f, 0f, halfDuration, cancellationToken);
			ShowFace(copy);
			await AnimateHorizontalScaleAsync(copy.Root, 0f, 1f, halfDuration, cancellationToken);
		}

		static void ShowFace(ChoiceCopy copy) {
			copy.Back.style.display = DisplayStyle.None;
			copy.Face.style.display = DisplayStyle.Flex;
		}

		async UniTask<Selection> WaitForSelectionAsync(CancellationToken cancellationToken) {
			_selectionLocked = false;
			var completion = new UniTaskCompletionSource<Selection>();
			foreach (ChoiceCopy copy in _copies) {
				copy.Root.RegisterCallback<PointerEnterEvent>(_ => {
					if (!_selectionLocked) {
						int generation = ++copy.HoverGeneration;
						copy.Root.BringToFront();
						AnimateHoverAsync(copy, HoverScale, generation, cancellationToken).Forget();
					}
				});
				copy.Root.RegisterCallback<PointerLeaveEvent>(_ => {
					if (!_selectionLocked) {
						int generation = ++copy.HoverGeneration;
						AnimateHoverAsync(copy, 1f, generation, cancellationToken).Forget();
					}
				});
				copy.Root.RegisterCallback<PointerUpEvent>(e => {
					if (_selectionLocked
						|| e.button != 0
						|| !copy.Root.ContainsPoint(e.localPosition)) {
						return;
					}
					_selectionLocked = true;
					foreach (ChoiceCopy pending in _copies) {
						pending.HoverGeneration++;
					}
					completion.TrySetResult(new Selection(copy));
					e.StopPropagation();
				});
			}

			using (cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken))) {
				return await completion.Task;
			}
		}

		async UniTask AnimateHoverAsync(
			ChoiceCopy copy,
			float target,
			int generation,
			CancellationToken cancellationToken) {
			try {
				await AnimateScaleAsync(copy, target, cancellationToken, generation);
				if (target <= 1f && generation == copy.HoverGeneration && !_selectionLocked) {
					RestoreCopyOrder();
				}
			} catch (OperationCanceledException) {
				// Flow cleanup removes the temporary card.
			}
		}

		async UniTask AnimateScaleAsync(
			ChoiceCopy copy,
			float target,
			CancellationToken cancellationToken,
			int hoverGeneration = -1) {
			float from = copy.Scale;
			float elapsed = 0f;
			while (elapsed < HoverDuration) {
				cancellationToken.ThrowIfCancellationRequested();
				if (hoverGeneration >= 0 && hoverGeneration != copy.HoverGeneration) {
					return;
				}
				elapsed += Time.unscaledDeltaTime;
				copy.Scale = Mathf.Lerp(from, target, Mathf.Clamp01(elapsed / HoverDuration));
				SetScale(copy.Root, copy.Scale, copy.Scale);
				await UniTask.NextFrame(cancellationToken: cancellationToken);
			}
			copy.Scale = target;
			SetScale(copy.Root, target, target);
		}

		void RestoreCopyOrder() {
			foreach (ChoiceCopy copy in _copies) {
				copy.Root.BringToFront();
			}
		}

		static async UniTask AnimateHorizontalScaleAsync(
			VisualElement element,
			float from,
			float to,
			float duration,
			CancellationToken cancellationToken) {
			float elapsed = 0f;
			while (elapsed < duration) {
				cancellationToken.ThrowIfCancellationRequested();
				elapsed += Time.unscaledDeltaTime;
				SetScale(element, Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration)), 1f);
				await UniTask.NextFrame(cancellationToken: cancellationToken);
			}
			SetScale(element, to, 1f);
		}

		static async UniTask AnimatePositionAsync(
			VisualElement element,
			Vector2 from,
			Vector2 to,
			float duration,
			CancellationToken cancellationToken) {
			float elapsed = 0f;
			while (elapsed < duration) {
				cancellationToken.ThrowIfCancellationRequested();
				if (element.panel == null) {
					throw new InvalidOperationException("A card-draw element detached during animation.");
				}
				elapsed += Time.unscaledDeltaTime;
				SetPosition(element, Vector2.Lerp(from, to, Mathf.Clamp01(elapsed / duration)));
				await UniTask.NextFrame(cancellationToken: cancellationToken);
			}
			SetPosition(element, to);
		}

		Vector2 WorldTopLeftToOverlay(Rect worldRect) =>
			_overlay.WorldToLocal(new Vector2(worldRect.xMin, worldRect.yMin));

		static void SetPosition(VisualElement element, Vector2 position) {
			element.style.left = position.x;
			element.style.top = position.y;
		}

		static void SetScale(VisualElement element, float x, float y) {
			element.style.scale = new Scale(new Vector3(x, y, 1f));
		}

		static void SetPickingIgnoreRecursive(VisualElement element) {
			element.pickingMode = PickingMode.Ignore;
			foreach (VisualElement child in element.Children()) {
				SetPickingIgnoreRecursive(child);
			}
		}

		public sealed class Selection {
			internal ChoiceCopy Copy { get; }
			public CardDrawChoiceEntry Choice => Copy.Choice;

			internal Selection(ChoiceCopy copy) {
				Copy = copy;
			}
		}

		internal sealed class ChoiceCopy {
			public CardDrawChoiceEntry Choice { get; }
			public VisualElement Root { get; }
			public VisualElement Face { get; set; }
			public VisualElement Back { get; }
			public Vector2 Position { get; set; }
			public float Scale { get; set; } = 1f;
			public int HoverGeneration { get; set; }

			public ChoiceCopy(
				CardDrawChoiceEntry choice,
				VisualElement root,
				VisualElement face,
				VisualElement back) {
				Choice = choice;
				Root = root;
				Face = face;
				Back = back;
			}
		}
	}
}

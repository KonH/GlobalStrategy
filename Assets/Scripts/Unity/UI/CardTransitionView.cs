using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using GS.Game.Configs;
using GS.Main;
using GS.Unity.Common;

namespace GS.Unity.UI {
	class CardTransitionView {
		VisualElement _overlay;
		MonoBehaviour _coroutineHost;
		VisualElement _cardCopy;

		public CardTransitionView(VisualElement overlay, MonoBehaviour coroutineHost) {
			_overlay = overlay;
			_coroutineHost = coroutineHost;
		}

		public void Show(
			string actionId,
			Rect fromRect,
			VisualElement toElement,
			float duration,
			ActionConfig actionConfig,
			ActionVisualConfig visualConfig,
			ILocalization loc,
			Action onComplete) {
			if (_cardCopy != null) {
				_overlay.Remove(_cardCopy);
			}

			_cardCopy = new VisualElement();
			_cardCopy.AddToClassList("action-card");
			_cardCopy.AddToClassList("action-card--available");

			var nameKey = actionConfig?.Find(actionId)?.NameKey;
			var nameText = nameKey != null ? loc.Get(nameKey) : actionId;
			var nameLabel = new Label(nameText);
			nameLabel.AddToClassList("action-card-name");
			_cardCopy.Add(nameLabel);

			var imageElement = new VisualElement();
			imageElement.AddToClassList("action-card-image");
			var frontSprite = visualConfig?.FindFront(actionId);
			if (frontSprite != null) {
				imageElement.style.backgroundImage = new StyleBackground(frontSprite);
			}
			_cardCopy.Add(imageElement);

			_cardCopy.style.position = Position.Absolute;
			_cardCopy.style.width = 240f;
			_cardCopy.style.height = 320f;

			var fromLocal = _overlay.WorldToLocal(new Vector2(fromRect.x, fromRect.y));
			_cardCopy.style.left = fromLocal.x;
			_cardCopy.style.top = fromLocal.y;

			_overlay.Add(_cardCopy);
			SetPickingIgnoreRecursive(_cardCopy);

			EventCallback<GeometryChangedEvent> onGeometry = null;
			onGeometry = _ => {
				_cardCopy.UnregisterCallback(onGeometry);
				var toWorld = toElement.worldBound;
				var toLocal = _overlay.WorldToLocal(new Vector2(toWorld.x, toWorld.y));
				_coroutineHost.StartCoroutine(AnimateCard(fromLocal, toLocal, duration, onComplete));
			};
			_cardCopy.RegisterCallback(onGeometry);
		}

		IEnumerator AnimateCard(Vector2 from, Vector2 to, float duration, Action onComplete) {
			float elapsed = 0f;
			while (elapsed < duration) {
				elapsed += Time.deltaTime;
				float t = Mathf.Clamp01(elapsed / duration);
				_cardCopy.style.left = Mathf.Lerp(from.x, to.x, t);
				_cardCopy.style.top = Mathf.Lerp(from.y, to.y, t);
				yield return null;
			}
			_cardCopy.style.left = to.x;
			_cardCopy.style.top = to.y;
			onComplete?.Invoke();
		}

		public void Hide() {
			if (_cardCopy != null && _cardCopy.parent != null) {
				_overlay.Remove(_cardCopy);
			}
			_cardCopy = null;
		}

		static void SetPickingIgnoreRecursive(VisualElement el) {
			el.pickingMode = PickingMode.Ignore;
			foreach (var child in el.Children()) {
				SetPickingIgnoreRecursive(child);
			}
		}
	}
}

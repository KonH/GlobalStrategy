using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace GS.Unity.UI {
	[RequireComponent(typeof(UIDocument))]
	public class FlyTextNotifierDocument : MonoBehaviour, IFlyTextNotifier {
		[SerializeField] int _topMostSortingOrder = 1000;
		[SerializeField] float _fadeInDuration = 0.5f;
		[SerializeField] float _holdDuration = 2.0f;
		[SerializeField] float _fadeOutDuration = 0.5f;

		enum Phase {
			Idle,
			FadeIn,
			Hold,
			FadeOut
		}

		ILocalization _loc;
		UIDocument _doc;
		VisualElement _root;
		Label _label;

		Phase _phase = Phase.Idle;
		float _elapsed;
		readonly Queue<string> _queue = new Queue<string>();

		[Inject]
		void Construct(ILocalization loc) {
			_loc = loc;
		}

		void Awake() {
			_doc = GetComponent<UIDocument>();
			_doc.sortingOrder = _topMostSortingOrder;
		}

		void Start() {
			_root = _doc.rootVisualElement.Q<VisualElement>("fly-text-root");
			if (_root == null) {
				return;
			}
			_label = _root.Q<Label>("fly-text-label");
			if (_label == null) {
				_root = null;
				return;
			}
			_label.enableRichText = true;
			_root.style.display = DisplayStyle.None;
			SetPickingIgnoreRecursive(_root);
		}

		public void Notify(string localizationKey, params object[] args) {
			string resolved = _loc?.Get(localizationKey) ?? localizationKey;
			string formatted;
			try {
				formatted = (args != null && args.Length > 0) ? string.Format(resolved, args) : resolved;
			} catch (FormatException) {
				formatted = resolved;
			}
			Debug.Log($"[FlyText] Notify: key={localizationKey}, resolved=\"{formatted}\", queueCountBefore={_queue.Count}");
			_queue.Enqueue(formatted);
		}

		public void NotifyRaw(string text) {
			_queue.Enqueue(text);
		}

		void Update() {
			if (_root == null) {
				return;
			}
			float dt = Time.deltaTime;
			switch (_phase) {
				case Phase.Idle:
					if (_queue.Count > 0) {
						StartFadeIn(_queue.Dequeue());
					}
					break;
				case Phase.FadeIn: {
					_elapsed += dt;
					float t = Mathf.Clamp01(_elapsed / _fadeInDuration);
					_root.style.opacity = Mathf.Lerp(0f, 1f, t);
					if (t >= 1f) {
						_phase = Phase.Hold;
						_elapsed = 0f;
					}
					break;
				}
				case Phase.Hold:
					_elapsed += dt;
					if (_elapsed >= _holdDuration) {
						_phase = Phase.FadeOut;
						_elapsed = 0f;
					}
					break;
				case Phase.FadeOut: {
					_elapsed += dt;
					float t = Mathf.Clamp01(_elapsed / _fadeOutDuration);
					_root.style.opacity = Mathf.Lerp(1f, 0f, t);
					if (t >= 1f) {
						HideAndReset();
						_phase = Phase.Idle;
					}
					break;
				}
			}
		}

		void StartFadeIn(string text) {
			_label.text = text;
			_root.style.opacity = 0f;
			_root.style.display = DisplayStyle.Flex;
			_phase = Phase.FadeIn;
			_elapsed = 0f;
		}

		void HideAndReset() {
			_root.style.display = DisplayStyle.None;
			_root.style.opacity = 1f;
		}

		static void SetPickingIgnoreRecursive(VisualElement element) {
			element.pickingMode = PickingMode.Ignore;
			foreach (var child in element.Children()) {
				SetPickingIgnoreRecursive(child);
			}
		}
	}
}

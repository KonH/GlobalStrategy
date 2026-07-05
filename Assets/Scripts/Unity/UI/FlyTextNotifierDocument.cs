using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace GS.Unity.UI {
	[RequireComponent(typeof(UIDocument))]
	public class FlyTextNotifierDocument : MonoBehaviour, IFlyTextNotifier {
		[SerializeField] int _topMostSortingOrder = 1000;
		[SerializeField] float _entranceDuration = 0.2f;
		[SerializeField] float _holdDuration = 1.5f;
		[SerializeField] float _exitDuration = 0.5f;
		[SerializeField] float _entranceStartScale = 0.5f;
		[SerializeField] float _exitEndScale = 0.8f;
		[SerializeField] float _exitMoveDownPx = 40f;

		enum Phase {
			Idle,
			Entrance,
			Hold,
			Exit
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
			_label = _root.Q<Label>("fly-text-label");
			if (_root == null) {
				return;
			}
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

		void Update() {
			if (_root == null) {
				return;
			}
			float dt = Time.deltaTime;
			switch (_phase) {
				case Phase.Idle:
					if (_queue.Count > 0) {
						StartEntrance(_queue.Dequeue());
					}
					break;
				case Phase.Entrance: {
					_elapsed += dt;
					float t = Mathf.Clamp01(_elapsed / _entranceDuration);
					float scale = Mathf.Lerp(_entranceStartScale, 1f, t);
					_root.style.scale = new Scale(new Vector3(scale, scale, 1f));
					if (t >= 1f) {
						_phase = Phase.Hold;
						_elapsed = 0f;
					}
					break;
				}
				case Phase.Hold:
					_elapsed += dt;
					if (_elapsed >= _holdDuration) {
						_phase = Phase.Exit;
						_elapsed = 0f;
					}
					break;
				case Phase.Exit: {
					_elapsed += dt;
					float et = Mathf.Clamp01(_elapsed / _exitDuration);
					_root.style.translate = new Translate(0, Mathf.Lerp(0, _exitMoveDownPx, et), 0);
					float exitScale = Mathf.Lerp(1f, _exitEndScale, et);
					_root.style.scale = new Scale(new Vector3(exitScale, exitScale, 1f));
					_root.style.opacity = Mathf.Lerp(1f, 0f, et);
					if (et >= 1f) {
						HideAndReset();
						_phase = Phase.Idle;
					}
					break;
				}
			}
		}

		void StartEntrance(string text) {
			_label.text = text;
			_root.style.opacity = 1f;
			_root.style.translate = new Translate(0, 0, 0);
			_root.style.scale = new Scale(new Vector3(_entranceStartScale, _entranceStartScale, 1f));
			_root.style.display = DisplayStyle.Flex;
			_phase = Phase.Entrance;
			_elapsed = 0f;
		}

		void HideAndReset() {
			_root.style.display = DisplayStyle.None;
			_root.style.translate = new Translate(0, 0, 0);
			_root.style.scale = new Scale(Vector3.one);
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

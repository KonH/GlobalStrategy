using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace GS.Unity.UI {
	[RequireComponent(typeof(UIDocument))]
	public class FlyTextNotifierDocument : MonoBehaviour, IFlyTextNotifier {
		public const int TopMostSortingOrder = 1000;

		const float EntranceDuration = 0.2f;
		const float HoldDuration = 1.5f;
		const float ExitDuration = 0.5f;
		const float EntranceStartScale = 0.5f;
		const float ExitEndScale = 0.8f;
		const float ExitMoveDownPx = 40f;

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
			Debug.Log($"[FlyText] Construct: injected, locIsNull={_loc == null}");
		}

		void Awake() {
			_doc = GetComponent<UIDocument>();
			_doc.sortingOrder = TopMostSortingOrder;
			Debug.Log($"[FlyText] Awake: sortingOrder set to {TopMostSortingOrder}");
		}

		void Start() {
			_root = _doc.rootVisualElement.Q<VisualElement>("fly-text-root");
			_label = _root.Q<Label>("fly-text-label");
			Debug.Log($"[FlyText] Start: doc={_doc?.name}, root={(_root != null ? "found" : "NULL")}, label={(_label != null ? "found" : "NULL")}");
			if (_root == null) {
				return;
			}
			_root.style.display = DisplayStyle.None;
			SetPickingIgnoreRecursive(_root);
			Debug.Log($"[FlyText] Start: panel={(_root.panel != null)}, worldBound={_root.worldBound}, resolvedDisplay={_root.resolvedStyle.display}, resolvedOpacity={_root.resolvedStyle.opacity}");
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
						Debug.Log($"[FlyText] Phase Idle->Entrance: display={_root.resolvedStyle.display}, opacity={_root.resolvedStyle.opacity}, worldBound={_root.worldBound}, layout={_root.layout}, panel={(_root.panel != null)}, labelText=\"{_label.text}\"");
					}
					break;
				case Phase.Entrance: {
					_elapsed += dt;
					float t = Mathf.Clamp01(_elapsed / EntranceDuration);
					float scale = Mathf.Lerp(EntranceStartScale, 1f, t);
					_root.style.scale = new Scale(new Vector3(scale, scale, 1f));
					if (t >= 1f) {
						_phase = Phase.Hold;
						_elapsed = 0f;
						Debug.Log($"[FlyText] Phase Entrance->Hold: display={_root.resolvedStyle.display}, opacity={_root.resolvedStyle.opacity}, worldBound={_root.worldBound}");
					}
					break;
				}
				case Phase.Hold:
					_elapsed += dt;
					if (_elapsed >= HoldDuration) {
						_phase = Phase.Exit;
						_elapsed = 0f;
						Debug.Log("[FlyText] Phase Hold->Exit");
					}
					break;
				case Phase.Exit: {
					_elapsed += dt;
					float et = Mathf.Clamp01(_elapsed / ExitDuration);
					_root.style.translate = new Translate(0, Mathf.Lerp(0, ExitMoveDownPx, et), 0);
					float exitScale = Mathf.Lerp(1f, ExitEndScale, et);
					_root.style.scale = new Scale(new Vector3(exitScale, exitScale, 1f));
					_root.style.opacity = Mathf.Lerp(1f, 0f, et);
					if (et >= 1f) {
						HideAndReset();
						_phase = Phase.Idle;
						Debug.Log("[FlyText] Phase Exit->Idle: hidden and reset");
					}
					break;
				}
			}
		}

		void StartEntrance(string text) {
			_label.text = text;
			_root.style.opacity = 1f;
			_root.style.translate = new Translate(0, 0, 0);
			_root.style.scale = new Scale(new Vector3(EntranceStartScale, EntranceStartScale, 1f));
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

using System.Collections.Generic;
using UnityEngine.UIElements;
using GS.Main;
using GS.Unity.Map;

namespace GS.Unity.UI {
	public class ActionLogView {
		const float FadeInSeconds = 0.25f;
		const float FadeOutSeconds = 0.6f;
		const float TopGapPx = 6f;
		const float BottomReservedOffsetPx = 280f; // clear selected country/org bar (matches map-controls-panel bottom)
		const float WidthMultiplier = 1.5f;
		const float RightPx = 6f;

		readonly VisualElement _root;
		readonly VisualElement _content;
		readonly VisualElement _topRightPanel;
		readonly VisualElement _hudRoot;
		readonly ILocalization _loc;
		readonly CountryVisualConfig _countryVisualConfig;
		readonly OrgVisualConfig _orgVisualConfig;
		readonly Dictionary<long, LogEntry> _rendered = new();

		public ActionLogView(VisualElement hudRoot, VisualElement root, VisualElement topRightPanel,
			ILocalization loc, CountryVisualConfig countryVisualConfig, OrgVisualConfig orgVisualConfig) {
			_hudRoot = hudRoot;
			_root = root;
			_topRightPanel = topRightPanel;
			_loc = loc;
			_countryVisualConfig = countryVisualConfig;
			_orgVisualConfig = orgVisualConfig;
			_content = root.Q<VisualElement>("action-log-content");
			SetPickingIgnoreRecursive(_root);
			_root.style.bottom = BottomReservedOffsetPx;
			_topRightPanel.RegisterCallback<GeometryChangedEvent>(_ => RepositionAndResize());
			RepositionAndResize();
		}

		void RepositionAndResize() {
			var hudBound = _hudRoot.worldBound;
			var trBound = _topRightPanel.worldBound;
			float width = trBound.width * WidthMultiplier;
			_root.style.width = width;
			_root.style.right = RightPx;
			_root.style.top = (trBound.yMax - hudBound.yMin) + TopGapPx;
		}

		public void Refresh(GameLogState state) {
			var currentIds = new HashSet<long>();
			foreach (var entry in state.Entries) {
				currentIds.Add(entry.SequenceId);
				if (_rendered.ContainsKey(entry.SequenceId)) { continue; }
				var label = BuildLabel(entry);
				_content.Add(label);
				label.style.opacity = 0f;
				label.style.transitionProperty = new List<StylePropertyName> { new StylePropertyName("opacity") };
				label.style.transitionDuration = new List<TimeValue> { new TimeValue(FadeInSeconds, TimeUnit.Second) };
				var fadeInSchedule = label.schedule.Execute(() => label.style.opacity = 1f);
				fadeInSchedule.ExecuteLater(20);
				_rendered[entry.SequenceId] = new LogEntry(label, fadeInSchedule);
			}
			var toEvict = new List<long>();
			foreach (var id in _rendered.Keys) {
				if (!currentIds.Contains(id)) { toEvict.Add(id); }
			}
			foreach (var id in toEvict) {
				var entry = _rendered[id];
				_rendered.Remove(id);
				EvictEntry(entry);
			}
		}

		void EvictEntry(LogEntry entry) {
			var label = entry.Label;
			entry.FadeInSchedule?.Pause();

			// Nothing to transition from: not attached, or already fully faded — remove now.
			if (label.panel == null || label.resolvedStyle.opacity <= 0f) {
				label.RemoveFromHierarchy();
				return;
			}

			bool armed = false;
			bool cleanedUp = false;
			EventCallback<TransitionRunEvent> onRun = null;
			EventCallback<TransitionEndEvent> onEnd = null;
			EventCallback<TransitionCancelEvent> onCancel = null;
			EventCallback<DetachFromPanelEvent> onDetach = null;

			void CleanUp() {
				if (cleanedUp) { return; }
				cleanedUp = true;
				label.UnregisterCallback(onRun);
				label.UnregisterCallback(onEnd);
				label.UnregisterCallback(onCancel);
				label.UnregisterCallback(onDetach);
				entry.FadeInSchedule?.Pause();
				label.RemoveFromHierarchy();
			}

			onRun = evt => {
				if (evt.target != label || !evt.stylePropertyNames.Contains(new StylePropertyName("opacity"))) { return; }
				armed = true;
			};
			onEnd = evt => {
				if (evt.target != label || !evt.stylePropertyNames.Contains(new StylePropertyName("opacity"))) { return; }
				if (!armed) { return; }
				CleanUp();
			};
			onCancel = evt => {
				if (evt.target != label || !evt.stylePropertyNames.Contains(new StylePropertyName("opacity"))) { return; }
				if (!armed) { return; }
				CleanUp();
			};
			onDetach = _ => CleanUp();

			label.RegisterCallback(onRun);
			label.RegisterCallback(onEnd);
			label.RegisterCallback(onCancel);
			label.RegisterCallback(onDetach);

			label.style.transitionDuration = new List<TimeValue> { new TimeValue(FadeOutSeconds, TimeUnit.Second) };
			label.style.opacity = 0f;

			// State-based fallback: if no transition ever ran (e.g. effectively zero duration
			// in this environment) but opacity already resolved to invisible, clean up next frame
			// rather than waiting on an event that will never fire. Not a duration guess.
			label.schedule.Execute(() => {
				if (!armed && label.resolvedStyle.opacity <= 0f) { CleanUp(); }
			}).ExecuteLater(0);
		}

		internal sealed class LogEntry {
			public Label Label { get; }
			public IVisualElementScheduledItem FadeInSchedule { get; set; }

			public LogEntry(Label label, IVisualElementScheduledItem fadeInSchedule) {
				Label = label;
				FadeInSchedule = fadeInSchedule;
			}
		}

		Label BuildLabel(GameLogEntry entry) {
			string text = entry.Kind switch {
				GameLogEntryKind.Control => GameLogLineFormatter.BuildControlLine(entry, _loc, _countryVisualConfig, _orgVisualConfig),
				GameLogEntryKind.Opinion => GameLogLineFormatter.BuildOpinionLine(entry, _loc, _countryVisualConfig, _orgVisualConfig),
				GameLogEntryKind.Relation => GameLogLineFormatter.BuildRelationLine(entry, _loc, _countryVisualConfig, _orgVisualConfig),
				GameLogEntryKind.War => GameLogLineFormatter.BuildWarLine(entry, _loc, _countryVisualConfig, _orgVisualConfig),
				GameLogEntryKind.WarResolved => GameLogLineFormatter.BuildWarResolvedLine(entry, _loc, _countryVisualConfig, _orgVisualConfig),
				GameLogEntryKind.NewCharacter => GameLogLineFormatter.BuildNewCharacterLine(entry, _loc, _countryVisualConfig, _orgVisualConfig),
				_ => ""
			};
			var label = new Label(text) { enableRichText = true };
			label.AddToClassList("gs-label");
			label.AddToClassList("action-log-entry");
			SetPickingIgnoreRecursive(label);
			return label;
		}

		static void SetPickingIgnoreRecursive(VisualElement el) {
			el.pickingMode = PickingMode.Ignore;
			foreach (var child in el.Children()) {
				SetPickingIgnoreRecursive(child);
			}
		}
	}
}

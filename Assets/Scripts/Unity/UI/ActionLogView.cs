using System.Collections.Generic;
using UnityEngine.UIElements;
using GS.Main;
using GS.Unity.Map;

namespace GS.Unity.UI {
	class ActionLogView {
		const float FadeInSeconds = 0.25f;
		const float FadeOutSeconds = 0.6f;
		const float TopGapPx = 6f;
		const float BottomReservedOffsetPx = 160f; // representative closed-state height of the bottom-bar panel
		const float WidthMultiplier = 1.5f;
		const float RightPx = 6f;

		readonly VisualElement _root;
		readonly VisualElement _content;
		readonly VisualElement _topRightPanel;
		readonly VisualElement _hudRoot;
		readonly ILocalization _loc;
		readonly CountryVisualConfig _countryVisualConfig;
		readonly OrgVisualConfig _orgVisualConfig;
		readonly Dictionary<long, Label> _rendered = new();

		public ActionLogView(VisualElement hudRoot, VisualElement root, VisualElement topRightPanel,
			ILocalization loc, CountryVisualConfig countryVisualConfig, OrgVisualConfig orgVisualConfig) {
			_hudRoot = hudRoot;
			_root = root;
			_topRightPanel = topRightPanel;
			_loc = loc;
			_countryVisualConfig = countryVisualConfig;
			_orgVisualConfig = orgVisualConfig;
			_content = root.Q<VisualElement>("action-log-content");
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
				_rendered[entry.SequenceId] = label;
				label.style.opacity = 0f;
				label.style.transitionProperty = new List<StylePropertyName> { new StylePropertyName("opacity") };
				label.style.transitionDuration = new List<TimeValue> { new TimeValue(FadeInSeconds, TimeUnit.Second) };
				label.schedule.Execute(() => label.style.opacity = 1f).ExecuteLater(20);
			}
			var toEvict = new List<long>();
			foreach (var id in _rendered.Keys) {
				if (!currentIds.Contains(id)) { toEvict.Add(id); }
			}
			foreach (var id in toEvict) {
				var label = _rendered[id];
				_rendered.Remove(id);
				label.style.transitionDuration = new List<TimeValue> { new TimeValue(FadeOutSeconds, TimeUnit.Second) };
				label.style.opacity = 0f;
				label.schedule.Execute(() => label.RemoveFromHierarchy()).ExecuteLater((long)(FadeOutSeconds * 1000));
			}
		}

		Label BuildLabel(GameLogEntry entry) {
			string text = entry.Kind switch {
				GameLogEntryKind.Discovery => GameLogLineFormatter.BuildDiscoveryLine(entry, _loc, _countryVisualConfig, _orgVisualConfig),
				GameLogEntryKind.Control => GameLogLineFormatter.BuildControlLine(entry, _loc, _countryVisualConfig, _orgVisualConfig),
				GameLogEntryKind.Opinion => GameLogLineFormatter.BuildOpinionLine(entry, _loc, _countryVisualConfig, _orgVisualConfig),
				GameLogEntryKind.NewCharacter => GameLogLineFormatter.BuildNewCharacterLine(entry, _loc, _countryVisualConfig, _orgVisualConfig),
				_ => ""
			};
			var label = new Label(text) { enableRichText = true };
			label.AddToClassList("gs-label");
			label.AddToClassList("action-log-entry");
			return label;
		}
	}
}

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using GS.Unity.UI;

namespace GS.Unity.E2E {
	public readonly struct ElementResolveResult {
		public bool Success { get; }
		public VisualElement Element { get; }
		public PanelRenderer Document { get; }
		public string Error { get; }

		ElementResolveResult(bool success, VisualElement element, PanelRenderer document, string error) {
			Success = success;
			Element = element;
			Document = document;
			Error = error;
		}

		public static ElementResolveResult Ok(VisualElement element, PanelRenderer document) {
			return new ElementResolveResult(true, element, document, null);
		}

		public static ElementResolveResult Fail(string error) {
			return new ElementResolveResult(false, null, null, error);
		}
	}

	public static class E2EElementResolver {
		const int OfferCap = 40;

		public static PanelRenderer[] LiveDocuments() {
			var found = UnityEngine.Object.FindObjectsByType<PanelRenderer>(FindObjectsInactive.Exclude);
			Array.Sort(found, (a, b) => b.sortingOrder.CompareTo(a.sortingOrder));
			return found;
		}

		public static PanelRenderer TopmostVisibleDocument() {
			foreach (var document in LiveDocuments()) {
				if (IsDocumentVisible(document)) {
					return document;
				}
			}
			return null;
		}

		public static ElementResolveResult Resolve(string name, string label) {
			var documents = LiveDocuments();
			if (!string.IsNullOrEmpty(name)) {
				foreach (var document in documents) {
					if (!IsDocumentVisible(document)) {
						continue;
					}
					var match = Root(document).Q(name);
					if (IsInteractive(match)) {
						return ElementResolveResult.Ok(match, document);
					}
				}
			}

			if (!string.IsNullOrEmpty(label)) {
				VisualElement contains = null;
				PanelRenderer containsDoc = null;
				foreach (var document in documents) {
					if (!IsDocumentVisible(document)) {
						continue;
					}
					var exact = FindByLabel(Root(document), label, exact: true);
					if (IsInteractive(exact)) {
						return ElementResolveResult.Ok(exact, document);
					}
					if (contains == null) {
						var partial = FindByLabel(Root(document), label, exact: false);
						if (IsInteractive(partial)) {
							contains = partial;
							containsDoc = document;
						}
					}
				}
				if (contains != null) {
					return ElementResolveResult.Ok(contains, containsDoc);
				}
			}

			return ElementResolveResult.Fail(DescribeFailure(name, label, documents));
		}

		public static bool IsInteractive(VisualElement element) {
			return element != null
				&& element.enabledInHierarchy
				&& IsVisible(element);
		}

		public static bool TryScreenPoint(VisualElement element, out Vector2 screenPoint, out string error) {
			screenPoint = default;
			error = null;
			var panel = element.panel;
			if (panel == null) {
				error = "element has no panel";
				return false;
			}

			float panelWidth = panel.visualTree.layout.width;
			if (panelWidth <= 0f) {
				error = "panel width is zero";
				return false;
			}

			float scale = Screen.width / panelWidth;
			Rect bound = element.worldBound;
			Vector2 panelPoint = bound.center;
			// ScreenToPanel expects Y-down coordinates (0 at the top), matching panel space
			// and UIPointerState. Input System MouseState.position is Y-up.
			Vector2 yDownScreen = new Vector2(panelPoint.x * scale, panelPoint.y * scale);
			Vector2 roundTrip = RuntimePanelUtils.ScreenToPanel(panel, yDownScreen);
			if (!bound.Contains(roundTrip)) {
				error = $"screen point {yDownScreen} round-tripped to {roundTrip}, outside {bound}";
				return false;
			}
			screenPoint = new Vector2(yDownScreen.x, Screen.height - yDownScreen.y);
			return true;
		}

		public static VisualElement FindFirstButton(VisualElement root) {
			if (root == null) {
				return null;
			}
			if (root is Button button && IsInteractive(button)) {
				return button;
			}
			for (int i = 0; i < root.hierarchy.childCount; i++) {
				var found = FindFirstButton(root.hierarchy[i]);
				if (found != null) {
					return found;
				}
			}
			return null;
		}

		static VisualElement Root(PanelRenderer document) {
			return PanelRendererRoot.Get(document);
		}

		static bool IsOverlay(PanelRenderer document) {
			return document != null && document.gameObject.name == "FlyTextUI";
		}

		// Hidden windows keep an active PanelRenderer and a high sortingOrder (EndGame 1100,
		// GameMenu 990, Leaderboard 500, …). LiveDocuments() therefore lists them above the HUD
		// even when their content is display:none. "Visible" here means the panel is actually
		// showing, matching TopmostVisibleDocument / the snapshot screen.
		static bool IsDocumentVisible(PanelRenderer document) {
			if (document == null || IsOverlay(document)) {
				return false;
			}
			return HasVisibleContent(Root(document));
		}

		static bool HasVisibleContent(VisualElement element) {
			if (!IsVisible(element)) {
				return false;
			}
			if (element.hierarchy.childCount == 0) {
				return true;
			}
			for (int i = 0; i < element.hierarchy.childCount; i++) {
				if (HasVisibleContent(element.hierarchy[i])) {
					return true;
				}
			}
			return false;
		}

		static bool IsVisible(VisualElement element) {
			return element != null
				&& element.style.display != DisplayStyle.None
				&& element.resolvedStyle.display != DisplayStyle.None
				&& element.worldBound.width > 0
				&& element.worldBound.height > 0;
		}

		static VisualElement FindByLabel(VisualElement root, string label, bool exact) {
			if (root == null) {
				return null;
			}
			if (MatchesLabel(root, label, exact)) {
				return root;
			}
			for (int i = 0; i < root.hierarchy.childCount; i++) {
				var found = FindByLabel(root.hierarchy[i], label, exact);
				if (found != null) {
					return found;
				}
			}
			return null;
		}

		static bool MatchesLabel(VisualElement element, string label, bool exact) {
			string text = null;
			if (element is Button button) {
				text = button.text;
			} else if (element is Label lbl) {
				text = lbl.text;
			}
			if (string.IsNullOrEmpty(text)) {
				return false;
			}
			return exact
				? string.Equals(text, label, StringComparison.OrdinalIgnoreCase)
				: text.IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0;
		}

		static string DescribeFailure(string name, string label, PanelRenderer[] documents) {
			var sb = new StringBuilder();
			sb.Append("Looked for ");
			if (!string.IsNullOrEmpty(name)) {
				sb.Append("name '").Append(name).Append("'");
			}
			if (!string.IsNullOrEmpty(label)) {
				if (!string.IsNullOrEmpty(name)) {
					sb.Append(" or ");
				}
				sb.Append("label '").Append(label).Append("'");
			}
			sb.Append(". Offered on the topmost panel: ");
			PanelRenderer topmost = null;
			for (int i = 0; i < documents.Length; i++) {
				if (IsDocumentVisible(documents[i])) {
					topmost = documents[i];
					break;
				}
			}
			if (topmost == null || Root(topmost) == null) {
				sb.Append("(none)");
				return sb.ToString();
			}

			int count = 0;
			CollectOffers(Root(topmost), sb, ref count);
			if (count == 0) {
				sb.Append("(none)");
			}
			return sb.ToString();
		}

		static void CollectOffers(VisualElement element, StringBuilder sb, ref int count) {
			if (element == null || count >= OfferCap || !IsVisible(element)) {
				return;
			}
			if (!string.IsNullOrEmpty(element.name)) {
				if (count > 0) {
					sb.Append(", ");
				}
				sb.Append(element.name);
				count++;
			} else if (element is Button button && !string.IsNullOrEmpty(button.text)) {
				if (count > 0) {
					sb.Append(", ");
				}
				sb.Append('"').Append(button.text).Append('"');
				count++;
			} else if (element is Label lbl && !string.IsNullOrEmpty(lbl.text)) {
				if (count > 0) {
					sb.Append(", ");
				}
				sb.Append('"').Append(lbl.text).Append('"');
				count++;
			}
			for (int i = 0; i < element.hierarchy.childCount && count < OfferCap; i++) {
				CollectOffers(element.hierarchy[i], sb, ref count);
			}
		}
	}
}

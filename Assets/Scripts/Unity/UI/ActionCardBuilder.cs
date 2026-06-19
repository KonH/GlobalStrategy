using UnityEngine;
using UnityEngine.UIElements;

namespace GS.Unity.UI {
	static class ActionCardBuilder {
		public struct CardResult {
			public VisualElement Card;
			public VisualElement Body;
			public Label SuccessPct;
			public Label CostLabel;
		}

		public static CardResult Build(string name, string desc, string successPct, string goldCostText, Sprite art) {
			var card = new VisualElement();
			card.AddToClassList("action-card");
			var result = Populate(card, name, desc, successPct, goldCostText, art);
			result.Card = card;
			return result;
		}

		public static CardResult PopulateSlot(VisualElement slot, string name, string desc, string successPct, string goldCostText, Sprite art) {
			slot.Clear();
			slot.RemoveFromClassList("action-card--success");
			slot.RemoveFromClassList("action-card--fail");
			var result = Populate(slot, name, desc, successPct, goldCostText, art);
			result.Card = slot;
			return result;
		}

		static CardResult Populate(VisualElement container, string name, string desc, string successPct, string goldCostText, Sprite art) {
			var header = new Label(name);
			header.AddToClassList("action-card-header");
			container.Add(header);
			SetupHeaderAutoSize(header);

			var artEl = new VisualElement();
			artEl.AddToClassList("action-card-art");
			if (art != null) {
				artEl.style.backgroundImage = new StyleBackground(art);
			}
			container.Add(artEl);

			var body = new VisualElement();
			body.AddToClassList("action-card-body");

			var descLabel = new Label(desc);
			descLabel.AddToClassList("action-card-desc");
			body.Add(descLabel);
			SetupDescAutoSize(descLabel);

			var footer = new VisualElement();
			footer.AddToClassList("action-card-footer");

			var pctLabel = new Label(successPct);
			pctLabel.AddToClassList("action-card-success-pct");
			footer.Add(pctLabel);

			Label costLabel = null;
			if (!string.IsNullOrEmpty(goldCostText)) {
				var costRow = new VisualElement();
				costRow.AddToClassList("action-card-cost");
				costLabel = new Label(goldCostText);
				costLabel.AddToClassList("action-card-cost-label");
				costRow.Add(costLabel);
				var costIcon = new VisualElement();
				costIcon.AddToClassList("action-card-cost-icon");
				costRow.Add(costIcon);
				footer.Add(costRow);
			}

			body.Add(footer);
			container.Add(body);

			return new CardResult { Body = body, SuccessPct = pctLabel, CostLabel = costLabel };
		}

		static void SetupDescAutoSize(Label desc, float minSize = 11f) {
			desc.RegisterCallback<GeometryChangedEvent>(_ => {
				float availH = desc.resolvedStyle.height;
				float availW = desc.resolvedStyle.width;
				if (availH <= 0 || availW <= 0) { return; }
				var measured = desc.MeasureTextSize(
					desc.text, availW, VisualElement.MeasureMode.AtMost, float.PositiveInfinity, VisualElement.MeasureMode.Undefined);
				if (measured.y > availH + 0.5f) {
					float cur = desc.resolvedStyle.fontSize;
					if (cur > minSize) {
						float scale = availH / measured.y;
						float newSize = Mathf.Max(Mathf.Floor(cur * scale), minSize);
						if (newSize < cur) { desc.style.fontSize = newSize; }
					}
				}
			});
		}

		static void SetupHeaderAutoSize(Label header, float minSize = 12f) {
			header.RegisterCallback<GeometryChangedEvent>(_ => {
				float availW = header.resolvedStyle.width;
				if (availW <= 0) { return; }
				var measured = header.MeasureTextSize(
					header.text, float.PositiveInfinity, VisualElement.MeasureMode.Undefined,
					float.PositiveInfinity, VisualElement.MeasureMode.Undefined);
				if (measured.x > availW + 0.5f) {
					float cur = header.resolvedStyle.fontSize;
					if (cur > minSize) {
						float scale = availW / measured.x;
						float newSize = Mathf.Max(Mathf.Floor(cur * scale), minSize);
						if (newSize < cur) { header.style.fontSize = newSize; }
					}
				}
			});
		}
	}
}
